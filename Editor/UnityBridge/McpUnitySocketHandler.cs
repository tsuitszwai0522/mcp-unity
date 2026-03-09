using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using McpUnity.Tools;
using McpUnity.Resources;
using Unity.EditorCoroutines.Editor;
using System.Collections;
using System.Collections.Specialized;
using McpUnity.Utils;

namespace McpUnity.Unity
{
    /// <summary>
    /// WebSocket handler for MCP Unity communications
    /// </summary>
    public class McpUnitySocketHandler : WebSocketBehavior
    {
        private readonly McpUnityServer _server;

        // In-flight request tracking — populated in OnMessage, read by OnError so the
        // "WebSocket error: An error has occurred in sending data" log can be attributed
        // to the request that triggered it (method, id, payload size, elapsed time).
        private string _lastMethod;
        private string _lastRequestId;
        private int _lastResponseSize;
        private DateTime _lastRequestStartUtc;
        private bool _lastSendAttempted;

        /// <summary>
        /// Default constructor required by WebSocketSharp
        /// </summary>
        public McpUnitySocketHandler(McpUnityServer server)
        {
            _server = server;
        }
        
        /// <summary>
        /// Create a standardized error response
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="errorType">Type of error</param>
        /// <returns>A JObject containing the error information</returns>
        public static JObject CreateErrorResponse(string message, string errorType)
        {
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["type"] = errorType,
                    ["message"] = message
                }
            };
        }

        /// <summary>
        /// Handle list_tools request — returns external (non-built-in) tools for dynamic registration.
        /// </summary>
        private JObject HandleListTools()
        {
            var mcpAssembly = typeof(McpToolBase).Assembly;
            var tools = new JArray();

            foreach (var kvp in _server.Tools)
            {
                // Only return external tools. Built-in tools live in the main McpUnity.Editor
                // assembly. First-party sub-assemblies (e.g. McpUnity.Localization) ship hand-written
                // TS wrappers, so they're excluded from dynamic registration via either:
                //   1. [McpUnityFirstParty] attribute (preferred, explicit), OR
                //   2. assembly name prefix "McpUnity." (fallback for unmarked tools)
                var toolType = kvp.Value.GetType();
                if (toolType.Assembly == mcpAssembly) continue;
                if (toolType.GetCustomAttribute<McpUnityFirstPartyAttribute>() != null) continue;
                if (toolType.Assembly.GetName().Name.StartsWith("McpUnity.")) continue;

                tools.Add(new JObject
                {
                    ["name"] = kvp.Value.Name,
                    ["description"] = kvp.Value.Description,
                    ["parameterSchema"] = kvp.Value.ParameterSchema,
                    ["isAsync"] = kvp.Value.IsAsync
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["tools"] = tools,
                ["count"] = tools.Count
            };
        }

        /// <summary>
        /// Handle incoming messages from WebSocket clients
        /// </summary>
        protected override async void OnMessage(MessageEventArgs e)
        {
            try
            {
                McpLogger.LogInfo($"WebSocket message received: {e.Data}");
                JObject requestJson;
                try
                {
                    requestJson = JObject.Parse(e.Data);
                }
                catch (JsonReaderException jre)
                {
                    McpLogger.LogError($"Invalid JSON received: {jre.Message}. Data: {e.Data}");
                    // Attempt to send a parse error response. No requestId is available yet.
                    Send(CreateResponse(null, CreateErrorResponse($"Invalid JSON format: {jre.Message}", "invalid_json")).ToString(Formatting.None));
                    return;
                }

                var method = requestJson["method"]?.ToString();
                var parameters = requestJson["params"] as JObject ?? new JObject();
                var requestId = requestJson["id"]?.ToString();

                // Track for OnError diagnostics. Cleared on the next OnMessage / OnClose.
                _lastMethod = method;
                _lastRequestId = requestId;
                _lastResponseSize = 0;
                _lastRequestStartUtc = DateTime.UtcNow;
                _lastSendAttempted = false;

                // We need to dispatch to Unity's main thread and wait for completion
                var tcs = new TaskCompletionSource<JObject>();
                
                if (string.IsNullOrEmpty(method))
                {
                    tcs.SetResult(CreateErrorResponse("Missing method in request", "invalid_request"));
                }
                else if (method == "list_tools")
                {
                    tcs.SetResult(HandleListTools());
                }
                else if (_server.TryGetTool(method, out var tool))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
                }
                else if (_server.TryGetResource(method, out var resource))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResourceCoroutine(resource, parameters, tcs));
                }
                else
                {
                    tcs.SetResult(CreateErrorResponse($"Unknown method: {method}", "unknown_method"));
                }
                
                JObject responseJson = await tcs.Task;
                JObject jsonRpcResponse = CreateResponse(requestId, responseJson);
                string responseStr = jsonRpcResponse.ToString(Formatting.None);

                McpLogger.LogInfo($"WebSocket message response for request ID '{requestId}': {responseStr}");

                // Capture payload size before Send so OnError can report it if Send raises.
                _lastResponseSize = responseStr?.Length ?? 0;
                _lastSendAttempted = true;

                // Send the response back to the client
                Send(responseStr);
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error processing message: {ex.Message}");
                
                Send(CreateErrorResponse($"Internal server error: {ex.Message}", "internal_error").ToString(Formatting.None));
            }
        }
        
        /// <summary>
        /// Handle WebSocket connection open.
        /// Supports multiple concurrent MCP clients (e.g. multiple Claude Code instances).
        /// File descriptor accumulation from reconnection cycles is mitigated on the
        /// Node.js side via maxReconnectAttempts and socket.terminate().
        /// See: https://github.com/CoderGamester/mcp-unity/issues/110
        /// </summary>
        protected override void OnOpen()
        {
            // Extract client name from the X-Client-Name header (if available)
            string clientName = "";
            NameValueCollection headers = Context.Headers;
            if (headers != null && headers.Contains("X-Client-Name"))
            {
                clientName = headers["X-Client-Name"];
            }

            // Add the client to the server's tracking dictionary
            _server.Clients[ID] = clientName;

            McpLogger.LogInfo($"WebSocket client connected (ID: {ID}, Name: {(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)}, Total clients: {_server.Clients.Count})");
        }
        
        /// <summary>
        /// Handle WebSocket connection close
        /// </summary>
        protected override void OnClose(CloseEventArgs e)
        {
            _server.Clients.TryGetValue(ID, out string clientName);

            // Remove the client from the server
            _server.Clients.TryRemove(ID, out _);

            McpLogger.LogInfo($"WebSocket client '{clientName}' disconnected: {e.Reason} (Remaining clients: {_server.Clients.Count})");

            // Clear in-flight tracking so a later OnError on a different session
            // doesn't get misattributed to this session's last request.
            _lastMethod = null;
            _lastRequestId = null;
            _lastResponseSize = 0;
            _lastSendAttempted = false;
        }

        /// <summary>
        /// Handle WebSocket errors.
        /// Logs the underlying exception and the in-flight request context so we can
        /// attribute "An error has occurred in sending data" to a specific tool call,
        /// payload size, and elapsed time. See doc/lessons/unity-mcp-lessons.md for
        /// known triggers (oversized payloads, client reconnect kicking stale sessions).
        /// </summary>
        protected override void OnError(ErrorEventArgs e)
        {
            var sb = new StringBuilder();
            sb.Append("WebSocket error: ").Append(e?.Message ?? "(no message)");

            if (e?.Exception != null)
            {
                sb.Append("\n  Exception: ").Append(e.Exception.GetType().Name)
                  .Append(": ").Append(e.Exception.Message);
                if (e.Exception.InnerException != null)
                {
                    sb.Append("\n  InnerException: ").Append(e.Exception.InnerException.GetType().Name)
                      .Append(": ").Append(e.Exception.InnerException.Message);
                }
                if (!string.IsNullOrEmpty(e.Exception.StackTrace))
                {
                    sb.Append("\n  StackTrace: ").Append(e.Exception.StackTrace);
                }
            }

            if (!string.IsNullOrEmpty(_lastMethod) || !string.IsNullOrEmpty(_lastRequestId))
            {
                var elapsedMs = (DateTime.UtcNow - _lastRequestStartUtc).TotalMilliseconds;
                sb.Append("\n  Last request: method=").Append(_lastMethod ?? "(null)")
                  .Append(", id=").Append(_lastRequestId ?? "(null)")
                  .Append(", elapsedMs=").Append(elapsedMs.ToString("F0"))
                  .Append(", responseSize=").Append(_lastResponseSize)
                  .Append(", sendAttempted=").Append(_lastSendAttempted);
            }
            else
            {
                sb.Append("\n  Last request: (none — error not tied to an in-flight OnMessage)");
            }

            int otherClients = 0;
            try
            {
                foreach (var key in _server.Clients.Keys)
                {
                    if (key != ID) otherClients++;
                }
            }
            catch
            {
                // Defensive: clients dict shouldn't throw, but error path must stay safe.
            }
            sb.Append("\n  Session: ID=").Append(ID)
              .Append(", otherClients=").Append(otherClients);

            McpLogger.LogError(sb.ToString());
        }
        
        /// <summary>
        /// Execute a tool with the provided parameters
        /// </summary>
        private IEnumerator ExecuteTool(McpToolBase tool, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (tool.IsAsync)
                {
                    tool.ExecuteAsync(parameters, tcs);
                }
                else
                {
                    var result = tool.Execute(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error executing tool {tool.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to execute tool {tool.Name}: {ex.Message}",
                    "tool_execution_error"
                ));
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Fetch a resource with the provided parameters
        /// </summary>
        private IEnumerator FetchResourceCoroutine(McpResourceBase resource, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (resource.IsAsync)
                {
                    resource.FetchAsync(parameters, tcs);
                }
                else
                {
                    var result = resource.Fetch(parameters);
                    tcs.SetResult(result);
                }
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Error fetching resource {resource.Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(CreateErrorResponse(
                    $"Failed to fetch resource {resource.Name}: {ex.Message}",
                    "resource_fetch_error"
                ));
            }
            yield return null;
        }
        
        /// <summary>
        /// Create a JSON-RPC 2.0 response
        /// </summary>
        /// <param name="requestId">Request ID</param>
        /// <param name="result">Result object</param>
        /// <returns>JSON-RPC 2.0 response</returns>
        private JObject CreateResponse(string requestId, JObject result)
        {
            // Format as JSON-RPC 2.0 response
            JObject jsonRpcResponse = new JObject
            {
                ["id"] = requestId
            };
            
            // Add result or error
            if (result.TryGetValue("error", out var errorObj))
            {
                jsonRpcResponse["error"] = errorObj;
            }
            else
            {
                jsonRpcResponse["result"] = result;
            }
            
            return jsonRpcResponse;
        }
    }
}
