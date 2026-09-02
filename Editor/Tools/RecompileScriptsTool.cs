using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[assembly: InternalsVisibleTo("McpUnity.Editor.Tests")]

namespace McpUnity.Tools {
    /// <summary>
    /// Tool to recompile all scripts in the Unity project
    /// </summary>
    public class RecompileScriptsTool : McpToolBase
    {
        private class CompilationRequest
        {
            public readonly bool ReturnWithLogs;
            public readonly int LogsLimit;
            public readonly TaskCompletionSource<JObject> CompletionSource;
            public readonly bool Piggybacked;
            public string StatusMessage { get; private set; }
            public bool Refreshed { get; private set; }
            public int RefreshDurationMs { get; private set; }
            public bool? CompilationWasAlreadyInProgress { get; private set; }

            public CompilationRequest(
                bool returnWithLogs,
                int logsLimit,
                TaskCompletionSource<JObject> completionSource,
                string refreshStatusMessage = null,
                bool piggybacked = false)
            {
                ReturnWithLogs = returnWithLogs;
                LogsLimit = logsLimit;
                CompletionSource = completionSource;
                StatusMessage = refreshStatusMessage;
                Piggybacked = piggybacked;
            }

            public void RecordRefresh(int refreshDurationMs)
            {
                Refreshed = true;
                RefreshDurationMs = refreshDurationMs;
            }

            public void RecordCompilationState(bool wasAlreadyInProgress, bool refreshAssets)
            {
                CompilationWasAlreadyInProgress = wasAlreadyInProgress;
                if (!wasAlreadyInProgress)
                {
                    return;
                }

                AppendStatusMessage(refreshAssets
                    ? "Unity was already compiling before AssetDatabase.Refresh. This response does not confirm that changes discovered by the refresh were compiled; they may compile in a later cycle."
                    : "Unity was already compiling when this request began. This response describes that pre-existing compilation rather than a compilation started by recompile_scripts.");
            }

            private void AppendStatusMessage(string message)
            {
                StatusMessage = string.IsNullOrEmpty(StatusMessage)
                    ? message
                    : $"{StatusMessage} {message}";
            }
        }
        
        private class CompilationResult 
        {
            public readonly List<CompilerMessage> SortedLogs;
            public readonly int WarningsCount;
            public readonly int ErrorsCount;
            
            public bool HasErrors => ErrorsCount > 0;
            
            public CompilationResult(List<CompilerMessage> sortedLogs, int warningsCount, int errorsCount) 
            {
                SortedLogs = sortedLogs;
                WarningsCount = warningsCount;
                ErrorsCount = errorsCount;
            }
        }
        
        private readonly List<CompilationRequest> _pendingRequests = new List<CompilationRequest>();
        private readonly List<CompilerMessage> _compilationLogs = new List<CompilerMessage>();
        private int _processedAssemblies = 0;

        // Test seams follow the same private static delegate pattern used by MenuItemTool.
        private static Action _refreshAssets = AssetDatabase.Refresh;
        private static Action _requestScriptCompilation = CompilationPipeline.RequestScriptCompilation;
        private static Func<bool> _isCompiling = () => EditorApplication.isCompiling;

        public RecompileScriptsTool()
        {
            Name = "recompile_scripts";
            Description = "Refreshes the AssetDatabase by default to discover file changes, then recompiles scripts. With refreshAssets=false, recompiles only scripts already known to the AssetDatabase and does not discover added or deleted files";
            IsAsync = true; // Compilation is asynchronous
        }

        /// <summary>
        /// Execute the Recompile tool asynchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        /// <param name="tcs">TaskCompletionSource to set the result or exception</param>
        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            // Extract and store parameters
            var returnWithLogs = GetBoolParameter(parameters, "returnWithLogs", true);
            var logsLimit = Mathf.Clamp(GetIntParameter(parameters, "logsLimit", 100), 0, 1000);
            var refreshAssets = GetBoolParameter(parameters, "refreshAssets", true);

            bool hasActiveRequest;
            CompilationRequest request;
            lock (_pendingRequests)
            {
                hasActiveRequest = _pendingRequests.Count > 0;
                string refreshStatusMessage = null;
                if (hasActiveRequest)
                {
                    refreshStatusMessage = refreshAssets
                        ? "AssetDatabase.Refresh was not run for this request because another compilation was already in progress. This response does not confirm that this request's file changes were included in the observed compilation."
                        : "This request piggybacked on another compilation and did not run AssetDatabase.Refresh. This response does not confirm that this request's file changes were included in the observed compilation.";
                }
                request = new CompilationRequest(
                    returnWithLogs,
                    logsLimit,
                    tcs,
                    refreshStatusMessage,
                    hasActiveRequest);
                _pendingRequests.Add(request);
            }

