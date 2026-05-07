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
            Description = "Runs tests using Unity's Test Runner";
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

            TestMode testMode = TestMode.EditMode;

            if (Enum.TryParse(testModeStr, true, out TestMode parsedMode))
            {
                testMode = parsedMode;
            }

            string assemblyLog = assemblyNames != null ? string.Join(",", assemblyNames) : "(none)";
            McpLogger.LogInfo($"Executing RunTestsTool: Mode={testMode}, Filter={testFilter ?? "(none)"}, Assemblies={assemblyLog}");

            // Call the service to run tests
            JObject result = await _testRunnerService.ExecuteTestsAsync(testMode, returnOnlyFailures, returnWithLogs, testFilter, assemblyNames);
            tcs.SetResult(result);
        }
    }
}
