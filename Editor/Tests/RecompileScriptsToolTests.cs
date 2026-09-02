using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    public class RecompileScriptsToolTests
    {
        private static readonly Action OriginalRefreshAssets =
            GetPrivateStaticField<Action>("_refreshAssets");
        private static readonly Action OriginalRequestScriptCompilation =
            GetPrivateStaticField<Action>("_requestScriptCompilation");
        private static readonly Func<bool> OriginalIsCompiling =
            GetPrivateStaticField<Func<bool>>("_isCompiling");

        private RecompileScriptsTool _tool;

        [SetUp]
        public void SetUp()
        {
            RestoreProductionSeams();
            _tool = new RecompileScriptsTool();
        }

        [TearDown]
        public void TearDown()
        {
            if (_tool != null)
            {
                InvokePrivateInstanceMethod(_tool, "StopCompilationTracking", null);
                GetPrivateInstanceField<IList>(_tool, "_pendingRequests").Clear();
            }

            RestoreProductionSeams();
        }

        [Test]
        public void BuildResponse_ReturnsLimitedLogsAndTruncationMetadata()
        {
            var logs = new List<CompilerMessage>
            {
                CreateMessage("First error", CompilerMessageType.Error),
                CreateMessage("First warning", CompilerMessageType.Warning),
                CreateMessage("Extra info", CompilerMessageType.Info)
            };

            var response = RecompileScriptsTool.BuildResponse(logs, 1, 1, true, 2);

            Assert.IsTrue(response["success"]?.ToObject<bool>() ?? false);
            Assert.AreEqual("text", response["type"]?.ToString());
            Assert.AreEqual(
                "Recompilation completed with 1 error(s) and 1 warning(s) (returnWithLogs: True, logsLimit: 2)",
                response["message"]?.ToString());
            Assert.AreEqual(2, ((JArray)response["logs"]).Count);
            Assert.AreEqual(3, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(2, response["returnedLogs"]?.ToObject<int>());
            Assert.IsTrue(response["truncated"]?.ToObject<bool>() ?? false);
        }

        [Test]
        public void BuildResponse_ReportsTruncatedWhenExistingLogsAreNotRequested()
        {
            var logs = new List<CompilerMessage>
            {
                CreateMessage("Warning", CompilerMessageType.Warning)
            };

            var response = RecompileScriptsTool.BuildResponse(logs, 1, 0, false, 100);

            Assert.AreEqual(0, ((JArray)response["logs"]).Count);
            Assert.AreEqual(1, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(0, response["returnedLogs"]?.ToObject<int>());
            Assert.IsTrue(response["truncated"]?.ToObject<bool>() ?? false);
        }

        [Test]
        public void BuildResponse_DoesNotReportTruncatedWhenNoLogsExist()
        {
            var response = RecompileScriptsTool.BuildResponse(
                new List<CompilerMessage>(), 0, 0, false, 100);

            Assert.AreEqual(0, response["totalLogs"]?.ToObject<int>());
            Assert.AreEqual(0, response["returnedLogs"]?.ToObject<int>());
            Assert.IsFalse(response["truncated"]?.ToObject<bool>() ?? true);
        }

        [Test]
        public void BuildResponse_IncludesRefreshMetadata()
        {
            var response = RecompileScriptsTool.BuildResponse(
                new List<CompilerMessage>(), 0, 0, false, 100, true, 346);

            Assert.IsTrue(response["refreshed"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(346, response["refreshDurationMs"]?.ToObject<int>());
        }

        [Test]
        public void ExecuteAsync_RefreshesAfterTrackingAndBeforeRequestingCompilation()
        {
            var calls = new List<string>();
            bool trackingStartedBeforeRefresh = false;
            int isCompilingCallCount = 0;
            SetPrivateInstanceField(_tool, "_processedAssemblies", 7);
            GetPrivateInstanceField<List<CompilerMessage>>(_tool, "_compilationLogs")
                .Add(CreateMessage("stale", CompilerMessageType.Warning));
            SetPrivateStaticField<Action>(
                "_refreshAssets",
                () =>
                {
                    trackingStartedBeforeRefresh =
                        GetPrivateInstanceField<int>(_tool, "_processedAssemblies") == 0
                        && GetPrivateInstanceField<List<CompilerMessage>>(
                            _tool,
                            "_compilationLogs").Count == 0;
                    calls.Add("refresh");
                });
            SetPrivateStaticField<Func<bool>>(
                "_isCompiling",
                () =>
                {
                    calls.Add(isCompilingCallCount++ == 0
                        ? "isCompilingBeforeRefresh"
                        : "isCompilingAfterRefresh");
                    return false;
                });
            SetPrivateStaticField<Action>(
                "_requestScriptCompilation",
                () => calls.Add("requestCompilation"));

            var completionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = true },
                completionSource);

            Assert.IsTrue(trackingStartedBeforeRefresh);
            CollectionAssert.AreEqual(
                new[]
                {
                    "isCompilingBeforeRefresh",
                    "refresh",
                    "isCompilingAfterRefresh",
                    "requestCompilation"
                },
                calls);
            Assert.IsFalse(completionSource.Task.IsCompleted);

            CompleteCompilation(_tool);
            Assert.IsTrue(completionSource.Task.IsCompleted);
        }

        [Test]
        public void ExecuteAsync_MissingRefreshAssets_DefaultsToRefresh()
        {
            int refreshCount = 0;
            SetPrivateStaticField<Action>("_refreshAssets", () => refreshCount++);
            SetPrivateStaticField<Action>("_requestScriptCompilation", () => { });
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => false);

            var completionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(new JObject(), completionSource);

            Assert.AreEqual(1, refreshCount);
            CompleteCompilation(_tool);
        }

        [Test]
        public void ExecuteAsync_RefreshAssetsFalse_DoesNotRefresh()
        {
            int refreshCount = 0;
            int requestCompilationCount = 0;
            SetPrivateStaticField<Action>("_refreshAssets", () => refreshCount++);
            SetPrivateStaticField<Action>(
                "_requestScriptCompilation",
                () => requestCompilationCount++);
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => false);

            var completionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = false },
                completionSource);

            Assert.AreEqual(0, refreshCount);
            Assert.AreEqual(1, requestCompilationCount);
            CompleteCompilation(_tool);
        }

        [Test]
        public void ExecuteAsync_PiggybackSkipsRefreshAndDerivesHonestResponse()
        {
            int refreshCount = 0;
            SetPrivateStaticField<Action>("_refreshAssets", () => refreshCount++);
            SetPrivateStaticField<Action>("_requestScriptCompilation", () => { });
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => false);

            var firstCompletionSource = new TaskCompletionSource<JObject>();
            var piggybackCompletionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = false },
                firstCompletionSource);
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = true },
                piggybackCompletionSource);

            Assert.AreEqual(0, refreshCount);
            Assert.IsFalse(piggybackCompletionSource.Task.IsCompleted);

            CompleteCompilation(_tool);
            JObject response = piggybackCompletionSource.Task.Result;

            Assert.IsFalse(response["refreshed"]?.ToObject<bool>() ?? true);
            Assert.AreEqual(0, response["refreshDurationMs"]?.ToObject<int>());
            StringAssert.StartsWith(
                "Observed completion of a compilation already tracked by another recompile_scripts request",
                response["message"]?.ToString());
            StringAssert.DoesNotContain(
                "Successfully recompiled all scripts",
                response["message"]?.ToString());
            StringAssert.Contains(
                "AssetDatabase.Refresh was not run for this request because another compilation was already in progress.",
                response["message"]?.ToString());
        }

        [Test]
        public void ExecuteAsync_PiggybackWithRefreshDisabled_NeverClaimsSuccessfulRecompile()
        {
            int refreshCount = 0;
            int requestCompilationCount = 0;
            SetPrivateStaticField<Action>("_refreshAssets", () => refreshCount++);
            SetPrivateStaticField<Action>(
                "_requestScriptCompilation",
                () => requestCompilationCount++);
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => true);

            var leaderCompletionSource = new TaskCompletionSource<JObject>();
            var piggybackCompletionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = true },
                leaderCompletionSource);
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = false },
                piggybackCompletionSource);

            Assert.AreEqual(1, refreshCount);
            Assert.AreEqual(0, requestCompilationCount);

            CompleteCompilation(_tool);
            JObject response = piggybackCompletionSource.Task.Result;
            string message = response["message"]?.ToString();

            Assert.IsFalse(response["refreshed"]?.ToObject<bool>() ?? true);
            Assert.AreEqual(0, response["refreshDurationMs"]?.ToObject<int>());
            Assert.AreEqual(
                JTokenType.Null,
                response["compilationWasAlreadyInProgress"]?.Type);
            StringAssert.StartsWith(
                "Observed completion of a compilation already tracked by another recompile_scripts request",
                message);
            StringAssert.DoesNotContain("Successfully recompiled all scripts", message);
            StringAssert.Contains(
                "piggybacked on another compilation and did not run AssetDatabase.Refresh",
                message);
            StringAssert.Contains(
                "does not confirm that this request's file changes were included",
                message);
        }

        [Test]
        public void ExecuteAsync_AlreadyCompilingBeforeRefresh_DisclosesUnconfirmedCompilation()
        {
            int refreshCount = 0;
            int requestCompilationCount = 0;
            SetPrivateStaticField<Action>("_refreshAssets", () => refreshCount++);
            SetPrivateStaticField<Action>(
                "_requestScriptCompilation",
                () => requestCompilationCount++);
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => true);

            var completionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = true },
                completionSource);

            Assert.AreEqual(1, refreshCount);
            Assert.AreEqual(0, requestCompilationCount);

            CompleteCompilation(_tool);
            JObject response = completionSource.Task.Result;

            Assert.IsTrue(
                response["compilationWasAlreadyInProgress"]?.ToObject<bool>() ?? false);
            StringAssert.Contains(
                "Observed completion of a pre-existing compilation",
                response["message"]?.ToString());
            StringAssert.Contains(
                "does not confirm that changes discovered by the refresh were compiled",
                response["message"]?.ToString());
        }

        [Test]
        public void ExecuteAsync_RefreshFailure_FailsLoudlyWithRefreshMetadata()
        {
            int requestCompilationCount = 0;
            SetPrivateStaticField<Action>(
                "_refreshAssets",
                () => throw new InvalidOperationException("refresh exploded"));
            SetPrivateStaticField<Action>(
                "_requestScriptCompilation",
                () => requestCompilationCount++);
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => false);
            LogAssert.Expect(
                LogType.Error,
                new Regex("AssetDatabase\\.Refresh failed before script recompilation: refresh exploded"));

            var completionSource = new TaskCompletionSource<JObject>();
            _tool.ExecuteAsync(
                new JObject { ["refreshAssets"] = true },
                completionSource);

            Assert.IsTrue(completionSource.Task.IsCompleted);
            JObject response = completionSource.Task.Result;

            Assert.AreEqual(0, requestCompilationCount);
            Assert.IsFalse(response["success"]?.ToObject<bool>() ?? true);
            Assert.AreEqual("asset_database_refresh_failed", response["error_code"]?.ToString());
            StringAssert.Contains("refresh exploded", response["message"]?.ToString());
            Assert.IsTrue(response["refreshed"]?.ToObject<bool>() ?? false);
            Assert.GreaterOrEqual(response["refreshDurationMs"]?.ToObject<int>(), 0);
        }

        private static void CompleteCompilation(RecompileScriptsTool tool)
        {
            InvokePrivateInstanceMethod(tool, "OnCompilationFinished", new object[] { null });
        }

        private static void RestoreProductionSeams()
        {
            SetPrivateStaticField("_refreshAssets", OriginalRefreshAssets);
            SetPrivateStaticField(
                "_requestScriptCompilation",
                OriginalRequestScriptCompilation);
            SetPrivateStaticField("_isCompiling", OriginalIsCompiling);
        }

        private static T GetPrivateStaticField<T>(string name)
        {
            FieldInfo field = typeof(RecompileScriptsTool).GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException(typeof(RecompileScriptsTool).FullName, name);
            return (T)field.GetValue(null);
        }

        private static void SetPrivateStaticField<T>(string name, T value)
        {
            FieldInfo field = typeof(RecompileScriptsTool).GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                Assert.Fail($"RecompileScriptsTool private field '{name}' was not found");
            field.SetValue(null, value);
        }

        private static T GetPrivateInstanceField<T>(RecompileScriptsTool tool, string name)
        {
            FieldInfo field = typeof(RecompileScriptsTool).GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(typeof(RecompileScriptsTool).FullName, name);
            return (T)field.GetValue(tool);
        }

        private static void SetPrivateInstanceField<T>(
            RecompileScriptsTool tool,
            string name,
            T value)
        {
            FieldInfo field = typeof(RecompileScriptsTool).GetField(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                Assert.Fail($"RecompileScriptsTool private field '{name}' was not found");
            field.SetValue(tool, value);
        }

        private static void InvokePrivateInstanceMethod(
            RecompileScriptsTool tool,
            string name,
            object[] arguments)
        {
            MethodInfo method = typeof(RecompileScriptsTool).GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                Assert.Fail($"RecompileScriptsTool private method '{name}' was not found");
            method.Invoke(tool, arguments);
        }

        private static CompilerMessage CreateMessage(string message, CompilerMessageType type)
        {
            return new CompilerMessage
            {
                message = message,
                type = type
            };
        }
    }
}