            if (hasActiveRequest)
            {
                McpLogger.LogInfo("Recompilation already in progress. Waiting for completion...");
                return;
            }
            
            // On first request, initialize compilation listeners and start compilation
            StartCompilationTracking();
            bool compilationWasAlreadyInProgress = _isCompiling();
            request.RecordCompilationState(compilationWasAlreadyInProgress, refreshAssets);

            if (refreshAssets)
            {
                Stopwatch refreshStopwatch = Stopwatch.StartNew();
                try
                {
                    _refreshAssets();
                    refreshStopwatch.Stop();
                    request.RecordRefresh(ToDurationMilliseconds(refreshStopwatch));
                }
                catch (Exception ex)
                {
                    refreshStopwatch.Stop();
                    request.RecordRefresh(ToDurationMilliseconds(refreshStopwatch));
                    FailPendingRequestsAfterRefresh(ex);
                    return;
                }
            }

            if (_isCompiling() == false)
            {
                McpLogger.LogInfo("Recompiling all scripts in the Unity project");
                _requestScriptCompilation();
            }
        }

        /// <summary>
        /// Subscribe to compilation events, reset tracked state
        /// </summary>
        private void StartCompilationTracking()
        {
            _compilationLogs.Clear();
            _processedAssemblies = 0;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }
        
