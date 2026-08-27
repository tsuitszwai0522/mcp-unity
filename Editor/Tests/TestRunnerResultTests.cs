using System.Collections.Generic;
using System.Threading.Tasks;
using McpUnity.Services;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor.TestTools.TestRunner.Api;

namespace McpUnity.Tests
{
    public class TestRunnerResultTests
    {
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

            public FakeSummaryResultAdaptor(
                string runName,
                string resultState,
                double duration,
                int passCount,
                int failCount,
                int skipCount,
                int inconclusiveCount)
            {
                _test = new FakeTestAdaptor(runName);
                _resultState = resultState;
                _duration = duration;
                _passCount = passCount;
                _failCount = failCount;
                _skipCount = skipCount;
                _inconclusiveCount = inconclusiveCount;
            }

            public override ITestAdaptor Test => _test;
            public override string ResultState => _resultState;
            public override double Duration => _duration;
            public override int PassCount => _passCount;
            public override int FailCount => _failCount;
            public override int SkipCount => _skipCount;
            public override int InconclusiveCount => _inconclusiveCount;
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
        }
    }
}
