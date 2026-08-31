using System;
using System.Text.RegularExpressions;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    internal static class MenuItemToolTestFixtures
    {
        internal const string Root = "McpUnity Tests/S9";
        internal const string NormalLeaf = Root + "/Normal Leaf";
        internal const string DisabledLeaf = Root + "/Disabled Leaf";
        internal const string ThrowingValidateLeaf = Root + "/Throwing Validate Leaf";
        internal const string ThrowingValidationExceptionMessage =
            "S9 fixture validate throws";
        internal const string StatefulLeaf = "McpUnity Tests/S9 Stateful/Validate Once";
        internal const string NestedContainer = Root + "/Nested/Deep";
        internal const string NestedLeaf = NestedContainer + "/Three Levels";

        internal static int NormalExecutions { get; private set; }
        internal static int DisabledExecutions { get; private set; }
        internal static int DisabledValidationCalls { get; private set; }
        internal static int ThrowingValidationCalls { get; private set; }
        internal static int StatefulExecutions { get; private set; }
        internal static int StatefulValidationCalls { get; private set; }
        internal static int NestedExecutions { get; private set; }

        private static bool _statefulValidatorUsed;

        internal static void Reset()
        {
            NormalExecutions = 0;
            DisabledExecutions = 0;
            DisabledValidationCalls = 0;
            ThrowingValidationCalls = 0;
            StatefulExecutions = 0;
            StatefulValidationCalls = 0;
            NestedExecutions = 0;
            _statefulValidatorUsed = false;
        }

        [MenuItem(NormalLeaf)]
        private static void ExecuteNormalLeaf()
        {
            NormalExecutions++;
        }

        [MenuItem(DisabledLeaf)]
        private static void ExecuteDisabledLeaf()
        {
            DisabledExecutions++;
        }

        [MenuItem(DisabledLeaf, true)]
        private static bool ValidateDisabledLeaf()
        {
            DisabledValidationCalls++;
            return false;
        }

        [MenuItem(ThrowingValidateLeaf)]
        private static void ExecuteThrowingValidateLeaf()
        {
        }

        [MenuItem(ThrowingValidateLeaf, true)]
        private static bool ValidateThrowingLeaf()
        {
            ThrowingValidationCalls++;
            throw new InvalidOperationException(ThrowingValidationExceptionMessage);
        }

        [MenuItem(StatefulLeaf)]
        private static void ExecuteStatefulLeaf()
        {
            StatefulExecutions++;
        }

        [MenuItem(StatefulLeaf, true)]
        private static bool ValidateStatefulLeaf()
        {
            StatefulValidationCalls++;
            bool enabled = !_statefulValidatorUsed;
            _statefulValidatorUsed = true;
            return enabled;
        }

        [MenuItem(NestedLeaf)]
        private static void ExecuteNestedLeaf()
        {
            NestedExecutions++;
        }
    }

    public class MenuItemToolTests
    {
        private static readonly Func<string, bool> OriginalMenuItemExists =
            GetPrivateStaticField<Func<string, bool>>("_menuItemExists");
        private static readonly Func<string, int?> OriginalGetMenuItemCount =
            GetPrivateStaticField<Func<string, int?>>("_getMenuItemCount");
        private static readonly Func<string, bool> OriginalGetEnabled =
            GetPrivateStaticField<Func<string, bool>>("_getEnabled");
        private static readonly Func<string, bool> OriginalExecuteMenuItem =
            GetPrivateStaticField<Func<string, bool>>("_executeMenuItem");
        private static readonly Func<bool> OriginalIsCompiling =
            GetPrivateStaticField<Func<bool>>("_isCompiling");

        private MenuItemTool _tool;

        [SetUp]
        public void SetUp()
        {
            RestoreProductionSeams();
            MenuItemToolTestFixtures.Reset();
            _tool = new MenuItemTool();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreProductionSeams();
            MenuItemToolTestFixtures.Reset();
        }

        [Test]
        public void Execute_MissingMenuPath_ReturnsExistingValidationErrorWithoutDispatch()
        {
            bool dispatched = false;
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ =>
                {
                    dispatched = true;
                    return true;
                });

            JObject result = _tool.Execute(new JObject());

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.IsFalse(dispatched);
        }

        [Test]
        public void Description_DisclosesSynchronousMainThreadCaptureBoundary()
        {
            Assert.That(_tool.Description, Does.Contain("synchronous main-thread"));
            Assert.That(_tool.Description, Does.Contain("delayCall"));
            Assert.That(_tool.Description, Does.Contain("background-thread"));
            Assert.That(_tool.Description, Does.Contain("post-return"));
        }

        [Test]
        public void RealMenuReflection_DistinguishesLeavesSubmenusNativeAndMissingPaths()
        {
            Assert.IsNotNull(OriginalMenuItemExists);
            Assert.IsNotNull(OriginalGetMenuItemCount);

            Assert.IsTrue(OriginalMenuItemExists(MenuItemToolTestFixtures.NormalLeaf));
            Assert.AreEqual(0, OriginalGetMenuItemCount(MenuItemToolTestFixtures.NormalLeaf).Value);
            Assert.AreEqual(0, OriginalGetMenuItemCount(MenuItemToolTestFixtures.NestedLeaf).Value);
            Assert.Greater(
                OriginalGetMenuItemCount(MenuItemToolTestFixtures.NestedContainer).Value,
                0);
            Assert.Greater(
                OriginalGetMenuItemCount(MenuItemToolTestFixtures.Root).Value,
                0);

            Assert.IsTrue(OriginalMenuItemExists("Assets/Refresh"));
            Assert.AreEqual(0, OriginalGetMenuItemCount("Assets/Refresh").Value);
            Assert.Greater(OriginalGetMenuItemCount("Assets").Value, 0);
            Assert.IsFalse(OriginalMenuItemExists(MenuItemToolTestFixtures.Root + "/NoSuchXyz"));
        }

        [Test]
        public void Execute_RealLeavesAndSubmenu_UseUnityMenuSemanticsWithoutSeams()
        {
            JObject normal = Execute(MenuItemToolTestFixtures.NormalLeaf);
            JObject nested = Execute(MenuItemToolTestFixtures.NestedLeaf);
            JObject submenu = Execute(MenuItemToolTestFixtures.NestedContainer);

            Assert.IsTrue(normal["success"]?.ToObject<bool>() ?? false, normal.ToString());
            Assert.IsTrue(nested["success"]?.ToObject<bool>() ?? false, nested.ToString());
            Assert.AreEqual(1, MenuItemToolTestFixtures.NormalExecutions);
            Assert.AreEqual(1, MenuItemToolTestFixtures.NestedExecutions);
            AssertFailure(submenu, "menu_item_is_submenu", false, 0);
        }

        [Test]
        public void Execute_RealStatefulValidator_RunsOnceOnSuccessfulDispatch()
        {
            JObject result = Execute(MenuItemToolTestFixtures.StatefulLeaf);

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(1, MenuItemToolTestFixtures.StatefulValidationCalls);
            Assert.AreEqual(1, MenuItemToolTestFixtures.StatefulExecutions);
        }

        [Test]
        public void RealGetEnabled_ValidateFalse_ReturnsFalseWithoutExecutingLeaf()
        {
            bool enabled = Menu.GetEnabled(MenuItemToolTestFixtures.DisabledLeaf);

            Assert.IsFalse(enabled);
            Assert.AreEqual(1, MenuItemToolTestFixtures.DisabledValidationCalls);
            Assert.AreEqual(0, MenuItemToolTestFixtures.DisabledExecutions);
        }

        [Test]
        public void RealGetEnabled_ThrowingValidate_ReturnsFalseAndLogsException()
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex(Regex.Escape(
                    MenuItemToolTestFixtures.ThrowingValidationExceptionMessage)));

            bool enabled = Menu.GetEnabled(MenuItemToolTestFixtures.ThrowingValidateLeaf);

            Assert.IsFalse(enabled);
            Assert.AreEqual(1, MenuItemToolTestFixtures.ThrowingValidationCalls);
        }

        [Test]
        public void Execute_RealDisabledValidator_ReturnsDisabledWithoutSeams()
        {
            JObject result = Execute(MenuItemToolTestFixtures.DisabledLeaf);

            AssertFailure(result, "menu_item_disabled", false, 0);
            Assert.AreEqual(2, MenuItemToolTestFixtures.DisabledValidationCalls);
            Assert.AreEqual(0, MenuItemToolTestFixtures.DisabledExecutions);
        }

        [Test]
        public void Execute_RealThrowingValidator_ReturnsValidateThrewWithoutSeams()
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex(Regex.Escape(
                    MenuItemToolTestFixtures.ThrowingValidationExceptionMessage)));
            LogAssert.Expect(
                LogType.Exception,
                new Regex(Regex.Escape(
                    MenuItemToolTestFixtures.ThrowingValidationExceptionMessage)));

            JObject result = Execute(MenuItemToolTestFixtures.ThrowingValidateLeaf);

            AssertFailure(result, "menu_item_validate_threw", false, 2);
            Assert.AreEqual(2, MenuItemToolTestFixtures.ThrowingValidationCalls);
        }

        [Test]
        public void Execute_WhileCompiling_ReturnsEditorBusyCompilingWithoutPreflightOrDispatch()
        {
            bool preflighted = false;
            bool dispatched = false;
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => true);
            SetPrivateStaticField<Func<string, bool>>(
                "_menuItemExists",
                _ =>
                {
                    preflighted = true;
                    return true;
                });
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ =>
                {
                    dispatched = true;
                    return true;
                });

            JObject result = Execute("McpUnity Tests/S9/Busy");

            AssertFailure(result, "editor_busy_compiling", false, 0);
            Assert.IsFalse(preflighted);
            Assert.IsFalse(dispatched);
        }

        [Test]
        public void Execute_MissingItem_ReturnsMenuItemNotFoundWithoutFurtherProbes()
        {
            int submenuProbeCount = 0;
            int enabledProbeCount = 0;
            SetPrivateStaticField<Func<string, bool>>("_menuItemExists", _ => false);
            SetPrivateStaticField<Func<string, int?>>(
                "_getMenuItemCount",
                _ =>
                {
                    submenuProbeCount++;
                    return 0;
                });
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ =>
                {
                    enabledProbeCount++;
                    return false;
                });

            JObject result = Execute("McpUnity Tests/S9/Missing");

            AssertFailure(result, "menu_item_not_found", false, 0);
            Assert.AreEqual(0, submenuProbeCount);
            Assert.AreEqual(0, enabledProbeCount);
        }

        [Test]
        public void Execute_SubmenuContainer_ReturnsMenuItemIsSubmenuWithoutValidationProbe()
        {
            int enabledProbeCount = 0;
            SetPrivateStaticField<Func<string, bool>>("_menuItemExists", _ => true);
            SetPrivateStaticField<Func<string, int?>>("_getMenuItemCount", _ => 2);
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ =>
                {
                    enabledProbeCount++;
                    return true;
                });

            JObject result = Execute("McpUnity Tests/S9/Submenu");

            AssertFailure(result, "menu_item_is_submenu", false, 0);
            Assert.AreEqual(0, enabledProbeCount);
        }

        [Test]
        public void Execute_DispatchRefusedAndValidationFalse_ReturnsDisabledAfterOneDiagnosticProbe()
        {
            int enabledProbeCount = 0;
            ConfigureLeafPreflight();
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => false);
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ =>
                {
                    enabledProbeCount++;
                    return false;
                });

            JObject result = Execute("McpUnity Tests/S9/Disabled");

            AssertFailure(result, "menu_item_disabled", false, 0);
            Assert.AreEqual(1, enabledProbeCount);
        }

        [Test]
        public void Execute_DispatchRefusedAndValidationLogsException_ReturnsValidateThrew()
        {
            ConfigureLeafPreflight();
            LogAssert.Expect(
                LogType.Exception,
                new Regex(Regex.Escape(
                    MenuItemToolTestFixtures.ThrowingValidationExceptionMessage)));
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ => EditorApplication.ExecuteMenuItem(
                    MenuItemToolTestFixtures.ThrowingValidateLeaf));
            SetPrivateStaticField<Func<string, bool>>("_getEnabled", _ => false);

            JObject result = Execute("McpUnity Tests/S9/ValidateThrows");

            AssertFailure(result, "menu_item_validate_threw", false, 1);
            Assert.That(result["message"]?.ToString(), Does.Contain("validation reported"));
            Assert.AreEqual(1, MenuItemToolTestFixtures.ThrowingValidationCalls);
        }

        [Test]
        public void Execute_GetEnabledThrows_ReturnsValidateThrewInsteadOfEscaping()
        {
            const string getEnabledError = "GetEnabled seam threw";
            ConfigureLeafPreflight();
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => false);
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ => throw new InvalidOperationException(getEnabledError));

            JObject result = Execute("McpUnity Tests/S9/GetEnabledThrows");

            AssertFailure(result, "menu_item_validate_threw", false, 0);
            Assert.That(result["message"]?.ToString(), Does.Contain(getEnabledError));
        }

        [Test]
        public void Execute_ExceptionLoggedAfterSuccessfulDispatch_ReturnsMenuItemThrew()
        {
            ConfigureLeafPreflight();
            LogAssert.Expect(
                LogType.Exception,
                new Regex(Regex.Escape(
                    MenuItemToolTestFixtures.ThrowingValidationExceptionMessage)));
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ =>
                {
                    EditorApplication.ExecuteMenuItem(
                        MenuItemToolTestFixtures.ThrowingValidateLeaf);
                    return true;
                });

            JObject result = Execute("McpUnity Tests/S9/Throws");

            AssertFailure(result, "menu_item_threw", true, 1);
            Assert.AreEqual("Exception", result["capturedLogs"]?[0]?["type"]?.ToString());
            Assert.That(result["message"]?.ToString(), Does.Contain("were logged"));
            Assert.That(result["message"]?.ToString(), Does.Not.Contain("menu item threw"));
            Assert.AreEqual(1, MenuItemToolTestFixtures.ThrowingValidationCalls);
        }

        [Test]
        public void Execute_ErrorsAfterSuccessfulDispatch_ReturnsMenuItemLoggedErrors()
        {
            const string firstError = "S9 menu error one";
            const string secondError = "S9 menu error two";
            ConfigureLeafPreflight();
            LogAssert.Expect(LogType.Error, firstError);
            LogAssert.Expect(LogType.Error, secondError);
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ =>
                {
                    Debug.LogError(firstError);
                    Debug.LogError(secondError);
                    return true;
                });

            JObject result = Execute("McpUnity Tests/S9/LogsErrors");

            AssertFailure(result, "menu_item_logged_errors", true, 2);
            Assert.AreEqual("Error", result["capturedLogs"]?[0]?["type"]?.ToString());
            Assert.AreEqual("Error", result["capturedLogs"]?[1]?["type"]?.ToString());
        }

        [Test]
        public void Execute_EnabledLeafRefusedByUnity_ReturnsMenuItemRefused()
        {
            ConfigureLeafPreflight();
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => false);
            SetPrivateStaticField<Func<string, bool>>("_getEnabled", _ => true);

            JObject result = Execute("McpUnity Tests/S9/Refused");

            AssertFailure(result, "menu_item_refused", false, 0);
            Assert.That(result["message"]?.ToString(), Does.Contain("unknown reason"));
        }

        [Test]
        public void Execute_ReflectionUnavailableAndNativeMissingError_ReturnsFallbackWithLogs()
        {
            const string missingMenuError =
                "ExecuteMenuItem failed because there is no menu named S9 missing";
            int enabledProbeCount = 0;
            SetPrivateStaticField<Func<string, bool>>("_menuItemExists", null);
            SetPrivateStaticField<Func<string, int?>>("_getMenuItemCount", null);
            LogAssert.Expect(LogType.Error, missingMenuError);
            SetPrivateStaticField<Func<string, bool>>(
                "_executeMenuItem",
                _ =>
                {
                    Debug.LogError(missingMenuError);
                    return false;
                });
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ =>
                {
                    enabledProbeCount++;
                    return false;
                });

            JObject result = Execute("McpUnity Tests/S9/UnknownCapability");

            AssertFailure(result, "menu_item_not_found_or_disabled", false, 1);
            Assert.That(result["message"]?.ToString(), Does.Contain("MenuItemExists"));
            Assert.AreEqual(1, enabledProbeCount);
        }

        [Test]
        public void Execute_GetMenuItemsUnavailable_PreservesKnownExistsInFallbackMessage()
        {
            ConfigureLeafPreflight();
            SetPrivateStaticField<Func<string, int?>>(
                "_getMenuItemCount",
                _ => throw new InvalidOperationException("GetMenuItems seam failed"));
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => false);
            SetPrivateStaticField<Func<string, bool>>("_getEnabled", _ => true);

            JObject result = Execute("McpUnity Tests/S9/KnownExists");

            AssertFailure(result, "menu_item_not_found_or_disabled", false, 0);
            Assert.That(result["message"]?.ToString(), Does.Contain("known to exist"));
            Assert.That(result["message"]?.ToString(), Does.Contain("GetMenuItems"));
            Assert.That(result["message"]?.ToString(), Does.Not.Contain("not-found, submenu"));
        }

        [Test]
        public void Execute_InternalPreflightUnavailable_DoesNotClaimMenuItemDisabled()
        {
            SetPrivateStaticField<Func<string, bool>>("_menuItemExists", null);
            SetPrivateStaticField<Func<string, int?>>("_getMenuItemCount", null);
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => false);
            SetPrivateStaticField<Func<string, bool>>("_getEnabled", _ => false);

            JObject result = Execute("McpUnity Tests/S9/UnknownButDisabled");

            AssertFailure(result, "menu_item_not_found_or_disabled", false, 0);
            Assert.That(result["message"]?.ToString(), Does.Contain("cannot be distinguished"));
        }

        [Test]
        public void Execute_Success_DoesNotProbeValidationAgain()
        {
            int enabledProbeCount = 0;
            ConfigureLeafPreflight();
            SetPrivateStaticField<Func<string, bool>>("_executeMenuItem", _ => true);
            SetPrivateStaticField<Func<string, bool>>(
                "_getEnabled",
                _ =>
                {
                    enabledProbeCount++;
                    return true;
                });

            JObject result = Execute("McpUnity Tests/S9/Success");

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.IsTrue(result["dispatched"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(0, ((JArray)result["capturedLogs"]).Count);
            Assert.AreEqual(0, enabledProbeCount);
            Assert.IsNull(result["error_code"]);
        }

        private JObject Execute(string menuPath)
        {
            return _tool.Execute(new JObject { ["menuPath"] = menuPath });
        }

        private static void ConfigureLeafPreflight()
        {
            SetPrivateStaticField<Func<bool>>("_isCompiling", () => false);
            SetPrivateStaticField<Func<string, bool>>("_menuItemExists", _ => true);
            SetPrivateStaticField<Func<string, int?>>("_getMenuItemCount", _ => 0);
            SetPrivateStaticField<Func<string, bool>>("_getEnabled", _ => true);
        }

        private static void AssertFailure(
            JObject result,
            string errorCode,
            bool dispatched,
            int capturedLogCount)
        {
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, result.ToString());
            Assert.AreEqual(errorCode, result["error_code"]?.ToString(), result.ToString());
            Assert.AreEqual(dispatched, result["dispatched"]?.ToObject<bool>());
            Assert.AreEqual(capturedLogCount, ((JArray)result["capturedLogs"]).Count);
            Assert.IsNull(result["error"], "Operational failures must stay in the normal response.");
        }

        private static void RestoreProductionSeams()
        {
            SetPrivateStaticField("_menuItemExists", OriginalMenuItemExists);
            SetPrivateStaticField("_getMenuItemCount", OriginalGetMenuItemCount);
            SetPrivateStaticField("_getEnabled", OriginalGetEnabled);
            SetPrivateStaticField("_executeMenuItem", OriginalExecuteMenuItem);
            SetPrivateStaticField("_isCompiling", OriginalIsCompiling);
        }

        private static T GetPrivateStaticField<T>(string name)
        {
            System.Reflection.FieldInfo field = typeof(MenuItemTool).GetField(
                name,
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException(typeof(MenuItemTool).FullName, name);
            return (T)field.GetValue(null);
        }

        private static void SetPrivateStaticField<T>(string name, T value)
        {
            System.Reflection.FieldInfo field = typeof(MenuItemTool).GetField(
                name,
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (field == null)
                Assert.Fail($"MenuItemTool private field '{name}' was not found");
            field.SetValue(null, value);
        }
    }
}