        /// <summary>
        /// Unsubscribe from compilation events
        /// </summary>
        private void StopCompilationTracking()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
        }

        /// <summary>
        /// Record compilation logs for every single assembly
        /// </summary>
        private void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _processedAssemblies++;
            _compilationLogs.AddRange(messages);
        }

        /// <summary>
        /// Stop tracking and complete all pending requests
        /// </summary>
        private void OnCompilationFinished(object _)
        {
            McpLogger.LogInfo($"Recompilation completed. Processed {_processedAssemblies} assemblies with {_compilationLogs.Count} compiler messages");

            // Sort logs by type: first errors, then warnings and info
            List<CompilerMessage> sortedLogs = _compilationLogs.OrderBy(x => x.type).ToList();
            int errorsCount = _compilationLogs.Count(l => l.type == CompilerMessageType.Error);
            int warningsCount = _compilationLogs.Count(l => l.type == CompilerMessageType.Warning);
            CompilationResult result = new CompilationResult(sortedLogs, warningsCount, errorsCount);
            
            // Stop tracking before completing requests
            StopCompilationTracking();
            
            // Complete all requests received before compilation end, the next received request will start a new compilation
            List<CompilationRequest> requestsToComplete = new List<CompilationRequest>();
            
            lock (_pendingRequests)
            {
                requestsToComplete.AddRange(_pendingRequests);
                _pendingRequests.Clear();
            }

            foreach (var request in requestsToComplete)
            {
                CompleteRequest(request, result);
            }
        }

        /// <summary>
        /// Process a completed compilation request
        /// </summary>
        private static void CompleteRequest(CompilationRequest request, CompilationResult result)
        {
            request.CompletionSource.SetResult(BuildResponse(
                result.SortedLogs,
                result.WarningsCount,
                result.ErrorsCount,
                request.ReturnWithLogs,
                request.LogsLimit,
                request.Refreshed,
                request.RefreshDurationMs,
                request.StatusMessage,
                request.CompilationWasAlreadyInProgress,
                request.Piggybacked));
        }

        internal static JObject BuildResponse(
            IReadOnlyList<CompilerMessage> sortedLogs,
            int warningsCount,
            int errorsCount,
            bool returnWithLogs,
            int logsLimit,
            bool refreshed = false,
            int refreshDurationMs = 0,
            string refreshStatusMessage = null,
            bool? compilationWasAlreadyInProgress = null,
            bool piggybacked = false)
        {
            JArray logsArray = new JArray();
            IEnumerable<CompilerMessage> logsToReturn = returnWithLogs
                ? sortedLogs.Take(logsLimit)
                : Enumerable.Empty<CompilerMessage>();

            foreach (var message in logsToReturn)
            {
                var logObject = new JObject 
                {
                    ["message"] = message.message,
                    ["type"] = message.type.ToString()
                };

                // Add file information if available
                if (!string.IsNullOrEmpty(message.file))
                {
                    logObject["file"] = message.file;
                    logObject["line"] = message.line;
                    logObject["column"] = message.column;
                }

                logsArray.Add(logObject);
            }

            string summaryMessage;
            if (piggybacked)
            {
                summaryMessage = $"Observed completion of a compilation already tracked by another recompile_scripts request with {errorsCount} error(s) and {warningsCount} warning(s)";
            }
            else if (compilationWasAlreadyInProgress == true)
            {
                summaryMessage = $"Observed completion of a pre-existing compilation with {errorsCount} error(s) and {warningsCount} warning(s)";
            }
            else
            {
                summaryMessage = errorsCount > 0
                    ? $"Recompilation completed with {errorsCount} error(s) and {warningsCount} warning(s)"
                    : $"Successfully recompiled all scripts with {warningsCount} warning(s)";
            }

            summaryMessage += $" (returnWithLogs: {returnWithLogs}, logsLimit: {logsLimit})";
            if (!string.IsNullOrEmpty(refreshStatusMessage))
            {
                summaryMessage += $" {refreshStatusMessage}";
            }

            int totalLogs = sortedLogs.Count;
            int returnedLogs = logsArray.Count;

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = summaryMessage,
                ["logs"] = logsArray,
                ["totalLogs"] = totalLogs,
                ["returnedLogs"] = returnedLogs,
                ["truncated"] = returnedLogs < totalLogs,
                ["refreshed"] = refreshed,
                ["refreshDurationMs"] = refreshDurationMs,
                ["compilationWasAlreadyInProgress"] = compilationWasAlreadyInProgress.HasValue
                    ? JToken.FromObject(compilationWasAlreadyInProgress.Value)
                    : JValue.CreateNull()
            };
        }

        internal static JObject BuildRefreshFailureResponse(
            Exception exception,
            bool refreshed,
            int refreshDurationMs,
            string refreshStatusMessage = null,
            bool? compilationWasAlreadyInProgress = null)
        {
            string message = $"AssetDatabase.Refresh failed before script recompilation: {exception.Message}";
            if (!string.IsNullOrEmpty(refreshStatusMessage))
            {
                message += $" {refreshStatusMessage}";
            }

            return new JObject
            {
                ["success"] = false,
                ["type"] = "text",
                ["message"] = message,
                ["error_code"] = "asset_database_refresh_failed",
                ["logs"] = new JArray(),
                ["totalLogs"] = 0,
                ["returnedLogs"] = 0,
                ["truncated"] = false,
                ["refreshed"] = refreshed,
                ["refreshDurationMs"] = refreshDurationMs,
                ["compilationWasAlreadyInProgress"] = compilationWasAlreadyInProgress.HasValue
                    ? JToken.FromObject(compilationWasAlreadyInProgress.Value)
                    : JValue.CreateNull()
            };
        }

        private void FailPendingRequestsAfterRefresh(Exception exception)
        {
            StopCompilationTracking();

            List<CompilationRequest> requestsToComplete = new List<CompilationRequest>();
            lock (_pendingRequests)
            {
                requestsToComplete.AddRange(_pendingRequests);
                _pendingRequests.Clear();
            }

            McpLogger.LogError($"AssetDatabase.Refresh failed before script recompilation: {exception.Message}");
            foreach (CompilationRequest pendingRequest in requestsToComplete)
            {
                pendingRequest.CompletionSource.TrySetResult(BuildRefreshFailureResponse(
                    exception,
                    pendingRequest.Refreshed,
                    pendingRequest.RefreshDurationMs,
                    pendingRequest.StatusMessage,
                    pendingRequest.CompilationWasAlreadyInProgress));
            }
        }

        private static int ToDurationMilliseconds(Stopwatch stopwatch)
        {
            return (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Helper method to safely extract integer parameters with default values
        /// </summary>
        /// <param name="parameters">JObject containing parameters</param>
        /// <param name="key">Parameter key to extract</param>
        /// <param name="defaultValue">Default value if parameter is missing or invalid</param>
        /// <returns>Extracted integer value or default</returns>
        private static int GetIntParameter(JObject parameters, string key, int defaultValue)
        {
            if (parameters?[key] != null && int.TryParse(parameters[key].ToString(), out int value))
                return value;
            return defaultValue;
        }

        /// <summary>
        /// Helper method to safely extract boolean parameters with default values
        /// </summary>
        /// <param name="parameters">JObject containing parameters</param>
        /// <param name="key">Parameter key to extract</param>
        /// <param name="defaultValue">Default value if parameter is missing or invalid</param>
        /// <returns>Extracted boolean value or default</returns>
        private static bool GetBoolParameter(JObject parameters, string key, bool defaultValue)
        {
            if (parameters?[key] != null && bool.TryParse(parameters[key].ToString(), out bool value))
                return value;
            return defaultValue;
        }
    }
}
