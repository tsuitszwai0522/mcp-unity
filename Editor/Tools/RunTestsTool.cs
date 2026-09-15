using System;
using System.Threading;
using System.Threading.Tasks;
using McpUnity.Unity;
using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;
using McpUnity.Services;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for running Unity Test Runner tests
    /// </summary>
    public class RunTestsTool : McpToolBase
    {
        private readonly ITestRunnerService _testRunnerService;

        public RunTestsTool(ITestRunnerService testRunnerService)
        {
            Name = "run_tests";
            Description = "Runs tests using Unity's Test Runner. Unity waits for 75% of the configured Node transport timeout, then returns test_run_still_running with runId and expectedArtifactPath so the receipt reaches the caller; the path is published as artifactPath only after validated XML exists. Poll with get_test_run without cancelling the run. Only one run may be tracked because Unity callbacks have no run GUID: a second RunStarted (for example from the Test Runner window) invalidates the MCP record and discards its result. The active lock ends on RunFinished or is released as stale after 24 hours; a stale-release response tells the caller to retry. Owned artifacts are stored under Library/McpUnity/TestResults and only the 20 most recent are retained. If any loaded scene has unsaved changes the run is not started: the response is dirty_scenes_present with dirtyScenes, because Unity Test Framework would otherwise block the Editor on a save dialog and Don't Save reloads the scene from disk";
            IsAsync = true;
            _testRunnerService = testRunnerService;
        }
        
        /// <summary>
        /// Executes the RunTests tool asynchronously on the main thread.
        /// </summary>
        /// <param name="parameters">Tool parameters, including optional 'testMode', 'testFilter', and 'assemblyNames'.</param>
        /// <param name="tcs">TaskCompletionSource to set the result or exception.</param>
        public override async void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                // Parse parameters
                string testModeStr = parameters?["testMode"]?.ToObject<string>() ?? "EditMode";
                string testFilter = parameters?["testFilter"]?.ToObject<string>(); // Optional
                bool returnOnlyFailures = parameters?["returnOnlyFailures"]?.ToObject<bool>() ?? false; // Optional
                bool returnWithLogs = parameters?["returnWithLogs"]?.ToObject<bool>() ?? false; // Optional

                // Optional assembly-name filter; supports NUnit "!" exclusion prefix per entry
                // (e.g. "!Unity.Multiplayer.Tools.Adapters.Tests" to skip a broken third-party test assembly).
                string[] assemblyNames = null;
                JArray assemblyNamesArr = parameters?["assemblyNames"] as JArray;
                if (assemblyNamesArr != null && assemblyNamesArr.Count > 0)
                {
                    var list = new List<string>(assemblyNamesArr.Count);
                    foreach (var token in assemblyNamesArr)
                    {
                        var name = token?.ToObject<string>();
                        if (!string.IsNullOrEmpty(name))
                        {
                            list.Add(name);
                        }
                    }
                    if (list.Count > 0)
                    {
                        assemblyNames = list.ToArray();
                    }
                }

                bool isNumericMode = int.TryParse(testModeStr, out _);
                if (string.IsNullOrWhiteSpace(testModeStr)
                    || isNumericMode
                    || !Enum.TryParse(testModeStr, true, out TestMode testMode)
                    || !Enum.IsDefined(typeof(TestMode), testMode))
                {
                    string validModes = string.Join(", ", Enum.GetNames(typeof(TestMode)));
                    tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                        $"Invalid testMode '{testModeStr}'. Valid values: {validModes}.",
                        "validation_error"));
                    return;
                }

                string assemblyLog = assemblyNames != null ? string.Join(",", assemblyNames) : "(none)";
                McpLogger.LogInfo($"Executing RunTestsTool: Mode={testMode}, Filter={testFilter ?? "(none)"}, Assemblies={assemblyLog}");

                // Call the service to run tests
                JObject result = await _testRunnerService.ExecuteTestsAsync(testMode, returnOnlyFailures, returnWithLogs, testFilter, assemblyNames);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"Failed to execute tool {Name}: {ex.Message}\n{ex.StackTrace}");
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to execute tool {Name}: {ex.Message}",
                    "tool_execution_error"));
            }
        }
    }
}
