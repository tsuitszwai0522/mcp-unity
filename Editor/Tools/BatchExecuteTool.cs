using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using McpUnity.Services;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using Unity.EditorCoroutines.Editor;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for executing multiple operations in a single batch request.
    /// Supports sequential execution, stop-on-error, and atomic rollback outside Prefab sessions.
    /// </summary>
    public class BatchExecuteTool : McpToolBase
    {
        private readonly McpUnityServer _server;

        public BatchExecuteTool(McpUnityServer server)
        {
            _server = server;
            Name = "batch_execute";
            Description = "Executes multiple tool operations in a single batch request. Reduces " +
                          "round-trips and enables Undo-backed atomic operations outside active " +
                          "Prefab contents sessions; atomic=true is rejected while a session is active. " +
                          "Atomic rollback only restores Undo-tracked in-memory state. Asset paths " +
                          "observed during the batch window are reported, may include other editor " +
                          "activity, and have no disk-reversion guarantee from Unity Undo.";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteBatchCoroutine(parameters, tcs));
        }

        private IEnumerator ExecuteBatchCoroutine(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            JArray operations = parameters["operations"] as JArray;
            bool stopOnError = parameters["stopOnError"]?.ToObject<bool?>() ?? true;
            bool atomic = parameters["atomic"]?.ToObject<bool?>() ?? false;

            if (atomic && !stopOnError)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "atomic requires stopOnError to be true.",
                    "validation_error"
                ));
                yield break;
            }

            // Validate operations array
            if (operations == null || operations.Count == 0)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "The 'operations' array is required and must contain at least one operation.",
                    "validation_error"
                ));
                yield break;
            }

            // Validate max operations (prevent abuse)
            if (operations.Count > 100)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "Maximum of 100 operations allowed per batch.",
                    "validation_error"
                ));
                yield break;
            }

            if (atomic && PrefabSessionScope.HasActiveSession)
            {
                tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                    "atomic=true is not supported while a Prefab contents session is active " +
                    "because preview-scene create and delete operations intentionally bypass " +
                    "Unity Undo and therefore cannot be rolled back reliably. Save or discard " +
                    "the session, or retry with atomic=false.",
                    "validation_error"
                ));
                yield break;
            }

            if (atomic)
            {
                foreach (JToken operationToken in operations)
                {
                    if (operationToken is JObject operation
                        && operation["tool"]?.ToString() == "open_prefab_contents")
                    {
                        tcs.SetResult(McpUnitySocketHandler.CreateErrorResponse(
                            "atomic=true cannot include open_prefab_contents because that operation " +
                            "would activate a Prefab contents session whose preview-scene changes " +
                            "intentionally bypass Unity Undo. Open the Prefab before a non-atomic " +
                            "batch, or run the atomic batch outside Prefab editing.",
                            "validation_error"
                        ));
                        yield break;
                    }
                }
            }

            JArray results = new JArray();
            int succeeded = 0;
            int failed = 0;
            int undoGroup = -1;
            int assetWriteCollectionId = -1;
            string[] unrevertedAssetWrites = Array.Empty<string>();

            // Start undo group for atomic operations
            if (atomic)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Batch Execute");
                assetWriteCollectionId = AtomicBatchAssetWriteTracker.Begin();
            }

            try
            {
            for (int i = 0; i < operations.Count; i++)
            {
                JObject operation = operations[i] as JObject;
                if (operation == null)
                {
                    results.Add(CreateOperationResult(i, null, false, null, "Invalid operation format"));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                    continue;
                }

                string toolName = operation["tool"]?.ToString();
                JObject toolParams = operation["params"] as JObject ?? new JObject();
                string operationId = operation["id"]?.ToString() ?? i.ToString();

                // Validate tool name
                if (string.IsNullOrEmpty(toolName))
                {
                    results.Add(CreateOperationResult(i, operationId, false, null, "Missing 'tool' name in operation"));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                    continue;
                }

                // Prevent recursive batch execution
                if (toolName == Name)
                {
                    results.Add(CreateOperationResult(i, operationId, false, null, "Cannot nest batch_execute operations"));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                    continue;
                }

                // Get the tool
                if (!_server.TryGetTool(toolName, out McpToolBase tool))
                {
                    results.Add(CreateOperationResult(i, operationId, false, null, $"Unknown tool: {toolName}"));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                    continue;
                }

                // Execute the tool
                JObject toolResult = null;
                Exception toolException = null;

                if (tool.IsAsync)
                {
                    var toolTcs = new TaskCompletionSource<JObject>();

                    try
                    {
                        tool.ExecuteAsync(toolParams, toolTcs);
                    }
                    catch (Exception ex)
                    {
                        toolException = ex;
                    }

                    // Wait for async tool completion (yield must be outside try-catch)
                    if (toolException == null)
                    {
                        while (!toolTcs.Task.IsCompleted)
                        {
                            yield return null;
                        }

                        if (toolTcs.Task.IsFaulted)
                        {
                            toolException = toolTcs.Task.Exception?.InnerException ?? toolTcs.Task.Exception;
                        }
                        else
                        {
                            toolResult = toolTcs.Task.Result;
                        }
                    }
                }
                else
                {
                    try
                    {
                        toolResult = tool.Execute(toolParams);
                    }
                    catch (Exception ex)
                    {
                        toolException = ex;
                    }
                }

                // Process result
                if (toolException != null)
                {
                    results.Add(CreateOperationResult(i, operationId, false, null, toolException.Message));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                }
                else if (toolResult != null)
                {
                    if (toolResult["type"]?.ToString() == "image")
                    {
                        const string imageErrorCode = "IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH";
                        string imageError =
                            $"{imageErrorCode}: Tool '{toolName}' returned image content, which " +
                            $"batch_execute cannot transport. Call '{toolName}' directly.";
                        if (toolResult["gameViewWindowCreated"]?.ToObject<bool?>() == true)
                        {
                            imageError += " Side effect: gameViewWindowCreated=true; Unity Undo " +
                                "cannot close this editor window.";
                        }
                        results.Add(CreateOperationResult(
                            i,
                            operationId,
                            false,
                            null,
                            imageError,
                            imageErrorCode));
                        failed++;

                        if (stopOnError)
                        {
                            unrevertedAssetWrites = RevertIfAtomic(
                                atomic, undoGroup, assetWriteCollectionId);
                            break;
                        }

                        yield return null;
                        continue;
                    }

                    // Check if the result indicates an error
                    bool isError = toolResult["error"] != null;
                    bool isSuccess = toolResult["success"]?.ToObject<bool?>() ?? !isError;

                    if (isSuccess && !isError)
                    {
                        results.Add(CreateOperationResult(i, operationId, true, toolResult, null));
                        succeeded++;
                    }
                    else
                    {
                        string errorMessage = toolResult["error"]?["message"]?.ToString()
                            ?? toolResult["message"]?.ToString()
                            ?? "Tool execution failed";
                        results.Add(CreateOperationResult(i, operationId, false, toolResult, errorMessage));
                        failed++;

                        if (stopOnError)
                        {
                            unrevertedAssetWrites = RevertIfAtomic(
                                atomic, undoGroup, assetWriteCollectionId);
                            break;
                        }
                    }
                }
                else
                {
                    results.Add(CreateOperationResult(i, operationId, false, null, "Tool returned null result"));
                    failed++;

                    if (stopOnError)
                    {
                        unrevertedAssetWrites = RevertIfAtomic(
                            atomic, undoGroup, assetWriteCollectionId);
                        break;
                    }
                }

                // Yield to allow Unity to process other events
                yield return null;
            }

            // Collapse undo group
            if (atomic && failed == 0)
            {
                if (undoGroup >= 0)
                    Undo.CollapseUndoOperations(undoGroup);
            }

            // Build response
            string message;
            if (failed == 0)
            {
                message = $"Successfully executed {succeeded}/{operations.Count} operations.";
            }
            else if (atomic && stopOnError)
            {
                message = "Batch execution failed. Unity Undo restored only Undo-tracked " +
                    $"in-memory state. {succeeded} operations succeeded before failure.";
                if (unrevertedAssetWrites.Length > 0)
                {
                    message += $" {unrevertedAssetWrites.Length} asset path(s) were observed " +
                        "in Unity save/postprocess callbacks during this batch's collection " +
                        "window. This evidence may include writes from other editor activity; " +
                        "Unity Undo does not establish that those disk writes were reverted. " +
                        "See unrevertedAssetWrites.";
                }
                else
                {
                    message += " No asset save/postprocess callbacks were observed during this " +
                        "batch's collection window.";
                }
            }
            else if (stopOnError)
            {
                message = $"Batch execution stopped on error. {succeeded}/{operations.Count} operations succeeded.";
            }
            else
            {
                message = $"Batch execution completed with errors. {succeeded}/{operations.Count} operations succeeded, {failed} failed.";
            }

            var response = new JObject
            {
                ["success"] = failed == 0,
                ["type"] = "text",
                ["message"] = message,
                ["results"] = results,
                ["summary"] = new JObject
                {
                    ["total"] = operations.Count,
                    ["succeeded"] = succeeded,
                    ["failed"] = failed,
                    ["executed"] = succeeded + failed
                }
            };

            if (atomic && failed > 0)
                response["unrevertedAssetWrites"] = new JArray(unrevertedAssetWrites);

            tcs.SetResult(response);
            }
            finally
            {
                if (assetWriteCollectionId >= 0)
                    AtomicBatchAssetWriteTracker.End(assetWriteCollectionId);
            }
        }

        private string[] RevertIfAtomic(
            bool atomic,
            int undoGroup,
            int assetWriteCollectionId)
        {
            if (!atomic)
                return Array.Empty<string>();

            string[] assetWrites = AtomicBatchAssetWriteTracker.End(assetWriteCollectionId);
            if (undoGroup >= 0)
                Undo.RevertAllDownToGroup(undoGroup);

            return assetWrites;
        }

        private JObject CreateOperationResult(
            int index,
            string id,
            bool success,
            JObject result,
            string error,
            string errorCode = null)
        {
            var operationResult = new JObject
            {
                ["index"] = index,
                ["id"] = id ?? index.ToString(),
                ["success"] = success
            };

            if (result != null)
            {
                operationResult["result"] = result;
            }

            if (!success)
            {
                operationResult["error"] = error ?? "Unknown error";
                if (!string.IsNullOrEmpty(errorCode))
                    operationResult["errorCode"] = errorCode;
            }

            return operationResult;
        }
    }
}
