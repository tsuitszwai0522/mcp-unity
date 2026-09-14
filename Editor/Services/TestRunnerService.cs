using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace McpUnity.Services
{
    internal static class TestRunStatus
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string FailedToSave = "failed_to_save";
        public const string Stale = "stale";
        public const string Untrusted = "untrusted";
        public const string Unknown = "unknown";
    }

    internal sealed class TestRunRecord
    {
        public string RunId;
        public string Status;
        public JObject Filter;
        public string ArtifactPath;
        public string StartedAt;
        public bool ReturnOnlyFailures;
        public bool ReturnWithLogs;
        public bool RunStartedObserved;
        public bool? Success;
        public string Type;
        public string Message;
        public string ErrorCode;
        public string ResultState;
        public double? DurationSeconds;
        public int? TestCount;
        public int? TreeNodeCount;
        public int? PassCount;
        public int? FailCount;
        public int? SkipCount;
        public int? InconclusiveCount;
        public JObject ArtifactError;

        public JObject ToJson()
        {
            return new JObject
            {
                ["runId"] = RunId,
                ["status"] = Status,
                ["filter"] = Filter?.DeepClone(),
                ["artifactPath"] = ArtifactPath != null
                    ? new JValue(ArtifactPath)
                    : JValue.CreateNull(),
                ["startedAt"] = StartedAt,
                ["returnOnlyFailures"] = ReturnOnlyFailures,
                ["returnWithLogs"] = ReturnWithLogs,
                ["runStartedObserved"] = RunStartedObserved,
                ["success"] = Success != null
                    ? new JValue(Success.Value)
                    : JValue.CreateNull(),
                ["type"] = Type,
                ["message"] = Message,
                ["error_code"] = ErrorCode,
                ["resultState"] = ResultState,
                ["durationSeconds"] = DurationSeconds != null
                    ? new JValue(DurationSeconds.Value)
                    : JValue.CreateNull(),
                ["testCount"] = TestCount != null
                    ? new JValue(TestCount.Value)
                    : JValue.CreateNull(),
                ["treeNodeCount"] = TreeNodeCount != null
                    ? new JValue(TreeNodeCount.Value)
                    : JValue.CreateNull(),
                ["passCount"] = PassCount != null
                    ? new JValue(PassCount.Value)
                    : JValue.CreateNull(),
                ["failCount"] = FailCount != null
                    ? new JValue(FailCount.Value)
                    : JValue.CreateNull(),
                ["skipCount"] = SkipCount != null
                    ? new JValue(SkipCount.Value)
                    : JValue.CreateNull(),
                ["inconclusiveCount"] = InconclusiveCount != null
                    ? new JValue(InconclusiveCount.Value)
                    : JValue.CreateNull(),
                ["artifactError"] = ArtifactError?.DeepClone()
            };
        }

        public static TestRunRecord FromJson(JObject json)
        {
            string runId = json.Value<string>("runId");
            if (!TestRunnerService.TryNormalizeRunId(runId, out string normalizedRunId))
            {
                return null;
            }

            return new TestRunRecord
            {
                RunId = normalizedRunId,
                Status = json.Value<string>("status") ?? TestRunStatus.Unknown,
                Filter = (json["filter"] as JObject)?.DeepClone() as JObject,
                ArtifactPath = json.Value<string>("artifactPath"),
                StartedAt = json.Value<string>("startedAt"),
                ReturnOnlyFailures = json.Value<bool?>("returnOnlyFailures") ?? true,
                ReturnWithLogs = json.Value<bool?>("returnWithLogs") ?? false,
                RunStartedObserved = json.Value<bool?>("runStartedObserved") ?? false,
                Success = json.Value<bool?>("success"),
                Type = json.Value<string>("type"),
                Message = json.Value<string>("message"),
                ErrorCode = json.Value<string>("error_code"),
                ResultState = json.Value<string>("resultState"),
                DurationSeconds = json.Value<double?>("durationSeconds"),
                TestCount = json.Value<int?>("testCount"),
                TreeNodeCount = json.Value<int?>("treeNodeCount"),
                PassCount = json.Value<int?>("passCount"),
                FailCount = json.Value<int?>("failCount"),
                SkipCount = json.Value<int?>("skipCount"),
                InconclusiveCount = json.Value<int?>("inconclusiveCount"),
                ArtifactError = (json["artifactError"] as JObject)?.DeepClone() as JObject
            };
        }
    }

    internal interface ITestRunRegistry
    {
        TestRunRecord Get(string runId);
        TestRunRecord GetMostRecent();
        TestRunRecord GetActive();
        void Upsert(TestRunRecord record);
    }

    internal sealed class SessionStateTestRunRegistry : ITestRunRegistry
    {
        internal const string DefaultRegistryKey = "McpUnity.TestRunnerService.RunRegistry";
        internal const int MaxRecords = 20;

        private readonly string _registryKey;

        public SessionStateTestRunRegistry(string registryKey = DefaultRegistryKey)
        {
            _registryKey = string.IsNullOrEmpty(registryKey)
                ? throw new ArgumentException("Registry key must not be empty.", nameof(registryKey))
                : registryKey;
        }

        public TestRunRecord Get(string runId)
        {
            return Load().LastOrDefault(record =>
                string.Equals(record.RunId, runId, StringComparison.Ordinal));
        }

        public TestRunRecord GetMostRecent()
        {
            return Load().LastOrDefault();
        }

        public TestRunRecord GetActive()
        {
            return Load().LastOrDefault(record => record.Status == TestRunStatus.Running);
        }

        public void Upsert(TestRunRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (!TestRunnerService.TryNormalizeRunId(record.RunId, out string normalizedRunId))
            {
                throw new ArgumentException(
                    "Test run records require a GUID runId in D format.",
                    nameof(record));
            }
            record.RunId = normalizedRunId;

            List<TestRunRecord> records = Load();
            records.RemoveAll(existing =>
                string.Equals(existing.RunId, record.RunId, StringComparison.Ordinal));
            records.Add(record);

            if (records.Count > MaxRecords)
            {
                records.RemoveRange(0, records.Count - MaxRecords);
            }

            var serialized = new JArray(records.Select(existing => existing.ToJson()));
            SessionState.SetString(_registryKey, serialized.ToString(Formatting.None));
        }

        private List<TestRunRecord> Load()
        {
            string serialized = SessionState.GetString(_registryKey, string.Empty);
            if (string.IsNullOrEmpty(serialized))
            {
                return new List<TestRunRecord>();
            }

            try
            {
                using (var stringReader = new StringReader(serialized))
                using (var jsonReader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None
                })
                {
                    return JArray.Load(jsonReader)
                        .OfType<JObject>()
                        .Select(TestRunRecord.FromJson)
                        .Where(record => record != null)
                        .ToList();
                }
            }
            catch (JsonException exception)
            {
                McpLogger.LogWarning(
                    $"Could not read persisted test run registry: {exception.Message}");
                return new List<TestRunRecord>();
            }
        }
    }

    /// <summary>
    /// Service for accessing Unity Test Runner functionality.
    /// A single active run is tracked by Unity's Execute() GUID. Because ICallbacks does not carry
    /// that GUID, a second RunStarted invalidates the record instead of guessing attribution.
    /// </summary>
    public class TestRunnerService : ITestRunnerService, ICallbacks
    {
        private const int ArtifactRetentionCount = 20;
        private const string ArtifactWriteErrorCode = "test_result_artifact_write_failed";
        private const string ArtifactUnavailableErrorCode = "test_result_artifact_unavailable";
        private const double UnityWaitBudgetRatio = 0.75;

        internal static readonly TimeSpan ActiveRunTtl = TimeSpan.FromHours(24);

        private sealed class ActiveRun
        {
            public readonly TestRunRecord Record;
            public readonly TaskCompletionSource<JObject> CompletionSource;

            public ActiveRun(
                TestRunRecord record,
                TaskCompletionSource<JObject> completionSource)
            {
                Record = record;
                CompletionSource = completionSource;
            }
        }

        private readonly ITestRunnerApi _testRunnerApi;
        private readonly ITestRunRegistry _registry;
        private readonly string _artifactDirectory;
        private readonly Func<TimeSpan, Task> _delay;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<string> _pruneArtifacts;
        private ActiveRun _activeRun;
        private readonly Func<bool?> _frameworkRunActive;
        private DateTime? _inactiveObservedAt;
        private string _inactiveObservedRunId;
        internal static readonly TimeSpan InactiveRunGrace = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Constructor. The optional API seam allows deterministic tests while preserving the
        /// existing new TestRunnerService() production call shape.
        /// </summary>
        public TestRunnerService(ITestRunnerApi testRunnerApi = null)
            : this(
                testRunnerApi ?? new UnityTestRunnerApi(),
                new SessionStateTestRunRegistry(),
                GetDefaultArtifactDirectory(),
                duration => Task.Delay(duration),
                () => DateTime.UtcNow,
                frameworkRunActive: testRunnerApi == null || testRunnerApi is UnityTestRunnerApi
                    ? (Func<bool?>)UnityTestRunnerApi.GetFrameworkRunActive
                    : null)
        {
        }

        internal TestRunnerService(
            ITestRunnerApi testRunnerApi,
            ITestRunRegistry registry,
            string artifactDirectory,
            Func<TimeSpan, Task> delay = null,
            Func<DateTime> utcNow = null,
            Action<string> pruneArtifacts = null,
            Func<bool?> frameworkRunActive = null)
        {
            _testRunnerApi = testRunnerApi ?? throw new ArgumentNullException(nameof(testRunnerApi));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _artifactDirectory = artifactDirectory ?? throw new ArgumentNullException(nameof(artifactDirectory));
            _delay = delay ?? (duration => Task.Delay(duration));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _pruneArtifacts = pruneArtifacts ?? (Action<string>)PruneArtifacts;
            _frameworkRunActive = frameworkRunActive;
            _testRunnerApi.RegisterCallbacks(this);

            TestRunRecord persistedRun = _registry.GetActive();
            if (persistedRun != null)
            {
                _activeRun = new ActiveRun(persistedRun, null);
            }
        }

        /// <summary>
        /// Async retrieval of all tests using TestRunnerApi callbacks.
        /// </summary>
        public async Task<List<ITestAdaptor>> GetAllTestsAsync(string testModeFilter = "")
        {
            var tests = new List<ITestAdaptor>();
            var tasks = new List<Task<List<ITestAdaptor>>>();

            if (string.IsNullOrEmpty(testModeFilter) ||
                testModeFilter.Equals("EditMode", StringComparison.OrdinalIgnoreCase))
            {
                tasks.Add(RetrieveTestsAsync(TestMode.EditMode));
            }
            if (string.IsNullOrEmpty(testModeFilter) ||
                testModeFilter.Equals("PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                tasks.Add(RetrieveTestsAsync(TestMode.PlayMode));
            }

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                tests.AddRange(result);
            }

            return tests;
        }

        /// <summary>
        /// Executes tests and returns the existing complete summary plus Unity's run GUID and
        /// the verified NUnit XML artifact path.
        /// </summary>
        public async Task<JObject> ExecuteTestsAsync(
            TestMode testMode,
            bool returnOnlyFailures,
            bool returnWithLogs,
            string testFilter = "",
            string[] assemblyNames = null)
        {
            TestRunRecord persistedActiveRun = _registry.GetActive();
            if (_activeRun != null || persistedActiveRun != null)
            {
                TestRunRecord activeRecord = _activeRun?.Record ?? persistedActiveRun;
                string activeRunId = _activeRun?.Record.RunId ?? persistedActiveRun?.RunId;
                if (string.IsNullOrEmpty(activeRunId))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "The active Unity test run has no run GUID. This is an internal state error; " +
                        "wait for the current Execute call to finish or restart the Editor.",
                        "test_run_state_invalid");
                }

                if (ReconcileInactiveRun(activeRecord))
                {
                    // 本次僅回報舊 run 終止；下一個明確請求才可開新 run。
                    return GetTestRun(activeRecord.RunId);
                }
                if (IsStale(activeRecord, _utcNow(), out string staleReason))
                {
                    return ReleaseStaleRun(activeRecord, staleReason);
                }

                return CreateErrorResponse(
                    "test_run_in_progress",
                    $"Test run '{activeRunId}' is still running. Only one run can be tracked at a time. " +
                    "Poll it with get_test_run before starting another run. The lock is released by " +
                    $"RunFinished, or as stale after {ActiveRunTtl.TotalHours:0} hours if Unity never emits RunFinished.",
                    new JObject
                    {
                        ["activeRunId"] = activeRunId,
                        ["status"] = TestRunStatus.Running,
                        ["lockReleased"] = false,
                        ["staleAfterSeconds"] = (long)ActiveRunTtl.TotalSeconds
                    });
            }

            string normalizedTestFilter = string.IsNullOrEmpty(testFilter) ? null : testFilter;
            string[] normalizedAssemblyNames = assemblyNames != null && assemblyNames.Length > 0
                ? assemblyNames.ToArray()
                : null;
            var filter = new Filter { testMode = testMode };
            if (normalizedTestFilter != null)
            {
                filter.testNames = new[] { normalizedTestFilter };
            }
            if (normalizedAssemblyNames != null)
            {
                filter.assemblyNames = normalizedAssemblyNames;
            }

            var completionSource = new TaskCompletionSource<JObject>();
            var pendingRecord = new TestRunRecord
            {
                Status = TestRunStatus.Running,
                Filter = BuildFilterJson(
                    testMode.ToString(),
                    normalizedTestFilter,
                    normalizedAssemblyNames),
                StartedAt = _utcNow().ToString("o", CultureInfo.InvariantCulture),
                ReturnOnlyFailures = returnOnlyFailures,
                ReturnWithLogs = returnWithLogs
            };
            _activeRun = new ActiveRun(pendingRecord, completionSource);

            try
            {
                // This is the authoritative run identity supplied by Unity Test Framework.
                string runId = _testRunnerApi.Execute(new ExecutionSettings(filter));
                if (!TryBuildArtifactPath(
                    _artifactDirectory,
                    runId,
                    out string normalizedRunId,
                    out string artifactPath,
                    out string pathError))
                {
                    throw new InvalidOperationException(
                        $"Unity TestRunnerApi.Execute returned an invalid run GUID: {pathError}");
                }

                pendingRecord.RunId = normalizedRunId;
                pendingRecord.ArtifactPath = artifactPath;
                _registry.Upsert(pendingRecord);
            }
            catch (Exception exception)
            {
                _activeRun = null;
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not start the Unity test run: {exception.Message}",
                    "test_run_start_failed");
            }

            int transportTimeoutSeconds = McpUnitySettings.Instance.RequestTimeoutSeconds;
            TimeSpan unityWaitBudget = GetUnityWaitBudget(transportTimeoutSeconds);
            JObject timeoutResponse = CreateErrorResponse(
                "test_run_still_running",
                $"Test run '{pendingRecord.RunId}' is still running after " +
                $"{unityWaitBudget.TotalSeconds:0.###} seconds. Unity returns this receipt at 75% of " +
                $"the {transportTimeoutSeconds}-second Node transport timeout so it can reach the caller. " +
                "Use get_test_run to poll it.",
                new JObject
                {
                    ["runId"] = pendingRecord.RunId,
                    ["status"] = TestRunStatus.Running,
                    ["expectedArtifactPath"] = pendingRecord.ArtifactPath,
                    ["artifactExists"] = false
                });

            return await WaitForCompletionAsync(
                completionSource.Task,
                _delay(unityWaitBudget),
                timeoutResponse);
        }

        /// <summary>
        /// Returns a persisted run by Unity GUID, or the most recent run when no GUID is supplied.
        /// </summary>
        public JObject GetTestRun(string runId = null)
        {
            string normalizedRunId = null;
            if (runId != null && !TryNormalizeRunId(runId, out normalizedRunId))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid runId '{runId}'. Expected a Unity TestRunnerApi GUID in D format.",
                    "validation_error");
            }

            TestRunRecord record = normalizedRunId == null
                ? _registry.GetMostRecent()
                : _registry.Get(normalizedRunId);

            if (record == null)
            {
                var metadata = new JObject
                {
                    ["runId"] = normalizedRunId != null
                        ? new JValue(normalizedRunId)
                        : JValue.CreateNull(),
                    ["status"] = TestRunStatus.Unknown
                };
                return CreateErrorResponse(
                    "test_run_not_found",
                    normalizedRunId == null
                        ? "No Unity test run is recorded in this Editor session."
                        : $"Unity test run '{normalizedRunId}' was not found in this Editor session.",
                    metadata);
            }

            if (record.Status == TestRunStatus.Running && ReconcileInactiveRun(record))
            {
                record = _registry.Get(record.RunId);
            }

            if (record.Status == TestRunStatus.Running)
            {
                if (IsStale(record, _utcNow(), out string staleReason))
                {
                    return ReleaseStaleRun(record, staleReason, true);
                }

                return new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Test run '{record.RunId}' is still running.",
                    ["runId"] = record.RunId,
                    ["status"] = TestRunStatus.Running,
                    ["expectedArtifactPath"] = record.ArtifactPath,
                    ["artifactExists"] = false,
                    ["filter"] = record.Filter?.DeepClone(),
                    ["startedAt"] = record.StartedAt
                };
            }

            if (record.Status == TestRunStatus.Stale ||
                record.Status == TestRunStatus.Untrusted)
            {
                return CreateErrorResponse(
                    "test_run_not_found",
                    record.Message ??
                    $"Unity test run '{record.RunId}' has no trustworthy final result.",
                    new JObject
                    {
                        ["requestedRunId"] = record.RunId,
                        ["status"] = record.Status,
                        ["lockReleased"] = true,
                        ["filter"] = record.Filter?.DeepClone(),
                        ["startedAt"] = record.StartedAt
                    });
            }

            if (record.Status == TestRunStatus.Completed)
            {
                string artifactPath;
                string validationError;
                JArray results;
                if (!TryGetOwnedArtifactPath(record, out artifactPath, out validationError) ||
                    !TryReadArtifactResults(
                        artifactPath,
                        record.ReturnOnlyFailures,
                        record.ReturnWithLogs,
                        out results,
                        out validationError))
                {
                    return CreateErrorResponse(
                        ArtifactUnavailableErrorCode,
                        $"The NUnit XML artifact for test run '{record.RunId}' is unavailable: {validationError}",
                        new JObject
                        {
                            ["runId"] = record.RunId,
                            ["status"] = TestRunStatus.Completed,
                            ["filter"] = record.Filter?.DeepClone(),
                            ["startedAt"] = record.StartedAt
                        });
                }

                if (!TryValidateArtifactConsistency(artifactPath,
                    BuildResponseFromRecord(record, results), null, out validationError))
                {
                    record.Status = TestRunStatus.Untrusted;
                    record.ArtifactPath = null;
                    record.Message = $"Test run '{record.RunId}' has inconsistent NUnit XML: {validationError}";
                    _registry.Upsert(record);
                    return CreateErrorResponse(ArtifactUnavailableErrorCode, record.Message,
                        new JObject { ["runId"] = record.RunId, ["status"] = record.Status });
                }
                JObject completedResponse = BuildResponseFromRecord(record, results);
                completedResponse["artifactPath"] = artifactPath;
                return completedResponse;
            }

            if (record.Status == TestRunStatus.FailedToSave)
            {
                return BuildResponseFromRecord(record, new JArray());
            }

            return CreateErrorResponse(
                "test_run_not_found",
                $"Unity test run '{record.RunId}' has unknown status '{record.Status}'.",
                new JObject
                {
                    ["runId"] = record.RunId,
                    ["status"] = TestRunStatus.Unknown
                });
        }

        private Task<List<ITestAdaptor>> RetrieveTestsAsync(TestMode mode)
        {
            var completionSource = new TaskCompletionSource<List<ITestAdaptor>>();
            var tests = new List<ITestAdaptor>();

            _testRunnerApi.RetrieveTestList(mode, adaptor =>
            {
                CollectTestItems(adaptor, tests);
                completionSource.SetResult(tests);
            });

            return completionSource.Task;
        }

        private static void CollectTestItems(ITestAdaptor testAdaptor, ICollection<ITestAdaptor> tests)
        {
            if (testAdaptor.IsSuite)
            {
                foreach (var child in testAdaptor.Children)
                {
                    CollectTestItems(child, tests);
                }
            }
            else
            {
                tests.Add(testAdaptor);
            }
        }

        #region ICallbacks Implementation

        public void RunStarted(ITestAdaptor testsToRun)
        {
            if (_activeRun == null)
            {
                RestoreActiveRun();
            }
            if (_activeRun == null)
            {
                return;
            }

            if (_activeRun.Record.RunStartedObserved)
            {
                InvalidateActiveRun(
                    $"A second RunStarted callback ('{testsToRun?.Name}') arrived while test run " +
                    $"'{_activeRun.Record.RunId}' was active. Unity's global ICallbacks API does not " +
                    "include a run GUID, so the result is untrusted and has been discarded. " +
                    "The active-run lock was released; retry run_tests after all Test Runner UI runs finish.");
                return;
            }

            _activeRun.Record.RunStartedObserved = true;
            if (TryNormalizeRunId(_activeRun.Record.RunId, out _))
            {
                _registry.Upsert(_activeRun.Record);
            }
            McpLogger.LogInfo(
                $"Test run started: {testsToRun?.Name} (runId={_activeRun.Record.RunId})");
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
            // RunFinished carries the complete result tree. Using it for both original and
            // restored services keeps treeNodeCount additive and structurally identical.
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            if (_activeRun == null)
            {
                RestoreActiveRun();
            }

            ActiveRun completedRun = _activeRun;
            if (completedRun == null ||
                !TryNormalizeRunId(completedRun.Record.RunId, out _))
            {
                return;
            }

            IReadOnlyList<ITestResultAdaptor> runResults = CollectResultTreeChildren(result);
            JObject filter = completedRun.Record.Filter;
            var summary = BuildResultJson(
                runResults,
                result,
                completedRun.Record.ReturnOnlyFailures,
                completedRun.Record.ReturnWithLogs,
                ParseTestMode(filter),
                filter?.Value<string>("testFilter"),
                filter?["assemblyNames"]?.Type == JTokenType.Array
                    ? filter["assemblyNames"].ToObject<string[]>()
                    : null);
            summary["runId"] = completedRun.Record.RunId;

            // 保存前凍結完整 callback leaf，避免 SaveResultToFile 期間 adaptor 改動。
            JObject fullSummary = BuildResultJson(runResults, result, false, false,
                ParseTestMode(filter), filter?.Value<string>("testFilter"), null);
            string artifactPath = completedRun.Record.ArtifactPath;
            bool artifactSaved = TryWriteArtifact(result, artifactPath, out string artifactError);
            if (artifactSaved)
            {
                if (!TryValidateArtifactConsistency(artifactPath, summary,
                    (JArray)fullSummary["results"], out string consistencyError))
                {
                    // 保留原始 XML 供診斷，但不可將矛盾結果交付為可信 artifact。
                    InvalidateActiveRun($"Test run '{completedRun.Record.RunId}' has inconsistent NUnit XML: {consistencyError}");
                    return;
                }
                _pruneArtifacts(artifactPath);
                if (!File.Exists(artifactPath))
                {
                    artifactError = "the artifact disappeared after pruning";
                    artifactSaved = false;
                }
            }

            if (artifactSaved)
            {
                summary["artifactPath"] = artifactPath;
                completedRun.Record.Status = TestRunStatus.Completed;
            }
            else
            {
                completedRun.Record.ArtifactPath = null;
                completedRun.Record.Status = TestRunStatus.FailedToSave;
                var typedArtifactError = new JObject
                {
                    ["error_code"] = ArtifactWriteErrorCode,
                    ["message"] = artifactError
                };
                summary["artifactError"] = typedArtifactError;
                summary["message"] =
                    $"{summary.Value<string>("message")} The NUnit XML artifact could not be saved: {artifactError}";
            }

            summary["status"] = completedRun.Record.Status;
            summary["startedAt"] = completedRun.Record.StartedAt;
            CaptureSummaryMetadata(completedRun.Record, summary);
            _registry.Upsert(completedRun.Record);
            _activeRun = null;
            completedRun.CompletionSource?.TrySetResult(summary);
        }

        #endregion

        internal static async Task<JObject> WaitForCompletionAsync(
            Task<JObject> completionTask,
            Task delayTask,
            JObject timeoutResponse)
        {
            Task winner = await Task.WhenAny(completionTask, delayTask);
            return winner == completionTask
                ? await completionTask
                : timeoutResponse;
        }

        internal static JObject BuildResultJson(
            IReadOnlyList<ITestResultAdaptor> results,
            ITestResultAdaptor result,
            bool returnOnlyFailures,
            bool returnWithLogs,
            TestMode testMode,
            string testFilter,
            IReadOnlyList<string> assemblyNames)
        {
            var serializedResults = results
                .Where(item => !item.HasChildren)
                .Where(item => !returnOnlyFailures || item.ResultState.StartsWith("Failed"))
                .Select(item => new JObject
                {
                    ["name"] = item.Name,
                    ["fullName"] = item.FullName,
                    ["state"] = item.ResultState,
                    ["message"] = item.Message,
                    ["duration"] = item.Duration,
                    ["logs"] = returnWithLogs ? item.Output : null,
                    ["stackTrace"] = item.StackTrace
                })
                .ToList();

            return BuildResponse(
                serializedResults,
                result.Test.Name,
                result.ResultState,
                result.Duration,
                results.Count,
                result.PassCount,
                result.FailCount,
                result.SkipCount,
                result.InconclusiveCount,
                testMode.ToString(),
                testFilter,
                assemblyNames);
        }

        internal static JObject BuildResponse(
            IReadOnlyList<JObject> serializedResults,
            string runName,
            string resultState,
            double durationSeconds,
            int treeNodeCount,
            int passCount,
            int failCount,
            int skipCount,
            int inconclusiveCount,
            string testMode,
            string testFilter,
            IReadOnlyList<string> assemblyNames)
        {
            int executed = passCount + failCount + skipCount + inconclusiveCount;
            bool noTestsMatched = executed == 0;
            string message = noTestsMatched
                ? BuildNoTestsMatchedMessage(testMode, testFilter, assemblyNames)
                : $"{runName} test run completed: {passCount}/{executed} passed - " +
                  $"{failCount}/{executed} failed - {skipCount}/{executed} skipped - " +
                  $"{inconclusiveCount}/{executed} inconclusive";

            var response = new JObject
            {
                ["success"] = !noTestsMatched,
                ["type"] = "text",
                ["message"] = message,
                ["resultState"] = resultState,
                ["durationSeconds"] = durationSeconds,
                ["testCount"] = executed,
                ["treeNodeCount"] = treeNodeCount,
                ["passCount"] = passCount,
                ["failCount"] = failCount,
                ["skipCount"] = skipCount,
                ["inconclusiveCount"] = inconclusiveCount,
                ["filter"] = BuildFilterJson(testMode, testFilter, assemblyNames),
                ["results"] = new JArray(
                    serializedResults.Select(item => item.DeepClone()))
            };

            if (noTestsMatched)
            {
                response["error_code"] = "no_tests_matched";
            }

            return response;
        }

        private static JObject BuildFilterJson(
            string testMode,
            string testFilter,
            IReadOnlyList<string> assemblyNames)
        {
            return new JObject
            {
                ["testMode"] = testMode,
                ["testFilter"] = testFilter != null
                    ? new JValue(testFilter)
                    : JValue.CreateNull(),
                ["assemblyNames"] = assemblyNames != null
                    ? (JToken)new JArray(assemblyNames)
                    : JValue.CreateNull()
            };
        }

        private static string BuildNoTestsMatchedMessage(
            string testMode,
            string testFilter,
            IReadOnlyList<string> assemblyNames)
        {
            string filterValue = string.IsNullOrEmpty(testFilter)
                ? "(none)"
                : $"\"{testFilter}\"";
            string assemblyValue = assemblyNames == null || assemblyNames.Count == 0
                ? "(none)"
                : $"[\"{string.Join("\", \"", assemblyNames)}\"]";

            return $"No tests matched. testMode={testMode}, testFilter={filterValue}, assemblyNames={assemblyValue}. " +
                   "testFilter matches full test names starting at the namespace " +
                   "(e.g. \"MyNamespace.MyFixture.MyTest\"); an assembly name is not part of a test's full name - " +
                   "use assemblyNames for that. Use the get_tests resource to list available tests.";
        }

        private static JObject CreateErrorResponse(
            string errorCode,
            string message,
            JObject metadata = null)
        {
            var response = metadata != null
                ? (JObject)metadata.DeepClone()
                : new JObject();
            response.AddFirst(new JProperty("message", message));
            response.AddFirst(new JProperty("error_code", errorCode));
            response.AddFirst(new JProperty("success", false));
            return response;
        }

        internal static TimeSpan GetUnityWaitBudget(int transportTimeoutSeconds)
        {
            double milliseconds = Math.Max(
                1,
                TimeSpan.FromSeconds(Math.Max(0, transportTimeoutSeconds)).TotalMilliseconds *
                UnityWaitBudgetRatio);
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        internal static bool TryNormalizeRunId(string runId, out string normalizedRunId)
        {
            normalizedRunId = null;
            if (!Guid.TryParseExact(runId, "D", out Guid parsedRunId))
            {
                return false;
            }

            normalizedRunId = parsedRunId.ToString("D");
            return true;
        }

        internal static bool TryBuildArtifactPath(
            string artifactDirectory,
            string runId,
            out string normalizedRunId,
            out string artifactPath,
            out string error)
        {
            artifactPath = null;
            error = null;
            if (!TryNormalizeRunId(runId, out normalizedRunId))
            {
                error = $"'{runId ?? "(null)"}' is not a GUID in D format";
                return false;
            }

            try
            {
                string directory = Path.GetFullPath(artifactDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(
                    Path.Combine(directory, $"{normalizedRunId}.xml"));
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string directoryPrefix = directory + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(directoryPrefix, comparison))
                {
                    error = "the artifact path resolves outside the configured artifact directory";
                    return false;
                }

                artifactPath = candidate;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = exception.Message;
                return false;
            }
        }

        private bool ReconcileInactiveRun(TestRunRecord record)
        {
            // 啟動前或狀態未知都不能把「暫未跑」當作取消／完成。
            bool? active = null;
            try { active = _frameworkRunActive?.Invoke(); }
            catch (Exception) { /* 第三方探針失敗時保守維持鎖。 */ }
            if (record?.Status != TestRunStatus.Running || !record.RunStartedObserved || active != false)
            {
                _inactiveObservedAt = null;
                _inactiveObservedRunId = null;
                return false;
            }
            DateTime now = _utcNow();
            if (_inactiveObservedAt == null || _inactiveObservedRunId != record.RunId || now < _inactiveObservedAt.Value)
            {
                _inactiveObservedAt = now;
                _inactiveObservedRunId = record.RunId;
                return false;
            }
            if (now - _inactiveObservedAt.Value < InactiveRunGrace)
                return false;
            if (_activeRun == null)
                RestoreActiveRun();
            if (_activeRun?.Record.RunId != record.RunId)
                return false;
            InvalidateActiveRun(
                $"Test run '{record.RunId}' has no RunFinished result, while Unity reports no active " +
                $"framework jobs across polls at least {InactiveRunGrace.TotalSeconds:0} seconds apart. " +
                "It may have been cancelled or interrupted; no trustworthy result is available. " +
                "The active-run lock was released. Verify owned resource cleanup and that all " +
                "Test Runner jobs have stopped before retrying run_tests; fixture teardown is not guaranteed.");
            _inactiveObservedAt = null;
            _inactiveObservedRunId = null;
            return true;
        }

        private bool IsStale(
            TestRunRecord record,
            DateTime nowUtc,
            out string reason)
        {
            if (!DateTime.TryParse(
                record?.StartedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime startedAt))
            {
                reason = "its persisted startedAt timestamp is missing or invalid";
                return true;
            }

            TimeSpan age = nowUtc.ToUniversalTime() - startedAt.ToUniversalTime();
            if (age >= ActiveRunTtl)
            {
                reason = $"it exceeded the {ActiveRunTtl.TotalHours:0}-hour active-run TTL";
                return true;
            }

            reason = null;
            return false;
        }

        private JObject ReleaseStaleRun(
            TestRunRecord record,
            string staleReason,
            bool fromPoll = false)
        {
            string staleRunId = record?.RunId;
            string message =
                $"Test run '{staleRunId}' was still marked running, but {staleReason}. " +
                "Its stale active-run lock has been released. After confirming Unity has no test " +
                "run in progress, retry run_tests. The stale record has no trustworthy final result.";
            if (record != null)
            {
                record.Status = TestRunStatus.Stale;
                record.ArtifactPath = null;
                record.Message = message;
                _registry.Upsert(record);
            }

            ActiveRun releasedRun = _activeRun;
            if (releasedRun != null &&
                string.Equals(
                    releasedRun.Record.RunId,
                    staleRunId,
                    StringComparison.Ordinal))
            {
                _activeRun = null;
            }

            var response = CreateErrorResponse(
                fromPoll ? "test_run_not_found" : "test_run_in_progress",
                message,
                new JObject
                {
                    [fromPoll ? "requestedRunId" : "activeRunId"] = staleRunId,
                    ["status"] = TestRunStatus.Stale,
                    ["lockReleased"] = true,
                    ["staleAfterSeconds"] = (long)ActiveRunTtl.TotalSeconds
                });
            releasedRun?.CompletionSource?.TrySetResult(response);
            return response;
        }

        private void InvalidateActiveRun(string message)
        {
            ActiveRun invalidatedRun = _activeRun;
            if (invalidatedRun == null)
            {
                return;
            }

            invalidatedRun.Record.Status = TestRunStatus.Untrusted;
            invalidatedRun.Record.ArtifactPath = null;
            invalidatedRun.Record.Message = message;
            _registry.Upsert(invalidatedRun.Record);
            _activeRun = null;

            JObject response = CreateErrorResponse(
                "test_run_not_found",
                message,
                new JObject
                {
                    ["invalidatedRunId"] = invalidatedRun.Record.RunId,
                    ["status"] = TestRunStatus.Untrusted,
                    ["lockReleased"] = true
                });
            invalidatedRun.CompletionSource?.TrySetResult(response);
            McpLogger.LogWarning(message);
        }

        private static void CaptureSummaryMetadata(TestRunRecord record, JObject summary)
        {
            record.Success = summary.Value<bool?>("success");
            record.Type = summary.Value<string>("type");
            record.Message = summary.Value<string>("message");
            record.ErrorCode = summary.Value<string>("error_code");
            record.ResultState = summary.Value<string>("resultState");
            record.DurationSeconds = summary.Value<double?>("durationSeconds");
            record.TestCount = summary.Value<int?>("testCount");
            record.TreeNodeCount = summary.Value<int?>("treeNodeCount");
            record.PassCount = summary.Value<int?>("passCount");
            record.FailCount = summary.Value<int?>("failCount");
            record.SkipCount = summary.Value<int?>("skipCount");
            record.InconclusiveCount = summary.Value<int?>("inconclusiveCount");
            record.ArtifactError = (summary["artifactError"] as JObject)?.DeepClone() as JObject;
        }

        private static JObject BuildResponseFromRecord(TestRunRecord record, JArray results)
        {
            var response = new JObject
            {
                ["success"] = record.Success ?? false,
                ["type"] = record.Type ?? "text",
                ["message"] = record.Message,
                ["resultState"] = record.ResultState,
                ["durationSeconds"] = record.DurationSeconds,
                ["testCount"] = record.TestCount,
                ["treeNodeCount"] = record.TreeNodeCount,
                ["passCount"] = record.PassCount,
                ["failCount"] = record.FailCount,
                ["skipCount"] = record.SkipCount,
                ["inconclusiveCount"] = record.InconclusiveCount,
                ["filter"] = record.Filter?.DeepClone(),
                ["results"] = results,
                ["runId"] = record.RunId,
                ["status"] = record.Status,
                ["startedAt"] = record.StartedAt
            };

            if (!string.IsNullOrEmpty(record.ErrorCode))
            {
                response["error_code"] = record.ErrorCode;
            }
            if (record.ArtifactError != null)
            {
                response["artifactError"] = record.ArtifactError.DeepClone();
            }
            return response;
        }

        private void RestoreActiveRun()
        {
            TestRunRecord persistedRun = _registry.GetActive();
            if (persistedRun != null)
            {
                _activeRun = new ActiveRun(persistedRun, null);
            }
        }

        private static TestMode ParseTestMode(JObject filter)
        {
            return Enum.TryParse(filter?.Value<string>("testMode"), true, out TestMode testMode)
                ? testMode
                : TestMode.EditMode;
        }

        private static IReadOnlyList<ITestResultAdaptor> CollectResultTreeChildren(
            ITestResultAdaptor root)
        {
            var results = new List<ITestResultAdaptor>();
            if (root?.HasChildren == true && root.Children != null)
            {
                foreach (ITestResultAdaptor child in root.Children)
                {
                    CollectResultTree(child, results);
                }
            }
            return results;
        }

        private static void CollectResultTree(
            ITestResultAdaptor result,
            ICollection<ITestResultAdaptor> collected)
        {
            if (result == null)
            {
                return;
            }

            collected.Add(result);
            if (!result.HasChildren || result.Children == null)
            {
                return;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                CollectResultTree(child, collected);
            }
        }

        private bool TryGetOwnedArtifactPath(
            TestRunRecord record,
            out string artifactPath,
            out string error)
        {
            if (!TryBuildArtifactPath(
                _artifactDirectory,
                record?.RunId,
                out _,
                out artifactPath,
                out error))
            {
                return false;
            }

            try
            {
                string persistedPath = string.IsNullOrEmpty(record.ArtifactPath)
                    ? null
                    : Path.GetFullPath(record.ArtifactPath);
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!string.Equals(persistedPath, artifactPath, comparison))
                {
                    error = "the persisted artifact path is not the owned path for this runId";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool TryReadArtifactResults(
            string artifactPath,
            bool returnOnlyFailures,
            bool returnWithLogs,
            out JArray results,
            out string error)
        {
            results = null;
            if (string.IsNullOrEmpty(artifactPath) || !File.Exists(artifactPath))
            {
                error = "the file does not exist";
                return false;
            }

            try
            {
                XDocument document = XDocument.Load(artifactPath, LoadOptions.None);
                if (document.Root == null || document.Root.Name.LocalName != "test-run")
                {
                    error = "the XML root element is not <test-run>";
                    return false;
                }

                IEnumerable<XElement> testCases = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "test-case");
                var serializedResults = new JArray();
                foreach (XElement testCase in testCases)
                {
                    string resultState = AttributeValue(testCase, "result");
                    string label = AttributeValue(testCase, "label");
                    string state = string.IsNullOrEmpty(label)
                        ? resultState
                        : $"{resultState}:{label}";
                    if (returnOnlyFailures &&
                        !resultState.StartsWith("Failed", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    XElement failure = ChildElement(testCase, "failure");
                    XElement reason = ChildElement(testCase, "reason");
                    XElement message = ChildElement(failure, "message") ??
                        ChildElement(reason, "message");
                    XElement stackTrace = ChildElement(failure, "stack-trace");
                    XElement output = ChildElement(testCase, "output");
                    string durationValue = AttributeValue(testCase, "duration");
                    double.TryParse(
                        durationValue,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double duration);

                    serializedResults.Add(new JObject
                    {
                        ["name"] = AttributeValue(testCase, "name"),
                        ["fullName"] = AttributeValue(testCase, "fullname"),
                        ["state"] = state,
                        ["message"] = message != null
                            ? new JValue(message.Value)
                            : JValue.CreateNull(),
                        ["duration"] = duration,
                        ["logs"] = returnWithLogs && output != null
                            ? new JValue(output.Value)
                            : JValue.CreateNull(),
                        ["stackTrace"] = stackTrace != null
                            ? new JValue(stackTrace.Value)
                            : JValue.CreateNull()
                    });
                }

                results = serializedResults;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"the file is not complete NUnit XML ({exception.Message})";
                return false;
            }
        }

        internal static bool TryValidateArtifactConsistency(
            string artifactPath, JObject summary, JArray expectedLeaves, out string error)
        {
            try
            {
                XDocument document = XDocument.Load(artifactPath, LoadOptions.None);
                if (document.Root?.Name.LocalName != "test-run")
                    throw new InvalidDataException("missing test-run root");
                XElement[] leaves = document.Descendants()
                    .Where(node => node.Name.LocalName == "test-case").ToArray();
                string[] states = { "Passed", "Failed", "Skipped", "Inconclusive" };
                string[] countKeys = { "passCount", "failCount", "skipCount", "inconclusiveCount" };
                string[] xmlKeys = { "passed", "failed", "skipped", "inconclusive" };
                if (leaves.Any(node => !states.Contains(AttributeValue(node, "result"))))
                    throw new InvalidDataException("unknown test-case result");
                for (int i = 0; i < states.Length; i++)
                {
                    int count = leaves.Count(node => AttributeValue(node, "result") == states[i]);
                    if (summary.Value<int?>(countKeys[i]) != count)
                        throw new InvalidDataException($"{countKeys[i]} disagrees with test-case leaves");
                    XAttribute declared = document.Root.Attribute(xmlKeys[i]);
                    if (declared != null && (!int.TryParse(declared.Value, out int value) || value != count))
                        throw new InvalidDataException($"XML {xmlKeys[i]} disagrees with test-case leaves");
                }
                if (summary.Value<int?>("testCount") != leaves.Length)
                    throw new InvalidDataException("testCount disagrees with test-case leaves");
                XAttribute total = document.Root.Attribute("total");
                if (total != null && (!int.TryParse(total.Value, out int totalValue) || totalValue != leaves.Length))
                    throw new InvalidDataException("XML total disagrees with test-case leaves");
                bool hasFailures = leaves.Any(node => AttributeValue(node, "result") == "Failed");
                if (hasFailures && (AttributeValue(document.Root, "result") == "Passed" ||
                    summary.Value<string>("resultState") == "Passed"))
                    throw new InvalidDataException("Passed summary contains Failed leaves");
                if (expectedLeaves != null)
                {
                    // 排序保留重複 leaf，不能用 dictionary 吞掉同名案例。
                    Func<string, string> category = state => states.FirstOrDefault(
                        value => state == value || state.StartsWith(value + ":", StringComparison.Ordinal) ||
                            state.StartsWith(value + "(", StringComparison.Ordinal)) ?? state;
                    string[] expected = expectedLeaves.Select(node =>
                        node.Value<string>("fullName") + "\n" + category(node.Value<string>("state") ?? ""))
                        .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                    string[] actual = leaves.Select(node => AttributeValue(node, "fullname") + "\n" +
                        AttributeValue(node, "result")).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                    if (!expected.SequenceEqual(actual))
                        throw new InvalidDataException("callback and XML test-case identities/results disagree");
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string AttributeValue(XElement element, string localName)
        {
            return element?
                .Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == localName)?
                .Value ?? string.Empty;
        }

        private static XElement ChildElement(XElement element, string localName)
        {
            return element?
                .Elements()
                .FirstOrDefault(child => child.Name.LocalName == localName);
        }

        private bool TryWriteArtifact(
            ITestResultAdaptor result,
            string artifactPath,
            out string error)
        {
            string temporaryPath = $"{artifactPath}.tmp";
            try
            {
                Directory.CreateDirectory(_artifactDirectory);
                if (!TryDeleteIfExists(temporaryPath, out error))
                {
                    error = $"could not remove a stale temporary artifact: {error}";
                    return false;
                }
                _testRunnerApi.SaveResultToFile(result, temporaryPath);

                if (!TryValidateArtifact(temporaryPath, out error))
                {
                    if (!TryDeleteIfExists(temporaryPath, out string cleanupError))
                    {
                        error = $"{error}; temporary artifact cleanup also failed: {cleanupError}";
                    }
                    return false;
                }

                if (File.Exists(artifactPath))
                {
                    File.Replace(temporaryPath, artifactPath, null);
                }
                else
                {
                    File.Move(temporaryPath, artifactPath);
                }

                if (!TryValidateArtifact(artifactPath, out error))
                {
                    if (!TryDeleteIfExists(artifactPath, out string cleanupError))
                    {
                        error = $"{error}; invalid artifact cleanup also failed: {cleanupError}";
                    }
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (!TryDeleteIfExists(temporaryPath, out string cleanupError))
                {
                    error = $"{error}; temporary artifact cleanup also failed: {cleanupError}";
                }
                return false;
            }
        }

        private static bool TryValidateArtifact(string artifactPath, out string error)
        {
            if (string.IsNullOrEmpty(artifactPath) || !File.Exists(artifactPath))
            {
                error = "the file does not exist";
                return false;
            }

            try
            {
                XDocument document = XDocument.Load(artifactPath, LoadOptions.None);
                if (document.Root == null || document.Root.Name.LocalName != "test-run")
                {
                    error = "the XML root element is not <test-run>";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = $"the file is not complete NUnit XML ({exception.Message})";
                return false;
            }
        }

        internal void PruneArtifacts(string currentArtifactPath = null)
        {
            try
            {
                var directory = new DirectoryInfo(_artifactDirectory);
                if (!directory.Exists)
                {
                    return;
                }

                foreach (FileInfo temporaryArtifact in directory
                    .GetFiles("*.xml.tmp", SearchOption.TopDirectoryOnly)
                    .Where(file => IsOwnedArtifactFile(file.Name, ".xml.tmp")))
                {
                    if (!TryDeleteIfExists(temporaryArtifact.FullName, out string cleanupError))
                    {
                        McpLogger.LogWarning(
                            $"Could not prune temporary NUnit XML artifact " +
                            $"'{temporaryArtifact.FullName}': {cleanupError}");
                    }
                }

                string currentArtifactFullPath = string.IsNullOrEmpty(currentArtifactPath)
                    ? null
                    : Path.GetFullPath(currentArtifactPath);
                StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                int historicalRetentionCount = currentArtifactFullPath == null
                    ? ArtifactRetentionCount
                    : Math.Max(0, ArtifactRetentionCount - 1);
                foreach (FileInfo artifact in directory
                    .GetFiles("*.xml", SearchOption.TopDirectoryOnly)
                    .Where(file => IsOwnedArtifactFile(file.Name, ".xml"))
                    .Where(file => !string.Equals(
                        Path.GetFullPath(file.FullName),
                        currentArtifactFullPath,
                        pathComparison))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                    .Skip(historicalRetentionCount))
                {
                    artifact.Delete();
                }
            }
            catch (Exception exception)
            {
                McpLogger.LogWarning(
                    $"Could not prune old NUnit XML test artifacts: {exception.Message}");
            }
        }

        private static bool IsOwnedArtifactFile(string fileName, string suffix)
        {
            if (string.IsNullOrEmpty(fileName) ||
                !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string runId = fileName.Substring(0, fileName.Length - suffix.Length);
            return TryNormalizeRunId(runId, out _);
        }

        private static bool TryDeleteIfExists(string path, out string error)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static string GetDefaultArtifactDirectory()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException(
                    $"Could not resolve the Unity project root from '{Application.dataPath}'.");
            }

            return Path.Combine(projectRoot.FullName, "Library", "McpUnity", "TestResults");
        }
    }
}
