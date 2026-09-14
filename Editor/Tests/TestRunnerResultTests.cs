using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using McpUnity.Resources;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    public class TestRunnerResultTests
    {
        private bool _restoreIgnoreFailingMessages;
        private bool _previousIgnoreFailingMessages;

        [TearDown]
        public void RestoreLogAssertState()
        {
            if (!_restoreIgnoreFailingMessages)
                return;

            LogAssert.ignoreFailingMessages = _previousIgnoreFailingMessages;
            _restoreIgnoreFailingMessages = false;
        }

        [Test]
        public async Task SocketHandlerCatchCarriesParsedRequestIdAndTypedError()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Error processing message:"));
            string sent = null;
            var handler = new McpUnitySocketHandler(null);

            await handler.ProcessMessageAsync(
                "{\"id\":\"request-rc5\",\"method\":\"force-handler-catch\",\"params\":{}}",
                response => sent = response);

            JObject response = JObject.Parse(sent);

            Assert.AreEqual("request-rc5", response["id"]?.ToString());
            Assert.AreEqual("internal_error", response["error"]?["type"]?.ToString());
            StringAssert.Contains(
                "Internal server error",
                response["error"]?["message"]?.ToString());
            Assert.IsNull(response["result"]);
        }

        [Test, Timeout(1000)]
        public async Task RunTestsToolAsyncFailureCompletesTypedError()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Failed to execute tool run_tests: run tests exploded"));
            var completionSource = new TaskCompletionSource<JObject>();

            new RunTestsTool(new ThrowingTestRunnerService()).ExecuteAsync(
                new JObject(),
                completionSource);
            JObject response = await completionSource.Task;

            Assert.AreEqual("tool_execution_error", response["error"]?["type"]?.ToString());
            StringAssert.Contains(
                "run tests exploded",
                response["error"]?["message"]?.ToString());
        }

        [Test, Timeout(1000)]
        public async Task GetTestsResourceAsyncFailureCompletesTypedError()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Failed to fetch resource get_tests: get tests exploded"));
            var completionSource = new TaskCompletionSource<JObject>();

            new GetTestsResource(new ThrowingTestRunnerService()).FetchAsync(
                new JObject(),
                completionSource);
            JObject response = await completionSource.Task;

            Assert.AreEqual("resource_fetch_error", response["error"]?["type"]?.ToString());
            StringAssert.Contains(
                "get tests exploded",
                response["error"]?["message"]?.ToString());
        }

        [Test, Timeout(1000)]
        public async Task RunTestsToolCatchIgnoresCompletionThatAlreadyWon()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Failed to execute tool run_tests: run tests exploded"));
            var completionSource = new TaskCompletionSource<JObject>();
            var earlierResult = new JObject { ["source"] = "earlier callback" };
            completionSource.SetResult(earlierResult);
            var completionErrors = new List<string>();
            Application.LogCallback captureCompletionErrors = (condition, stackTrace, type) =>
            {
                if (IsCompletionStateError(condition, stackTrace, type, "RunTestsTool"))
                {
                    completionErrors.Add(condition);
                }
            };

            Application.logMessageReceived += captureCompletionErrors;
            try
            {
                new RunTestsTool(new ThrowingTestRunnerService()).ExecuteAsync(
                    new JObject(),
                    completionSource);
                await Task.Delay(20);

                Assert.AreSame(earlierResult, completionSource.Task.Result);
                CollectionAssert.IsEmpty(completionErrors);
            }
            finally
            {
                Application.logMessageReceived -= captureCompletionErrors;
            }
        }

        [Test, Timeout(1000)]
        public async Task GetTestsResourceCatchIgnoresCompletionThatAlreadyWon()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Failed to fetch resource get_tests: get tests exploded"));
            var completionSource = new TaskCompletionSource<JObject>();
            var earlierResult = new JObject { ["source"] = "earlier callback" };
            completionSource.SetResult(earlierResult);
            var completionErrors = new List<string>();
            Application.LogCallback captureCompletionErrors = (condition, stackTrace, type) =>
            {
                if (IsCompletionStateError(condition, stackTrace, type, "GetTestsResource"))
                {
                    completionErrors.Add(condition);
                }
            };

            Application.logMessageReceived += captureCompletionErrors;
            try
            {
                new GetTestsResource(new ThrowingTestRunnerService()).FetchAsync(
                    new JObject(),
                    completionSource);
                await Task.Delay(20);

                Assert.AreSame(earlierResult, completionSource.Task.Result);
                CollectionAssert.IsEmpty(completionErrors);
            }
            finally
            {
                Application.logMessageReceived -= captureCompletionErrors;
            }
        }

        [Test]
        public void SocketToolCatchIgnoresCompletionThatAlreadyWon()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Error executing tool completion_then_throw: after completion"));
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Ignored late tool failure completion for completion_then_throw"));
            var completionSource = new TaskCompletionSource<JObject>();
            IEnumerator execution = InvokeSocketCoroutine(
                "ExecuteTool",
                new CompletingThenThrowingTool(),
                completionSource);

            Assert.DoesNotThrow(() => execution.MoveNext());
            Assert.AreEqual("first", completionSource.Task.Result["source"]?.ToString());
        }

        [Test]
        public void SocketResourceCatchIgnoresCompletionThatAlreadyWon()
        {
            IgnoreAmbientEditorLogsForThisTest();
            LogAssert.Expect(
                LogType.Error,
                new Regex("Error fetching resource completion_then_throw: after completion"));
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Ignored late resource failure completion for completion_then_throw"));
            var completionSource = new TaskCompletionSource<JObject>();
            IEnumerator execution = InvokeSocketCoroutine(
                "FetchResourceCoroutine",
                new CompletingThenThrowingResource(),
                completionSource);

            Assert.DoesNotThrow(() => execution.MoveNext());
            Assert.AreEqual("first", completionSource.Task.Result["source"]?.ToString());
        }

        [Test, Timeout(1000)]
        public async Task RunTestsToolLateCompletionUsesTrySetResult()
        {
            IgnoreAmbientEditorLogsForThisTest();
            var service = new ControllableTestRunnerService();
            var completionSource = new TaskCompletionSource<JObject>();
            var earlierResult = new JObject { ["source"] = "earlier callback" };
            var errorLogs = new List<string>();
            Application.LogCallback captureErrors = (condition, _, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception)
                    && condition != null
                    && condition.Contains("Failed to execute tool run_tests:"))
                {
                    errorLogs.Add(condition);
                }
            };

            Application.logMessageReceived += captureErrors;
            try
            {
                new RunTestsTool(service).ExecuteAsync(new JObject(), completionSource);
                Assert.IsTrue(completionSource.TrySetResult(earlierResult));

                service.CompleteRun(new JObject { ["success"] = true });
                await Task.Delay(20);

                Assert.AreSame(earlierResult, completionSource.Task.Result);
                CollectionAssert.IsEmpty(errorLogs);
            }
            finally
            {
                Application.logMessageReceived -= captureErrors;
            }
        }

        [Test, Timeout(1000)]
        public async Task GetTestsResourceLateCompletionUsesTrySetResult()
        {
            IgnoreAmbientEditorLogsForThisTest();
            var service = new ControllableTestRunnerService();
            var completionSource = new TaskCompletionSource<JObject>();
            var earlierResult = new JObject { ["source"] = "earlier callback" };
            var errorLogs = new List<string>();
            Application.LogCallback captureErrors = (condition, _, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception)
                    && condition != null
                    && condition.Contains("Failed to fetch resource get_tests:"))
                {
                    errorLogs.Add(condition);
                }
            };

            Application.logMessageReceived += captureErrors;
            try
            {
                new GetTestsResource(service).FetchAsync(new JObject(), completionSource);
                Assert.IsTrue(completionSource.TrySetResult(earlierResult));

                service.CompleteGetTests(new List<ITestAdaptor>());
                await Task.Delay(20);

                Assert.AreSame(earlierResult, completionSource.Task.Result);
                CollectionAssert.IsEmpty(errorLogs);
            }
            finally
            {
                Application.logMessageReceived -= captureErrors;
            }
        }

        private void IgnoreAmbientEditorLogsForThisTest()
        {
            if (!_restoreIgnoreFailingMessages)
            {
                _previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
                _restoreIgnoreFailingMessages = true;
            }

            LogAssert.ignoreFailingMessages = true;
        }

        private static bool IsCompletionStateError(
            string condition,
            string stackTrace,
            LogType type,
            string ownerType)
        {
            return (type == LogType.Error || type == LogType.Exception)
                && condition != null
                && condition.Contains("InvalidOperationException")
                && stackTrace != null
                && stackTrace.Contains(ownerType);
        }

        [Test]
        public void ZeroExecutionFromTestFilterFailsLoud()
        {
            JObject response = BuildResponse(
                testFilter: "McpUnity.Editor.Tests");

            AssertNoTestsMatched(response);
            Assert.That(response["message"]?.ToString(),
                Does.Contain("testFilter=\"McpUnity.Editor.Tests\""));
            Assert.That(response["message"]?.ToString(),
                Does.Contain("assemblyNames=(none)"));
        }

        [Test]
        public void ZeroExecutionFromAssemblyNamesFailsLoud()
        {
            JObject response = BuildResponse(
                assemblyNames: new[] { "NoSuchAssembly_ZZZ_12345" });

            AssertNoTestsMatched(response);
            Assert.That(response["message"]?.ToString(),
                Does.Contain("testFilter=(none)"));
            Assert.That(response["message"]?.ToString(),
                Does.Contain("NoSuchAssembly_ZZZ_12345"));
        }

        [Test]
        public void ZeroExecutionFromBothFiltersFailsLoud()
        {
            JObject response = BuildResponse(
                testFilter: "NoSuchTestName_ZZZ_12345",
                assemblyNames: new[] { "NoSuchAssembly_ZZZ_12345" });

            AssertNoTestsMatched(response);
            string message = response["message"]?.ToString();
            Assert.That(message, Does.Contain("NoSuchTestName_ZZZ_12345"));
            Assert.That(message, Does.Contain("NoSuchAssembly_ZZZ_12345"));
        }

        [Test]
        public void ExecutedTestsUseLeafCountAndPreserveTreeNodeCountAndFilter()
        {
            var results = new List<JObject>
            {
                new JObject { ["fullName"] = "MyNamespace.MyFixture.Passes" },
                new JObject { ["fullName"] = "MyNamespace.MyFixture.Fails" }
            };
            JObject response = TestRunnerService.BuildResponse(
                results,
                "EditMode",
                "Failed",
                1.25,
                8,
                2,
                1,
                1,
                0,
                "EditMode",
                "MyNamespace.MyFixture",
                new[] { "McpUnity.Editor.Tests" });

            Assert.IsTrue(response.Value<bool>("success"));
            Assert.IsNull(response["error_code"]);
            Assert.AreEqual(4, response.Value<int>("testCount"));
            Assert.AreEqual(
                response.Value<int>("passCount")
                    + response.Value<int>("failCount")
                    + response.Value<int>("skipCount")
                    + response.Value<int>("inconclusiveCount"),
                response.Value<int>("testCount"));
            Assert.AreEqual(8, response.Value<int>("treeNodeCount"));
            Assert.AreEqual(
                "EditMode test run completed: 2/4 passed - 1/4 failed - 1/4 skipped - 0/4 inconclusive",
                response.Value<string>("message"));

            var filter = (JObject)response["filter"];
            Assert.AreEqual("EditMode", filter.Value<string>("testMode"));
            Assert.AreEqual("MyNamespace.MyFixture", filter.Value<string>("testFilter"));
            CollectionAssert.AreEqual(
                new[] { "McpUnity.Editor.Tests" },
                filter["assemblyNames"].ToObject<string[]>());
        }

        [Test]
        public void InconclusiveOnlyRunIsSuccessful()
        {
            JObject response = TestRunnerService.BuildResponse(
                new List<JObject>(),
                "EditMode",
                "Inconclusive",
                0.25,
                4,
                0,
                0,
                0,
                3,
                "EditMode",
                null,
                null);

            Assert.IsTrue(response.Value<bool>("success"));
            Assert.IsNull(response["error_code"]);
            Assert.AreEqual(3, response.Value<int>("testCount"));
            Assert.AreEqual(3, response.Value<int>("inconclusiveCount"));
            Assert.That(response.Value<string>("message"),
                Does.EndWith("3/3 inconclusive"));
        }

        [Test]
        public void MixedRunIncludesInconclusiveInTestCount()
        {
            JObject response = TestRunnerService.BuildResponse(
                new List<JObject>(),
                "EditMode",
                "Inconclusive",
                0.25,
                4,
                2,
                0,
                0,
                1,
                "EditMode",
                null,
                null);

            Assert.IsTrue(response.Value<bool>("success"));
            Assert.AreEqual(3, response.Value<int>("testCount"));
            Assert.AreEqual(2, response.Value<int>("passCount"));
            Assert.AreEqual(1, response.Value<int>("inconclusiveCount"));
            Assert.AreEqual(
                "EditMode test run completed: 2/3 passed - 0/3 failed - 0/3 skipped - 1/3 inconclusive",
                response.Value<string>("message"));
        }

        [Test]
        public void SkipOnlyRunIsSuccessful()
        {
            JObject response = TestRunnerService.BuildResponse(
                new List<JObject>(),
                "EditMode",
                "Skipped",
                0.25,
                3,
                0,
                0,
                2,
                0,
                "EditMode",
                null,
                null);

            Assert.IsTrue(response.Value<bool>("success"));
            Assert.IsNull(response["error_code"]);
            Assert.AreEqual(2, response.Value<int>("testCount"));
            Assert.AreEqual(2, response.Value<int>("skipCount"));
        }

        [Test]
        public void BuildResultJsonZeroSummaryFailsLoud()
        {
            var results = new List<ITestResultAdaptor>
            {
                new FakeSuiteResultAdaptor()
            };
            var summary = new FakeSummaryResultAdaptor(
                "EmptyRun",
                "Passed",
                0.01,
                passCount: 0,
                failCount: 0,
                skipCount: 0,
                inconclusiveCount: 0);

            JObject response = TestRunnerService.BuildResultJson(
                results,
                summary,
                returnOnlyFailures: true,
                returnWithLogs: false,
                testMode: TestMode.EditMode,
                testFilter: "NoSuchTestName_ZZZ_12345",
                assemblyNames: null);

            AssertNoTestsMatched(response);
            Assert.That(response.Value<string>("message"),
                Does.Contain("NoSuchTestName_ZZZ_12345"));
            Assert.AreEqual(0, ((JArray)response["results"]).Count);
        }

        [Test]
        public void BuildResultJsonMapsAdaptorSummaryAndSerializesOnlyLeaves()
        {
            var results = new List<ITestResultAdaptor>
            {
                new FakeSuiteResultAdaptor(),
                new FakeLeafResultAdaptor(
                    "Passes",
                    "MyNamespace.MyFixture.Passes",
                    "Passed",
                    "pass message",
                    0.1,
                    "pass output",
                    "pass stack"),
                new FakeLeafResultAdaptor(
                    "Fails",
                    "MyNamespace.MyFixture.Fails",
                    "Failed:Error",
                    "fail message",
                    0.2,
                    "fail output",
                    "fail stack")
            };
            var summary = new FakeSummaryResultAdaptor(
                "MappedRun",
                "Failed",
                1.5,
                passCount: 4,
                failCount: 3,
                skipCount: 2,
                inconclusiveCount: 1);

            JObject response = TestRunnerService.BuildResultJson(
                results,
                summary,
                returnOnlyFailures: false,
                returnWithLogs: true,
                testMode: TestMode.PlayMode,
                testFilter: "MyNamespace.MyFixture",
                assemblyNames: new[] { "McpUnity.Editor.Tests" });

            Assert.IsTrue(response.Value<bool>("success"));
            Assert.AreEqual(4, response.Value<int>("passCount"));
            Assert.AreEqual(3, response.Value<int>("failCount"));
            Assert.AreEqual(2, response.Value<int>("skipCount"));
            Assert.AreEqual(1, response.Value<int>("inconclusiveCount"));
            Assert.AreEqual(10, response.Value<int>("testCount"));
            Assert.AreEqual(3, response.Value<int>("treeNodeCount"));
            Assert.AreEqual("Failed", response.Value<string>("resultState"));
            Assert.AreEqual(1.5, response.Value<double>("durationSeconds"));
            Assert.That(response.Value<string>("message"), Does.StartWith("MappedRun test run completed:"));

            var filter = (JObject)response["filter"];
            Assert.AreEqual("PlayMode", filter.Value<string>("testMode"));
            Assert.AreEqual("MyNamespace.MyFixture", filter.Value<string>("testFilter"));
            CollectionAssert.AreEqual(
                new[] { "McpUnity.Editor.Tests" },
                filter["assemblyNames"].ToObject<string[]>());

            var serializedResults = (JArray)response["results"];
            Assert.AreEqual(2, serializedResults.Count);
            Assert.AreEqual("Passes", serializedResults[0].Value<string>("name"));
            Assert.AreEqual("MyNamespace.MyFixture.Passes", serializedResults[0].Value<string>("fullName"));
            Assert.AreEqual("Passed", serializedResults[0].Value<string>("state"));
            Assert.AreEqual("pass message", serializedResults[0].Value<string>("message"));
            Assert.AreEqual(0.1, serializedResults[0].Value<double>("duration"));
            Assert.AreEqual("pass output", serializedResults[0].Value<string>("logs"));
            Assert.AreEqual("pass stack", serializedResults[0].Value<string>("stackTrace"));
        }

        [Test]
        public void BuildResultJsonReturnOnlyFailuresFiltersLeaves()
        {
            var results = new List<ITestResultAdaptor>
            {
                new FakeSuiteResultAdaptor(),
                new FakeLeafResultAdaptor(
                    "Passes",
                    "MyNamespace.MyFixture.Passes",
                    "Passed",
                    "pass message",
                    0.1,
                    outputImplemented: false,
                    stackTrace: null),
                new FakeLeafResultAdaptor(
                    "Fails",
                    "MyNamespace.MyFixture.Fails",
                    "Failed:Error",
                    "fail message",
                    0.2,
                    outputImplemented: false,
                    stackTrace: "fail stack")
            };
            var summary = new FakeSummaryResultAdaptor(
                "FilteredRun",
                "Failed",
                0.3,
                passCount: 1,
                failCount: 1,
                skipCount: 0,
                inconclusiveCount: 0);

            JObject response = TestRunnerService.BuildResultJson(
                results,
                summary,
                returnOnlyFailures: true,
                returnWithLogs: false,
                testMode: TestMode.EditMode,
                testFilter: null,
                assemblyNames: null);

            var serializedResults = (JArray)response["results"];
            Assert.AreEqual(1, serializedResults.Count);
            Assert.AreEqual("Fails", serializedResults[0].Value<string>("name"));
            Assert.IsNull(serializedResults[0].Value<string>("logs"));
        }

        [TestCase("BogusMode")]
        [TestCase("1")]
        [TestCase("999")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("EditMode,PlayMode")]
        public void InvalidTestModeReturnsValidationErrorWithoutExecuting(string invalidMode)
        {
            var service = new RecordingTestRunnerService();
            var completionSource = new TaskCompletionSource<JObject>();

            new RunTestsTool(service).ExecuteAsync(
                new JObject { ["testMode"] = invalidMode },
                completionSource);

            Assert.IsTrue(completionSource.Task.IsCompleted);
            JObject response = completionSource.Task.Result;
            Assert.AreEqual("validation_error", response["error"]?["type"]?.ToString());
            Assert.IsNull(response["success"]);
            Assert.IsNull(response["error_code"]);
            Assert.That(response["error"]?["message"]?.ToString(), Does.Contain(invalidMode));
            Assert.That(response["error"]?["message"]?.ToString(), Does.Contain("EditMode"));
            Assert.That(response["error"]?["message"]?.ToString(), Does.Contain("PlayMode"));
            Assert.AreEqual(0, service.ExecuteCalls);
        }

        [Test]
        public void LowercaseTestModeIsAccepted()
        {
            var service = new RecordingTestRunnerService();
            var completionSource = new TaskCompletionSource<JObject>();

            new RunTestsTool(service).ExecuteAsync(
                new JObject { ["testMode"] = "editmode" },
                completionSource);

            Assert.IsTrue(completionSource.Task.IsCompleted);
            Assert.AreEqual(1, service.ExecuteCalls);
            Assert.AreEqual(TestMode.EditMode, service.LastTestMode.Value);
            Assert.IsTrue(completionSource.Task.Result.Value<bool>("success"));
        }

        [Test]
        public async Task ExecuteUsesUnityRunGuidAndReturnsVerifiedArtifact()
        {
            const string unityRunId = "11111111-1111-1111-1111-111111111111";
            string artifactDirectory = PrepareArtifactDirectory(nameof(ExecuteUsesUnityRunGuidAndReturnsVerifiedArtifact));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), artifactDirectory);

                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    returnOnlyFailures: false,
                    returnWithLogs: false,
                    testFilter: "RunA");
                api.CompleteSuccessfulRun("RunA.Test");
                JObject response = await pending;

                Assert.AreEqual(1, api.ExecuteCalls);
                Assert.AreEqual(unityRunId, response.Value<string>("runId"));
                Assert.AreEqual(
                    Path.Combine(artifactDirectory, $"{unityRunId}.xml"),
                    response.Value<string>("artifactPath"));
                Assert.IsTrue(File.Exists(response.Value<string>("artifactPath")));
                foreach (string baselineField in new[]
                {
                    "success",
                    "type",
                    "message",
                    "resultState",
                    "durationSeconds",
                    "testCount",
                    "treeNodeCount",
                    "passCount",
                    "failCount",
                    "skipCount",
                    "inconclusiveCount",
                    "filter",
                    "results"
                })
                {
                    Assert.IsNotNull(
                        response.Property(baselineField),
                        $"Happy-path response must preserve baseline field '{baselineField}'.");
                }
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task CompletionProtectsCurrentArtifactFromFutureDatedRetentionEntries()
        {
            const string unityRunId = "16161616-1616-1616-1616-161616161616";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(CompletionProtectsCurrentArtifactFromFutureDatedRetentionEntries));
            try
            {
                Directory.CreateDirectory(artifactDirectory);
                DateTime futureTimestamp = DateTime.UtcNow.AddYears(10);
                foreach (int value in Enumerable.Range(1, 20))
                {
                    string existingArtifact = Path.Combine(
                        artifactDirectory,
                        $"17000000-0000-0000-0000-{value:000000000000}.xml");
                    File.WriteAllText(existingArtifact, "<test-run />");
                    File.SetLastWriteTimeUtc(
                        existingArtifact,
                        futureTimestamp.AddMinutes(value));
                }

                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory);
                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "FutureDatedArtifacts");

                api.CompleteSuccessfulRun("FutureDatedArtifacts.Test");
                JObject response = await pending;

                string currentArtifact = Path.Combine(
                    artifactDirectory,
                    $"{unityRunId}.xml");
                Assert.AreEqual("completed", response.Value<string>("status"));
                Assert.AreEqual(currentArtifact, response.Value<string>("artifactPath"));
                Assert.IsTrue(File.Exists(currentArtifact));
                Assert.AreEqual(20, Directory.GetFiles(artifactDirectory, "*.xml").Length);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task ArtifactMissingAfterPruneUsesArtifactFailureResponse()
        {
            const string unityRunId = "18181818-1818-1818-1818-181818181818";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(ArtifactMissingAfterPruneUsesArtifactFailureResponse));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory,
                    pruneArtifacts: File.Delete);
                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "ArtifactRemovedByPrune");

                api.CompleteSuccessfulRun("ArtifactRemovedByPrune.Test");
                JObject response = await pending;
                JObject polled = service.GetTestRun(unityRunId);

                Assert.IsNull(response.Property("artifactPath"));
                Assert.AreEqual("failed_to_save", response.Value<string>("status"));
                Assert.AreEqual(
                    "test_result_artifact_write_failed",
                    response["artifactError"]?.Value<string>("error_code"));
                Assert.That(
                    response["artifactError"]?.Value<string>("message"),
                    Does.Contain("disappeared after pruning"));
                Assert.AreEqual("failed_to_save", polled.Value<string>("status"));
                Assert.IsNull(polled["artifactPath"]);
                Assert.That(
                    polled["artifactError"]?.Value<string>("message"),
                    Does.Contain("disappeared after pruning"));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task ConcurrentRunFailsLoudWithoutCallingExecuteTwice()
        {
            const string unityRunId = "22222222-2222-2222-2222-222222222222";
            string artifactDirectory = PrepareArtifactDirectory(nameof(ConcurrentRunFailsLoudWithoutCallingExecuteTwice));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), artifactDirectory);
                Task<JObject> runA = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "RunA");

                JObject runB = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "RunB");

                Assert.AreEqual(1, api.ExecuteCalls);
                Assert.IsFalse(runB.Value<bool>("success"));
                Assert.AreEqual("test_run_in_progress", runB.Value<string>("error_code"));
                Assert.AreEqual(unityRunId, runB.Value<string>("activeRunId"));

                api.CompleteSuccessfulRun("RunA.Test");
                await runA;
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task PollKeepsRunAFilterAndResultsAfterRunBIsRejected()
        {
            const string unityRunId = "33333333-3333-3333-3333-333333333333";
            string artifactDirectory = PrepareArtifactDirectory(nameof(PollKeepsRunAFilterAndResultsAfterRunBIsRejected));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var registry = new InMemoryTestRunRegistry();
                var service = new TestRunnerService(api, registry, artifactDirectory);
                Task<JObject> runA = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "RunA.Filter");

                JObject runB = await service.ExecuteTestsAsync(
                    TestMode.PlayMode,
                    false,
                    true,
                    "RunB.Filter");
                Assert.AreEqual("test_run_in_progress", runB.Value<string>("error_code"));

                api.CompleteSuccessfulRun("RunA.Filter.TestOne");
                await runA;
                JObject polled = service.GetTestRun(unityRunId);

                Assert.AreEqual("completed", polled.Value<string>("status"));
                Assert.AreEqual(
                    "RunA.Filter",
                    polled["filter"]?.Value<string>("testFilter"));
                Assert.AreEqual(
                    "RunA.Filter.TestOne",
                    polled["results"]?[0]?.Value<string>("fullName"));
                Assert.That(polled.ToString(), Does.Not.Contain("RunB.Filter"));
                Assert.AreEqual(1, api.ExecuteCalls);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [TestCase(ArtifactSaveBehavior.Throws, "simulated artifact failure")]
        [TestCase(ArtifactSaveBehavior.WritesNothing, "the file does not exist")]
        [TestCase(
            ArtifactSaveBehavior.WritesMalformedXml,
            "the file is not complete NUnit XML (")]
        [TestCase(
            ArtifactSaveBehavior.WritesWrongRootXml,
            "the XML root element is not <test-run>")]
        public async Task ArtifactSaveFailureNeverReturnsArtifactPath(
            ArtifactSaveBehavior saveBehavior,
            string expectedArtifactErrorPrefix)
        {
            const string unityRunId = "44444444-4444-4444-4444-444444444444";
            string artifactDirectory = PrepareArtifactDirectory(
                $"{nameof(ArtifactSaveFailureNeverReturnsArtifactPath)}-{saveBehavior}");
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, saveBehavior);
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), artifactDirectory);
                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "ArtifactFailure");

                api.CompleteSuccessfulRun("ArtifactFailure.Test");
                JObject response = await pending;
                JObject polled = service.GetTestRun(unityRunId);

                Assert.IsNull(response["artifactPath"]);
                Assert.IsNull(response.Property("artifactPath"));
                Assert.IsTrue(response.Value<bool>("success"));
                Assert.IsNull(response["error_code"]);
                Assert.AreEqual("failed_to_save", response.Value<string>("status"));
                Assert.AreEqual(
                    "test_result_artifact_write_failed",
                    response["artifactError"]?.Value<string>("error_code"));
                string responseArtifactError =
                    response["artifactError"]?.Value<string>("message");
                Assert.That(responseArtifactError, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    responseArtifactError,
                    Does.StartWith(expectedArtifactErrorPrefix));
                Assert.That(
                    response.Value<string>("message"),
                    Does.Contain("artifact could not be saved"));
                Assert.AreEqual("failed_to_save", polled.Value<string>("status"));
                Assert.IsNull(polled["artifactPath"]);
                Assert.IsTrue(polled.Value<bool>("success"));
                Assert.AreEqual(
                    "test_result_artifact_write_failed",
                    polled["artifactError"]?.Value<string>("error_code"));
                string polledArtifactError =
                    polled["artifactError"]?.Value<string>("message");
                Assert.That(polledArtifactError, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    polledArtifactError,
                    Does.StartWith(expectedArtifactErrorPrefix));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public void ArtifactWriteValidatesBeforeAndAfterPublishing()
        {
            MethodInfo writer = typeof(TestRunnerService).GetMethod(
                "TryWriteArtifact",
                BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo validator = typeof(TestRunnerService).GetMethod(
                "TryValidateArtifact",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.AreEqual(
                2,
                CountDirectCalls(writer, validator),
                "TryWriteArtifact must validate both the temporary artifact before publishing " +
                "and the final artifact after publishing.");
        }

        [Test]
        public async Task PollFailsLoudlyWhenCompletedArtifactWasDeleted()
        {
            const string unityRunId = "77777777-7777-7777-7777-777777777777";
            string artifactDirectory = PrepareArtifactDirectory(nameof(PollFailsLoudlyWhenCompletedArtifactWasDeleted));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), artifactDirectory);
                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "DeletedArtifact");
                api.CompleteSuccessfulRun("DeletedArtifact.Test");
                JObject completed = await pending;
                File.Delete(completed.Value<string>("artifactPath"));

                JObject polled = service.GetTestRun(unityRunId);

                Assert.IsFalse(polled.Value<bool>("success"));
                Assert.AreEqual(
                    "test_result_artifact_unavailable",
                    polled.Value<string>("error_code"));
                Assert.AreEqual("completed", polled.Value<string>("status"));
                Assert.IsNull(polled["artifactPath"]);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task WaitForCompletionReturnsTimeoutWithoutCompletingPendingRun()
        {
            var completionSource = new TaskCompletionSource<JObject>();
            var delayCompletionSource = new TaskCompletionSource<bool>();
            JObject timeout = new JObject
            {
                ["success"] = false,
                ["error_code"] = "test_run_still_running",
                ["message"] = "poll"
            };

            Task<JObject> waiter = TestRunnerService.WaitForCompletionAsync(
                completionSource.Task,
                delayCompletionSource.Task,
                timeout);

            delayCompletionSource.SetResult(true);
            JObject response = await waiter;

            Assert.AreSame(timeout, response);
            Assert.IsFalse(completionSource.Task.IsCompleted);
        }

        [Test]
        public void UnityWaitBudgetIsShorterThanNodeTransportTimeout()
        {
            TimeSpan budget = TestRunnerService.GetUnityWaitBudget(10);

            Assert.AreEqual(7.5, budget.TotalSeconds);
            Assert.Less(budget.TotalSeconds, 10);
        }

        [Test]
        public async Task TimeoutReturnsPollIdentityAndKeepsRunActive()
        {
            const string unityRunId = "66666666-6666-6666-6666-666666666666";
            string artifactDirectory = PrepareArtifactDirectory(nameof(TimeoutReturnsPollIdentityAndKeepsRunActive));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory,
                    _ => Task.CompletedTask);

                JObject response = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "SlowRun");
                JObject polled = service.GetTestRun(unityRunId);

                Assert.IsFalse(response.Value<bool>("success"));
                Assert.AreEqual("test_run_still_running", response.Value<string>("error_code"));
                Assert.AreEqual(unityRunId, response.Value<string>("runId"));
                Assert.IsNull(response["artifactPath"]);
                Assert.AreEqual(
                    Path.Combine(artifactDirectory, $"{unityRunId}.xml"),
                    response.Value<string>("expectedArtifactPath"));
                Assert.IsFalse(response.Value<bool>("artifactExists"));
                Assert.That(response.Value<string>("message"), Does.Contain("75%"));
                Assert.That(response.Value<string>("message"), Does.Contain("get_test_run"));
                Assert.AreEqual("running", polled.Value<string>("status"));
                Assert.IsNull(polled["artifactPath"]);
                Assert.AreEqual(
                    response.Value<string>("expectedArtifactPath"),
                    polled.Value<string>("expectedArtifactPath"));
                Assert.AreEqual(1, api.ExecuteCalls);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task TimedOutRunCompletionDoesNotPolluteNextRunRecord()
        {
            const string firstRunId = "88888888-8888-8888-8888-888888888888";
            const string secondRunId = "99999999-9999-9999-9999-999999999999";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(TimedOutRunCompletionDoesNotPolluteNextRunRecord));
            try
            {
                var api = new FakeTestRunnerApi(firstRunId, ArtifactSaveBehavior.ValidXml);
                var registry = new InMemoryTestRunRegistry();
                var service = new TestRunnerService(
                    api,
                    registry,
                    artifactDirectory,
                    _ => Task.CompletedTask);

                JObject firstTimeout = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "RunA");
                Assert.AreEqual(
                    "test_run_still_running",
                    firstTimeout.Value<string>("error_code"));

                api.CompleteSuccessfulRun("RunA.Test");
                api.RunId = secondRunId;
                JObject secondTimeout = await service.ExecuteTestsAsync(
                    TestMode.PlayMode,
                    false,
                    false,
                    "RunB");
                JObject secondRecord = service.GetTestRun(secondRunId);

                Assert.AreEqual(
                    "test_run_still_running",
                    secondTimeout.Value<string>("error_code"));
                Assert.AreEqual("running", secondRecord.Value<string>("status"));
                Assert.AreEqual(
                    "RunB",
                    secondRecord["filter"]?.Value<string>("testFilter"));
                Assert.That(secondRecord.ToString(), Does.Not.Contain("RunA.Test"));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task RestoredAndOriginalServicesUseEquivalentAdditiveResultTrees()
        {
            const string originalRunId = "55555555-5555-5555-5555-555555555555";
            const string restoredRunId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(RestoredAndOriginalServicesUseEquivalentAdditiveResultTrees));
            string registryKey = CreateRegistryKey(
                nameof(RestoredAndOriginalServicesUseEquivalentAdditiveResultTrees));
            try
            {
                var originalApi = new FakeTestRunnerApi(
                    originalRunId,
                    ArtifactSaveBehavior.ValidXml);
                var originalService = new TestRunnerService(
                    originalApi,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory,
                    _ => new TaskCompletionSource<bool>().Task);
                Task<JObject> originalPending = originalService.ExecuteTestsAsync(
                    TestMode.PlayMode,
                    false,
                    false,
                    "OriginalRun");
                originalApi.CompleteSuccessfulRun("OriginalRun.Test", sendTestFinished: true);
                JObject original = await originalPending;

                var sessionRegistry = new SessionStateTestRunRegistry(registryKey);
                var preReloadApi = new FakeTestRunnerApi(
                    restoredRunId,
                    ArtifactSaveBehavior.ValidXml);
                var preReloadService = new TestRunnerService(
                    preReloadApi,
                    sessionRegistry,
                    artifactDirectory,
                    _ => Task.CompletedTask);
                await preReloadService.ExecuteTestsAsync(
                    TestMode.PlayMode,
                    false,
                    false,
                    "RestoredRun");

                var resumedApi = new FakeTestRunnerApi(
                    restoredRunId,
                    ArtifactSaveBehavior.ValidXml);
                var resumedService = new TestRunnerService(
                    resumedApi,
                    new SessionStateTestRunRegistry(registryKey),
                    artifactDirectory,
                    _ => Task.CompletedTask);
                resumedApi.CompleteSuccessfulRun(
                    "RestoredRun.Test",
                    sendTestFinished: false);

                JObject polled = resumedService.GetTestRun();
                Assert.AreEqual(restoredRunId, polled.Value<string>("runId"));
                Assert.AreEqual("completed", polled.Value<string>("status"));
                Assert.AreEqual(
                    "RestoredRun",
                    polled["filter"]?.Value<string>("testFilter"));
                Assert.AreEqual(
                    "RestoredRun.Test",
                    polled["results"]?[0]?.Value<string>("fullName"));
                Assert.AreEqual(1, polled["results"]?.Count());
                Assert.AreEqual(1, original["results"]?.Count());
                Assert.AreEqual(
                    original.Value<int>("treeNodeCount"),
                    polled.Value<int>("treeNodeCount"));
            }
            finally
            {
                SessionState.EraseString(registryKey);
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task RestoredEmptyRunDoesNotSerializeRunRootAsTestResult()
        {
            const string runId = "abababab-abab-abab-abab-abababababab";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(RestoredEmptyRunDoesNotSerializeRunRootAsTestResult));
            string registryKey = CreateRegistryKey(
                nameof(RestoredEmptyRunDoesNotSerializeRunRootAsTestResult));
            try
            {
                var registry = new SessionStateTestRunRegistry(registryKey);
                var originalService = new TestRunnerService(
                    new FakeTestRunnerApi(runId, ArtifactSaveBehavior.ValidXml),
                    registry,
                    artifactDirectory,
                    _ => Task.CompletedTask);
                await originalService.ExecuteTestsAsync(
                    TestMode.PlayMode,
                    false,
                    false,
                    "NoMatches");

                var resumedApi = new FakeTestRunnerApi(runId, ArtifactSaveBehavior.ValidXml);
                var resumedService = new TestRunnerService(
                    resumedApi,
                    new SessionStateTestRunRegistry(registryKey),
                    artifactDirectory,
                    _ => Task.CompletedTask);
                resumedApi.CompleteEmptyRun();
                JObject polled = resumedService.GetTestRun(runId);

                Assert.AreEqual(0, polled.Value<int>("treeNodeCount"));
                Assert.AreEqual(0, polled["results"]?.Count());
                Assert.AreEqual("no_tests_matched", polled.Value<string>("error_code"));
            }
            finally
            {
                SessionState.EraseString(registryKey);
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public void SessionRegistryRoundTripsActiveRecentAndPlayModeFilterMetadata()
        {
            string registryKey = CreateRegistryKey(
                nameof(SessionRegistryRoundTripsActiveRecentAndPlayModeFilterMetadata));
            try
            {
                var registry = new SessionStateTestRunRegistry(registryKey);
                var record = new TestRunRecord
                {
                    RunId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                    Status = TestRunStatus.Running,
                    Filter = new JObject
                    {
                        ["testMode"] = "PlayMode",
                        ["testFilter"] = "My.PlayMode.Tests",
                        ["assemblyNames"] = new JArray("Tests.One", "!Tests.Two")
                    },
                    ArtifactPath = "/project/Library/McpUnity/TestResults/" +
                        "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb.xml",
                    StartedAt = "2026-09-02T00:00:00.0000000Z",
                    ReturnOnlyFailures = false,
                    ReturnWithLogs = true,
                    RunStartedObserved = true
                };

                registry.Upsert(record);
                TestRunRecord byId = registry.Get(record.RunId);
                TestRunRecord mostRecent = registry.GetMostRecent();
                TestRunRecord active = registry.GetActive();

                Assert.AreNotSame(record, byId);
                Assert.AreEqual(record.RunId, byId.RunId);
                Assert.AreEqual("PlayMode", byId.Filter.Value<string>("testMode"));
                CollectionAssert.AreEqual(
                    new[] { "Tests.One", "!Tests.Two" },
                    byId.Filter["assemblyNames"].ToObject<string[]>());
                Assert.IsTrue(byId.RunStartedObserved);
                Assert.AreEqual(record.RunId, mostRecent.RunId);
                Assert.AreEqual(record.RunId, active.RunId);
            }
            finally
            {
                SessionState.EraseString(registryKey);
            }
        }

        [Test]
        public void SessionRegistryPreservesDateLikeStringsExactly()
        {
            const string expectedStartedAt = "2026-09-02T00:00:00.0000000Z";
            const string expectedTestFilter = "2026-09-02T00:00:00Z";
            string registryKey = CreateRegistryKey(
                nameof(SessionRegistryPreservesDateLikeStringsExactly));
            try
            {
                var registry = new SessionStateTestRunRegistry(registryKey);
                registry.Upsert(new TestRunRecord
                {
                    RunId = "abababab-abab-abab-abab-abababababab",
                    Status = TestRunStatus.Running,
                    Filter = new JObject
                    {
                        ["testMode"] = "EditMode",
                        ["testFilter"] = expectedTestFilter
                    },
                    StartedAt = expectedStartedAt
                });

                TestRunRecord restored = registry.Get(
                    "abababab-abab-abab-abab-abababababab");

                Assert.AreEqual(expectedStartedAt, restored.StartedAt);
                Assert.AreEqual(
                    expectedTestFilter,
                    restored.Filter.Value<string>("testFilter"));
            }
            finally
            {
                SessionState.EraseString(registryKey);
            }
        }

        [Test]
        public void SessionRegistryRetainsOnlyMostRecentTwentyRecords()
        {
            string registryKey = CreateRegistryKey(
                nameof(SessionRegistryRetainsOnlyMostRecentTwentyRecords));
            try
            {
                var registry = new SessionStateTestRunRegistry(registryKey);
                var runIds = Enumerable.Range(1, SessionStateTestRunRegistry.MaxRecords + 1)
                    .Select(value => $"00000000-0000-0000-0000-{value:000000000000}")
                    .ToArray();
                foreach (string runId in runIds)
                {
                    registry.Upsert(new TestRunRecord
                    {
                        RunId = runId,
                        Status = TestRunStatus.Completed,
                        StartedAt = "2026-09-02T00:00:00.0000000Z"
                    });
                }

                Assert.IsNull(registry.Get(runIds[0]));
                Assert.AreEqual(
                    runIds[runIds.Length - 1],
                    registry.GetMostRecent().RunId);
                Assert.AreEqual(
                    SessionStateTestRunRegistry.MaxRecords,
                    JArray.Parse(SessionState.GetString(registryKey, string.Empty)).Count);
            }
            finally
            {
                SessionState.EraseString(registryKey);
            }
        }

        [Test]
        public void SessionRegistryMalformedJsonReturnsEmptyRegistry()
        {
            string registryKey = CreateRegistryKey(
                nameof(SessionRegistryMalformedJsonReturnsEmptyRegistry));
            try
            {
                SessionState.SetString(registryKey, "[not valid json");
                var registry = new SessionStateTestRunRegistry(registryKey);

                Assert.IsNull(registry.GetMostRecent());
                Assert.IsNull(registry.GetActive());
                Assert.IsNull(
                    registry.Get("cccccccc-cccc-cccc-cccc-cccccccccccc"));
            }
            finally
            {
                SessionState.EraseString(registryKey);
            }
        }

        [Test]
        public void SessionRegistryPersistsMetadataWithoutResultsOrLogs()
        {
            string registryKey = CreateRegistryKey(
                nameof(SessionRegistryPersistsMetadataWithoutResultsOrLogs));
            try
            {
                var registry = new SessionStateTestRunRegistry(registryKey);
                registry.Upsert(new TestRunRecord
                {
                    RunId = "dddddddd-dddd-dddd-dddd-dddddddddddd",
                    Status = TestRunStatus.Completed,
                    StartedAt = "2026-09-02T00:00:00.0000000Z",
                    Success = true,
                    Message = "1/1 passed",
                    TestCount = 1,
                    PassCount = 1
                });

                string serialized = SessionState.GetString(registryKey, string.Empty);
                Assert.That(serialized, Does.Not.Contain("\"results\""));
                Assert.That(serialized, Does.Not.Contain("\"logs\""));
                Assert.AreEqual(1, registry.GetMostRecent().TestCount);
            }
            finally
            {
                SessionState.EraseString(registryKey);
            }
        }

        [Test]
        public void PruneArtifactsDeletesOnlyOwnedFilesBeyondRetentionAndOwnedTemps()
        {
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(PruneArtifactsDeletesOnlyOwnedFilesBeyondRetentionAndOwnedTemps));
            try
            {
                Directory.CreateDirectory(artifactDirectory);
                string[] ownedArtifacts = Enumerable.Range(1, 21)
                    .Select(value => Path.Combine(
                        artifactDirectory,
                        $"10000000-0000-0000-0000-{value:000000000000}.xml"))
                    .ToArray();
                for (int index = 0; index < ownedArtifacts.Length; index++)
                {
                    File.WriteAllText(ownedArtifacts[index], "<test-run />");
                    File.SetLastWriteTimeUtc(
                        ownedArtifacts[index],
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddMinutes(index));
                }

                string unownedArtifact = Path.Combine(artifactDirectory, "external.xml");
                string ownedTemp = Path.Combine(
                    artifactDirectory,
                    "20000000-0000-0000-0000-000000000001.xml.tmp");
                File.WriteAllText(unownedArtifact, "external");
                File.WriteAllText(ownedTemp, "partial");

                var service = new TestRunnerService(
                    new FakeTestRunnerApi(
                        "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                        ArtifactSaveBehavior.ValidXml),
                    new InMemoryTestRunRegistry(),
                    artifactDirectory);
                service.PruneArtifacts();

                Assert.AreEqual(
                    20,
                    ownedArtifacts.Count(File.Exists));
                Assert.IsFalse(File.Exists(ownedArtifacts[0]));
                Assert.IsTrue(File.Exists(ownedArtifacts[20]));
                Assert.IsTrue(File.Exists(unownedArtifact));
                Assert.IsFalse(File.Exists(ownedTemp));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [TestCase("validMixed", true)]
        [TestCase("rootMismatch", false)]
        [TestCase("summaryMismatch", false)]
        [TestCase("identityMismatch", false)]
        [TestCase("swapStates", false)]
        [TestCase("empty", true)]
        public void ArtifactConsistencyChecksEveryLeaf(string condition, bool expected)
        {
            string directory = PrepareArtifactDirectory(nameof(ArtifactConsistencyChecksEveryLeaf) + condition);
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "result.xml");
                string[] states = condition == "empty" ? new string[0] :
                    new[] { "Passed", "Failed", "Skipped", "Inconclusive" };
                var root = new XElement("test-run", new XAttribute("result", states.Length == 0 ? "Passed" : "Failed"));
                var leaves = new JArray();
                for (int i = 0; i < states.Length; i++)
                {
                    root.Add(new XElement("test-case", new XAttribute("fullname", "Case" + i),
                        new XAttribute("result", states[i])));
                    leaves.Add(new JObject { ["fullName"] = "Case" + i, ["state"] = states[i] });
                }
                int count = states.Length == 0 ? 0 : 1;
                var summary = new JObject { ["testCount"] = states.Length, ["passCount"] = count,
                    ["failCount"] = count, ["skipCount"] = count, ["inconclusiveCount"] = count,
                    ["resultState"] = states.Length == 0 ? "Passed" : "Failed" };
                if (condition == "rootMismatch") root.SetAttributeValue("passed", 2);
                if (condition == "summaryMismatch") summary["passCount"] = 2;
                if (condition == "identityMismatch") leaves[0]["fullName"] = "WrongCase";
                if (condition == "swapStates") { leaves[0]["state"] = "Failed"; leaves[1]["state"] = "Passed"; }
                new XDocument(root).Save(path);
                Assert.AreEqual(expected, TestRunnerService.TryValidateArtifactConsistency(path, summary, leaves, out string error), error);
            }
            finally { DeleteArtifactDirectory(directory); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContradictoryArtifactCannotPublishOrReplayPass(bool tamperAfterCompletion)
        {
            string directory = PrepareArtifactDirectory(nameof(ContradictoryArtifactCannotPublishOrReplayPass) + tamperAfterCompletion);
            const string id = "29292929-2929-2929-2929-292929292929";
            try
            {
                var api = new FakeTestRunnerApi(id, ArtifactSaveBehavior.ValidXml);
                if (!tamperAfterCompletion) api.AfterSave = CorruptArtifactLeaf;
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), directory);
                Task<JObject> pending = service.ExecuteTestsAsync(TestMode.EditMode, true, false, "Identity");
                api.StartRun("Identity"); api.CompleteSuccessfulRun("Identity");
                Assert.IsTrue(pending.IsCompleted);
                JObject reply = pending.GetAwaiter().GetResult();
                if (tamperAfterCompletion)
                {
                    Assert.AreEqual("completed", reply.Value<string>("status"));
                    Assert.AreEqual(0, ((JArray)reply["results"]).Count);
                    CorruptArtifactLeaf(Path.Combine(directory, id + ".xml"));
                }
                else
                {
                    Assert.AreEqual("untrusted", reply.Value<string>("status"));
                    Assert.IsFalse(reply.Value<bool>("success"));
                    Assert.IsNull(reply["results"]); Assert.IsNull(reply["artifactPath"]);
                }
                JObject reread = service.GetTestRun(id);
                Assert.AreEqual("untrusted", reread.Value<string>("status"));
                Assert.IsFalse(reread.Value<bool>("success"));
                Assert.IsNull(reread["results"]); Assert.IsNull(reread["artifactPath"]);
                Assert.IsTrue(File.Exists(Path.Combine(directory, id + ".xml")));
            }
            finally { DeleteArtifactDirectory(directory); }
        }

        private static void CorruptArtifactLeaf(string path)
        {
            XDocument xml = XDocument.Load(path);
            xml.Descendants("test-case").First().SetAttributeValue("result", "Failed");
            xml.Save(path);
        }

        [Test, Timeout(2000)]
        public void StoppedFrameworkWithoutResultInvalidatesPendingRunAndAllowsExplicitRecovery()
        {
            string directory = PrepareArtifactDirectory(nameof(StoppedFrameworkWithoutResultInvalidatesPendingRunAndAllowsExplicitRecovery));
            DateTime now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
            const string oldId = "26262626-2626-2626-2626-262626262626";
            try
            {
                var api = new FakeTestRunnerApi(oldId, ArtifactSaveBehavior.ValidXml);
                var registry = new InMemoryTestRunRegistry();
                var service = new TestRunnerService(api, registry, directory,
                    _ => new TaskCompletionSource<bool>().Task, () => now, frameworkRunActive: () => false);
                Task<JObject> pending = service.ExecuteTestsAsync(TestMode.EditMode, false, false, "Cancelled");
                api.StartRun("Cancelled");
                Assert.AreEqual("running", service.GetTestRun(oldId).Value<string>("status"));
                now += TestRunnerService.InactiveRunGrace;
                Task<JObject> gateTask = service.ExecuteTestsAsync(TestMode.EditMode, false, false, "DoNotStartYet");
                Assert.IsTrue(gateTask.IsCompleted);
                JObject gate = gateTask.GetAwaiter().GetResult();
                Assert.AreEqual("untrusted", gate.Value<string>("status"));
                Assert.AreEqual(1, api.ExecuteCalls);
                Assert.IsTrue(pending.IsCompleted);
                JObject reply = pending.GetAwaiter().GetResult();
                Assert.AreEqual(oldId, reply.Value<string>("invalidatedRunId"));
                Assert.IsTrue(reply.Value<bool>("lockReleased"));
                Assert.IsNull(reply["results"]); Assert.IsNull(reply["artifactPath"]);
                api.CompleteSuccessfulRun("LateForeignResult");
                Assert.AreEqual("untrusted", service.GetTestRun(oldId).Value<string>("status"));
                Assert.IsFalse(File.Exists(Path.Combine(directory, oldId + ".xml")));
                api.RunId = "27272727-2727-2727-2727-272727272727";
                Task<JObject> recovery = service.ExecuteTestsAsync(TestMode.EditMode, false, false, "Recovery");
                api.StartRun("Recovery"); api.CompleteSuccessfulRun("Recovery");
                Assert.IsTrue(recovery.IsCompleted);
                Assert.AreEqual(api.RunId, recovery.GetAwaiter().GetResult().Value<string>("runId"));
                Assert.AreEqual("completed", service.GetTestRun(api.RunId).Value<string>("status"));
                Assert.AreEqual("untrusted", service.GetTestRun(oldId).Value<string>("status"));
            }
            finally { DeleteArtifactDirectory(directory); }
        }

        [TestCase("active")]
        [TestCase("unknown")]
        [TestCase("throws")]
        [TestCase("unstarted")]
        [TestCase("short")]
        [TestCase("reset")]
        public void InactiveProbeDoesNotReleaseUnconfirmedRun(string condition)
        {
            string directory = PrepareArtifactDirectory(nameof(InactiveProbeDoesNotReleaseUnconfirmedRun) + condition);
            DateTime now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
            bool? active = false;
            try
            {
                var api = new FakeTestRunnerApi("28282828-2828-2828-2828-282828282828", ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(api, new InMemoryTestRunRegistry(), directory,
                    _ => Task.CompletedTask, () => now, frameworkRunActive: () => {
                        if (condition == "throws") throw new InvalidOperationException("probe unavailable");
                        return active;
                    });
                Task<JObject> tracked = service.ExecuteTestsAsync(TestMode.EditMode, false, false, "StillTracked");
                Assert.IsTrue(tracked.IsCompleted);
                tracked.GetAwaiter().GetResult();
                if (condition != "unstarted") api.StartRun("StillTracked");
                if (condition == "active") active = true;
                if (condition == "unknown") active = null;
                service.GetTestRun(api.RunId);
                now += TimeSpan.FromSeconds(condition == "short" ? 1 : 3);
                if (condition == "reset") {
                    active = true; service.GetTestRun(api.RunId); active = false;
                }
                Assert.AreEqual("running", service.GetTestRun(api.RunId).Value<string>("status"));
                api.CompleteSuccessfulRun("StillTracked");
                Assert.AreEqual("completed", service.GetTestRun(api.RunId).Value<string>("status"));
            }
            finally { DeleteArtifactDirectory(directory); }
        }

        [Test]
        public async Task StaleActiveRunReleasesLockAndDisclosesRetry()
        {
            const string staleRunId = "ffffffff-ffff-ffff-ffff-ffffffffffff";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(StaleActiveRunReleasesLockAndDisclosesRetry));
            DateTime now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
            try
            {
                var registry = new InMemoryTestRunRegistry();
                registry.Upsert(new TestRunRecord
                {
                    RunId = staleRunId,
                    Status = TestRunStatus.Running,
                    StartedAt = now.Subtract(TestRunnerService.ActiveRunTtl)
                        .AddSeconds(-1)
                        .ToString("o")
                });
                var api = new FakeTestRunnerApi(
                    "12121212-1212-1212-1212-121212121212",
                    ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    registry,
                    artifactDirectory,
                    _ => Task.CompletedTask,
                    () => now);

                JObject released = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "RetryMe");
                JObject retry = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "Replacement");

                Assert.AreEqual(
                    "test_run_in_progress",
                    released.Value<string>("error_code"));
                Assert.AreEqual("stale", released.Value<string>("status"));
                Assert.IsTrue(released.Value<bool>("lockReleased"));
                Assert.That(released.Value<string>("message"), Does.Contain("retry run_tests"));
                Assert.AreEqual(
                    "test_run_still_running",
                    retry.Value<string>("error_code"));
                Assert.AreEqual(1, api.ExecuteCalls);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task SecondRunStartedInvalidatesRecordWithoutAttributingResult()
        {
            const string unityRunId = "13131313-1313-1313-1313-131313131313";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(SecondRunStartedInvalidatesRecordWithoutAttributingResult));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var pendingDelay = new TaskCompletionSource<bool>();
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory,
                    _ => pendingDelay.Task);
                Task<JObject> pending = service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "OwnedRun");

                api.StartRun("OwnedRun");
                api.StartRun("Test Runner Window Run");
                JObject invalidated = await pending;
                api.CompleteSuccessfulRun("Foreign.Test");
                JObject polled = service.GetTestRun(unityRunId);

                Assert.AreEqual("untrusted", invalidated.Value<string>("status"));
                Assert.IsNull(invalidated["runId"]);
                Assert.AreEqual(
                    unityRunId,
                    invalidated.Value<string>("invalidatedRunId"));
                Assert.IsNull(invalidated["results"]);
                Assert.AreEqual("untrusted", polled.Value<string>("status"));
                Assert.IsNull(polled["runId"]);
                Assert.AreEqual(
                    unityRunId,
                    polled.Value<string>("requestedRunId"));
                Assert.IsFalse(File.Exists(
                    Path.Combine(artifactDirectory, $"{unityRunId}.xml")));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public async Task InvalidUnityRunIdNeverBuildsArtifactPath()
        {
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(InvalidUnityRunIdNeverBuildsArtifactPath));
            try
            {
                var api = new FakeTestRunnerApi("../escape", ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory);

                JObject response = await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "InvalidGuid");

                Assert.AreEqual(
                    "test_run_start_failed",
                    response["error"]?["type"]?.ToString());
                Assert.IsFalse(Directory.Exists(artifactDirectory));
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        [Test]
        public void InvalidGetRunIdUsesExistingValidationEnvelope()
        {
            var service = new TestRunnerService(
                new FakeTestRunnerApi(
                    "14141414-1414-1414-1414-141414141414",
                    ArtifactSaveBehavior.ValidXml),
                new InMemoryTestRunRegistry(),
                Path.GetTempPath());

            JObject response = service.GetTestRun("../escape");

            Assert.AreEqual(
                "validation_error",
                response["error"]?["type"]?.ToString());
        }

        [Test]
        public async Task ReentrantRunBeforeGuidAssignmentReturnsStateErrorWithoutNre()
        {
            const string unityRunId = "15151515-1515-1515-1515-151515151515";
            string artifactDirectory = PrepareArtifactDirectory(
                nameof(ReentrantRunBeforeGuidAssignmentReturnsStateErrorWithoutNre));
            try
            {
                var api = new FakeTestRunnerApi(unityRunId, ArtifactSaveBehavior.ValidXml);
                var service = new TestRunnerService(
                    api,
                    new InMemoryTestRunRegistry(),
                    artifactDirectory,
                    _ => Task.CompletedTask);
                Task<JObject> reentrant = null;
                api.OnExecute = () =>
                {
                    api.OnExecute = null;
                    reentrant = service.ExecuteTestsAsync(
                        TestMode.PlayMode,
                        false,
                        false,
                        "Reentrant");
                };

                await service.ExecuteTestsAsync(
                    TestMode.EditMode,
                    false,
                    false,
                    "Original");
                JObject response = await reentrant;

                Assert.AreEqual(
                    "test_run_state_invalid",
                    response["error"]?["type"]?.ToString());
                Assert.AreEqual(1, api.ExecuteCalls);
            }
            finally
            {
                DeleteArtifactDirectory(artifactDirectory);
            }
        }

        private static JObject BuildResponse(
            string testFilter = null,
            string[] assemblyNames = null)
        {
            return TestRunnerService.BuildResponse(
                new List<JObject>(),
                "EditMode",
                "Passed",
                0.0014327,
                1,
                0,
                0,
                0,
                0,
                "EditMode",
                testFilter,
                assemblyNames);
        }

        private static void AssertNoTestsMatched(JObject response)
        {
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual("no_tests_matched", response.Value<string>("error_code"));
            Assert.AreEqual(0, response.Value<int>("testCount"));
            Assert.AreEqual(1, response.Value<int>("treeNodeCount"));
            Assert.AreEqual("Passed", response.Value<string>("resultState"));
            Assert.That(response["message"]?.ToString(), Does.Contain("testMode=EditMode"));
            Assert.That(response["message"]?.ToString(), Does.Contain("full test names"));
            Assert.That(response["message"]?.ToString(), Does.Contain("namespace"));
            Assert.That(response["message"]?.ToString(), Does.Contain("assemblyNames"));
            Assert.That(response["message"]?.ToString(), Does.Contain("get_tests"));
        }

        private abstract class ThrowingTestResultAdaptor : ITestResultAdaptor
        {
            public virtual ITestAdaptor Test => throw new System.NotImplementedException();
            public virtual string Name => throw new System.NotImplementedException();
            public virtual string FullName => throw new System.NotImplementedException();
            public virtual string ResultState => throw new System.NotImplementedException();
            public virtual UnityEditor.TestTools.TestRunner.Api.TestStatus TestStatus => throw new System.NotImplementedException();
            public virtual double Duration => throw new System.NotImplementedException();
            public virtual System.DateTime StartTime => throw new System.NotImplementedException();
            public virtual System.DateTime EndTime => throw new System.NotImplementedException();
            public virtual string Message => throw new System.NotImplementedException();
            public virtual string StackTrace => throw new System.NotImplementedException();
            public virtual int AssertCount => throw new System.NotImplementedException();
            public virtual int FailCount => throw new System.NotImplementedException();
            public virtual int PassCount => throw new System.NotImplementedException();
            public virtual int SkipCount => throw new System.NotImplementedException();
            public virtual int InconclusiveCount => throw new System.NotImplementedException();
            public virtual bool HasChildren => throw new System.NotImplementedException();
            public virtual IEnumerable<ITestResultAdaptor> Children => throw new System.NotImplementedException();
            public virtual string Output => throw new System.NotImplementedException();
            public virtual TNode ToXml() => throw new System.NotImplementedException();
        }

        private sealed class FakeSuiteResultAdaptor : ThrowingTestResultAdaptor
        {
            public override bool HasChildren => true;
        }

        private sealed class FakeLeafResultAdaptor : ThrowingTestResultAdaptor
        {
            private readonly string _name;
            private readonly string _fullName;
            private readonly string _resultState;
            private readonly string _message;
            private readonly double _duration;
            private readonly string _output;
            private readonly bool _outputImplemented;
            private readonly string _stackTrace;

            public FakeLeafResultAdaptor(
                string name,
                string fullName,
                string resultState,
                string message,
                double duration,
                string output = null,
                string stackTrace = null,
                bool outputImplemented = true)
            {
                _name = name;
                _fullName = fullName;
                _resultState = resultState;
                _message = message;
                _duration = duration;
                _output = output;
                _outputImplemented = outputImplemented;
                _stackTrace = stackTrace;
            }

            public override string Name => _name;
            public override string FullName => _fullName;
            public override string ResultState => _resultState;
            public override string Message => _message;
            public override double Duration => _duration;
            public override string Output => _outputImplemented
                ? _output
                : throw new System.NotImplementedException();
            public override string StackTrace => _stackTrace;
            public override bool HasChildren => false;
        }

        private sealed class FakeSummaryResultAdaptor : ThrowingTestResultAdaptor
        {
            private readonly ITestAdaptor _test;
            private readonly string _resultState;
            private readonly double _duration;
            private readonly int _passCount;
            private readonly int _failCount;
            private readonly int _skipCount;
            private readonly int _inconclusiveCount;
            private readonly IReadOnlyList<ITestResultAdaptor> _children;

            public FakeSummaryResultAdaptor(
                string runName,
                string resultState,
                double duration,
                int passCount,
                int failCount,
                int skipCount,
                int inconclusiveCount,
                IReadOnlyList<ITestResultAdaptor> children = null)
            {
                _test = new FakeTestAdaptor(runName);
                _resultState = resultState;
                _duration = duration;
                _passCount = passCount;
                _failCount = failCount;
                _skipCount = skipCount;
                _inconclusiveCount = inconclusiveCount;
                _children = children;
            }

            public override ITestAdaptor Test => _test;
            public override string ResultState => _resultState;
            public override double Duration => _duration;
            public override int PassCount => _passCount;
            public override int FailCount => _failCount;
            public override int SkipCount => _skipCount;
            public override int InconclusiveCount => _inconclusiveCount;
            public override bool HasChildren => _children != null && _children.Count > 0;
            public override IEnumerable<ITestResultAdaptor> Children =>
                _children ?? Enumerable.Empty<ITestResultAdaptor>();
        }

        public enum ArtifactSaveBehavior
        {
            ValidXml,
            Throws,
            WritesNothing,
            WritesMalformedXml,
            WritesWrongRootXml
        }

        private sealed class FakeTestRunnerApi : ITestRunnerApi
        {
            private readonly ArtifactSaveBehavior _saveBehavior;
            private ICallbacks _callbacks;
            private string _lastCompletedFullName;
            private bool _lastRunWasEmpty;

            public int ExecuteCalls { get; private set; }
            public string RunId { get; set; }
            public Action OnExecute { get; set; }
            public Action<string> AfterSave { get; set; }

            public FakeTestRunnerApi(string runId, ArtifactSaveBehavior saveBehavior)
            {
                RunId = runId;
                _saveBehavior = saveBehavior;
            }

            public string Execute(ExecutionSettings executionSettings)
            {
                ExecuteCalls++;
                OnExecute?.Invoke();
                return RunId;
            }

            public void RegisterCallbacks(ICallbacks callbacks)
            {
                _callbacks = callbacks;
            }

            public void RetrieveTestList(TestMode testMode, Action<ITestAdaptor> callback)
            {
                throw new NotImplementedException();
            }

            public void SaveResultToFile(ITestResultAdaptor results, string xmlFilePath)
            {
                if (_saveBehavior == ArtifactSaveBehavior.Throws)
                {
                    throw new IOException("simulated artifact failure");
                }
                if (_saveBehavior == ArtifactSaveBehavior.ValidXml)
                {
                    var root = new XElement("test-run");
                    if (!_lastRunWasEmpty)
                    {
                        root.Add(new XElement(
                            "test-suite",
                            new XElement(
                                "test-case",
                                new XAttribute("name", "Test"),
                                new XAttribute(
                                    "fullname",
                                    _lastCompletedFullName ?? "Fake.Test"),
                                new XAttribute("result", "Passed"),
                                new XAttribute("duration", "0.1"))));
                    }
                    var document = new XDocument(root);
                    document.Save(xmlFilePath);
                    AfterSave?.Invoke(xmlFilePath);
                }
                if (_saveBehavior == ArtifactSaveBehavior.WritesMalformedXml)
                {
                    File.WriteAllText(xmlFilePath, "<test-run>");
                }
                if (_saveBehavior == ArtifactSaveBehavior.WritesWrongRootXml)
                {
                    var document = new XDocument(new XElement("not-test-run"));
                    document.Save(xmlFilePath);
                }
            }

            public void StartRun(string name)
            {
                _callbacks.RunStarted(new FakeTestAdaptor(name));
            }

            public void CompleteSuccessfulRun(
                string fullName,
                bool sendTestFinished = true)
            {
                _lastCompletedFullName = fullName;
                _lastRunWasEmpty = false;
                var leaf = new FakeLeafResultAdaptor(
                    "Test",
                    fullName,
                    "Passed",
                    null,
                    0.1,
                    null,
                    null);
                if (sendTestFinished)
                {
                    _callbacks.TestFinished(leaf);
                }
                _callbacks.RunFinished(new FakeSummaryResultAdaptor(
                    "FakeRun",
                    "Passed",
                    0.1,
                    1,
                    0,
                    0,
                    0,
                    new[] { leaf }));
            }

            public void CompleteEmptyRun()
            {
                _lastCompletedFullName = null;
                _lastRunWasEmpty = true;
                _callbacks.RunFinished(new FakeSummaryResultAdaptor(
                    "FakeRun",
                    "Passed",
                    0.01,
                    0,
                    0,
                    0,
                    0));
            }
        }

        private sealed class InMemoryTestRunRegistry : ITestRunRegistry
        {
            private readonly List<TestRunRecord> _records = new List<TestRunRecord>();

            public TestRunRecord Get(string runId)
            {
                return Clone(_records.LastOrDefault(record => record.RunId == runId));
            }

            public TestRunRecord GetMostRecent()
            {
                return Clone(_records.LastOrDefault());
            }

            public TestRunRecord GetActive()
            {
                return Clone(_records.LastOrDefault(
                    record => record.Status == TestRunStatus.Running));
            }

            public void Upsert(TestRunRecord record)
            {
                _records.RemoveAll(existing => existing.RunId == record.RunId);
                _records.Add(Clone(record));
            }

            private static TestRunRecord Clone(TestRunRecord record)
            {
                return record == null
                    ? null
                    : TestRunRecord.FromJson(record.ToJson());
            }
        }

        private static string CreateRegistryKey(string testName)
        {
            return $"McpUnity.Tests.TestRunnerResultTests.{testName}.{Guid.NewGuid():N}";
        }

        private static int CountDirectCalls(MethodInfo caller, MethodInfo expectedCallee)
        {
            Assert.IsNotNull(caller, "Expected caller method to exist.");
            Assert.IsNotNull(expectedCallee, "Expected callee method to exist.");
            Assert.AreEqual(
                caller.Module,
                expectedCallee.Module,
                "Direct-call count requires caller and callee to share one module.");
            byte[] il = caller.GetMethodBody()?.GetILAsByteArray();
            Assert.IsNotNull(il, $"Method '{caller.Name}' has no readable IL body.");

            int count = 0;
            for (int index = 0; index + sizeof(int) < il.Length; index++)
            {
                if ((il[index] == 0x28 || il[index] == 0x6f)
                    && BitConverter.ToInt32(il, index + 1) == expectedCallee.MetadataToken)
                {
                    count++;
                }
            }
            return count;
        }

        private static string PrepareArtifactDirectory(string testName)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "McpUnity-TestRunnerServiceTests",
                testName);
            DeleteArtifactDirectory(path);
            return path;
        }

        private static void DeleteArtifactDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class FakeTestAdaptor : ITestAdaptor
        {
            private readonly string _name;

            public FakeTestAdaptor(string name)
            {
                _name = name;
            }

            public string Name => _name;
            public string Id => throw new System.NotImplementedException();
            public string FullName => throw new System.NotImplementedException();
            public int TestCaseCount => throw new System.NotImplementedException();
            public bool HasChildren => throw new System.NotImplementedException();
            public bool IsSuite => throw new System.NotImplementedException();
            public IEnumerable<ITestAdaptor> Children => throw new System.NotImplementedException();
            public ITestAdaptor Parent => throw new System.NotImplementedException();
            public int TestCaseTimeout => throw new System.NotImplementedException();
            public ITypeInfo TypeInfo => throw new System.NotImplementedException();
            public IMethodInfo Method => throw new System.NotImplementedException();
            public object[] Arguments => throw new System.NotImplementedException();
            public string[] Categories => throw new System.NotImplementedException();
            public bool IsTestAssembly => throw new System.NotImplementedException();
            public UnityEditor.TestTools.TestRunner.Api.RunState RunState => throw new System.NotImplementedException();
            public string Description => throw new System.NotImplementedException();
            public string SkipReason => throw new System.NotImplementedException();
            public string ParentId => throw new System.NotImplementedException();
            public string ParentFullName => throw new System.NotImplementedException();
            public string UniqueName => throw new System.NotImplementedException();
            public string ParentUniqueName => throw new System.NotImplementedException();
            public int ChildIndex => throw new System.NotImplementedException();
            public TestMode TestMode => throw new System.NotImplementedException();
        }

        private static IEnumerator InvokeSocketCoroutine(
            string methodName,
            object executable,
            TaskCompletionSource<JObject> completionSource)
        {
            MethodInfo method = typeof(McpUnitySocketHandler).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"Expected McpUnitySocketHandler.{methodName} to exist.");
            return (IEnumerator)method.Invoke(
                new McpUnitySocketHandler(null),
                new[] { executable, new JObject(), completionSource });
        }

        private sealed class CompletingThenThrowingTool : McpToolBase
        {
            public CompletingThenThrowingTool()
            {
                Name = "completion_then_throw";
                IsAsync = true;
            }

            public override void ExecuteAsync(
                JObject parameters,
                TaskCompletionSource<JObject> completionSource)
            {
                completionSource.SetResult(new JObject { ["source"] = "first" });
                throw new InvalidOperationException("after completion");
            }
        }

        private sealed class CompletingThenThrowingResource : McpResourceBase
        {
            public CompletingThenThrowingResource()
            {
                Name = "completion_then_throw";
                IsAsync = true;
            }

            public override void FetchAsync(
                JObject parameters,
                TaskCompletionSource<JObject> completionSource)
            {
                completionSource.SetResult(new JObject { ["source"] = "first" });
                throw new InvalidOperationException("after completion");
            }
        }

        private sealed class RecordingTestRunnerService : ITestRunnerService
        {
            public int ExecuteCalls { get; private set; }
            public TestMode? LastTestMode { get; private set; }

            public Task<List<ITestAdaptor>> GetAllTestsAsync(string testModeFilter = "")
            {
                return Task.FromResult(new List<ITestAdaptor>());
            }

            public Task<JObject> ExecuteTestsAsync(
                TestMode testMode,
                bool returnOnlyFailures,
                bool returnWithLogs,
                string testFilter,
                string[] assemblyNames = null)
            {
                ExecuteCalls++;
                LastTestMode = testMode;
                return Task.FromResult(new JObject { ["success"] = true });
            }

            public JObject GetTestRun(string runId = null)
            {
                return new JObject { ["success"] = false };
            }
        }

        private sealed class ThrowingTestRunnerService : ITestRunnerService
        {
            public async Task<List<ITestAdaptor>> GetAllTestsAsync(string testModeFilter = "")
            {
                await Task.Yield();
                throw new InvalidOperationException("get tests exploded");
            }

            public async Task<JObject> ExecuteTestsAsync(
                TestMode testMode,
                bool returnOnlyFailures,
                bool returnWithLogs,
                string testFilter,
                string[] assemblyNames = null)
            {
                await Task.Yield();
                throw new InvalidOperationException("run tests exploded");
            }

            public JObject GetTestRun(string runId = null)
            {
                return new JObject { ["success"] = false };
            }
        }

        private sealed class ControllableTestRunnerService : ITestRunnerService
        {
            private readonly TaskCompletionSource<List<ITestAdaptor>> _getTests =
                new TaskCompletionSource<List<ITestAdaptor>>();
            private readonly TaskCompletionSource<JObject> _run =
                new TaskCompletionSource<JObject>();

            public Task<List<ITestAdaptor>> GetAllTestsAsync(string testModeFilter = "")
            {
                return _getTests.Task;
            }

            public Task<JObject> ExecuteTestsAsync(
                TestMode testMode,
                bool returnOnlyFailures,
                bool returnWithLogs,
                string testFilter,
                string[] assemblyNames = null)
            {
                return _run.Task;
            }

            public JObject GetTestRun(string runId = null)
            {
                return new JObject { ["success"] = false };
            }

            public void CompleteGetTests(List<ITestAdaptor> tests)
            {
                _getTests.SetResult(tests);
            }

            public void CompleteRun(JObject result)
            {
                _run.SetResult(result);
            }
        }
    }
}
