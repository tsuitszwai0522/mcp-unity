using System;
using System.Reflection;
using McpUnity.Unity;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for executing Unity Editor menu items.
    /// </summary>
    public class MenuItemTool : McpToolBase
    {
        private static readonly Func<string, bool> ResolvedMenuItemExists =
            ResolveMenuItemExists();
        private static readonly Func<string, int?> ResolvedGetMenuItemCount =
            ResolveGetMenuItemCount();

        // Test seams retain the one-time cached production delegates above.
        private static Func<string, bool> _menuItemExists = ResolvedMenuItemExists;
        private static Func<string, int?> _getMenuItemCount = ResolvedGetMenuItemCount;
        private static Func<string, bool> _getEnabled = Menu.GetEnabled;
        private static Func<string, bool> _executeMenuItem = EditorApplication.ExecuteMenuItem;
        private static Func<bool> _isCompiling = () => EditorApplication.isCompiling;

        public MenuItemTool()
        {
            Name = "execute_menu_item";
            Description = "Executes functions tagged with the MenuItem attribute. Error capture " +
                "covers only the synchronous main-thread call window; delayCall, background-thread, " +
                "and post-return errors are not captured.";
        }

        /// <summary>
        /// Execute the MenuItem tool with the provided parameters synchronously.
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject.</param>
        public override JObject Execute(JObject parameters)
        {
            string menuPath = parameters["menuPath"]?.ToObject<string>();
            if (string.IsNullOrEmpty(menuPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'menuPath' not provided",
                    "validation_error"
                );
            }

            if (_isCompiling())
            {
                return CreateResult(
                    false,
                    false,
                    "editor_busy_compiling",
                    $"Unity Editor is compiling; menu item '{menuPath}' was not dispatched.",
                    new JArray());
            }

            var capturedLogs = new JArray();
            int exceptionCount = 0;
            Application.LogCallback captureLog = (message, stackTrace, logType) =>
            {
                if (logType != LogType.Exception
                    && logType != LogType.Error
                    && logType != LogType.Assert)
                {
                    return;
                }

                if (logType == LogType.Exception)
                    exceptionCount++;

                capturedLogs.Add(new JObject
                {
                    ["type"] = logType.ToString(),
                    ["message"] = message
                });
            };

            // Use the non-threaded callback to exclude unrelated background-thread logs. The
            // synchronous main-thread window can still include errors from other editor systems
            // pumped by the menu command (for example, asset import during Assets/Refresh), so
            // responses claim only that errors were logged during the window, not who caused them.
            Application.logMessageReceived += captureLog;
            try
            {
                ProbePreflight(
                    menuPath,
                    out bool existsKnown,
                    out bool exists,
                    out bool submenuKnown,
                    out bool isSubmenu,
                    out string reflectionFailure);

                if (existsKnown && !exists)
                {
                    return CreateResult(
                        false,
                        false,
                        "menu_item_not_found",
                        $"Menu item '{menuPath}' was not found.",
                        capturedLogs);
                }

                if (submenuKnown && isSubmenu)
                {
                    return CreateResult(
                        false,
                        false,
                        "menu_item_is_submenu",
                        $"Menu path '{menuPath}' is a submenu, not an executable menu item.",
                        capturedLogs);
                }

                McpLogger.LogInfo($"[MCP Unity] Executing menu item: {menuPath}");
                bool dispatched = _executeMenuItem(menuPath);

                // A false dispatch means the command body did not run. Diagnose that first so
                // Unity's own "no menu named ..." Error cannot be misreported as an execution error.
                if (!dispatched)
                {
                    bool? enabled = null;
                    Exception getEnabledException = null;
                    try
                    {
                        // GetEnabled invokes user validation, so call it only after dispatch fails.
                        enabled = _getEnabled(menuPath);
                    }
                    catch (Exception ex)
                    {
                        getEnabledException = ex;
                    }

                    bool preflightUnavailable = !existsKnown
                        || (exists && !submenuKnown);

                    // When existence cannot be established, Menu.GetEnabled(false) is ambiguous:
                    // Unity returns false for both disabled and missing paths. Never claim that the
                    // item is specifically disabled in this degraded-reflection mode, regardless of
                    // whether Unity logged during the diagnostic probe.
                    if (!existsKnown)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_not_found_or_disabled",
                            CreateFallbackMessage(
                                menuPath,
                                existsKnown,
                                exists,
                                submenuKnown,
                                reflectionFailure),
                            capturedLogs);
                    }

                    if (getEnabledException != null)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_validate_threw",
                            $"Menu item '{menuPath}' was not dispatched, and Unity's validation " +
                            $"check threw {getEnabledException.GetType().Name}: " +
                            getEnabledException.Message,
                            capturedLogs);
                    }

                    if (enabled == false && exceptionCount > 0)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_validate_threw",
                            $"Menu item '{menuPath}' was not dispatched, and validation reported " +
                            $"disabled while {exceptionCount} exception(s) were logged during the " +
                            "synchronous call window.",
                            capturedLogs);
                    }

                    if (preflightUnavailable && capturedLogs.Count > 0)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_not_found_or_disabled",
                            CreateFallbackMessage(
                                menuPath,
                                existsKnown,
                                exists,
                                submenuKnown,
                                reflectionFailure),
                            capturedLogs);
                    }

                    if (enabled == false)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_disabled",
                            $"Menu item '{menuPath}' is disabled.",
                            capturedLogs);
                    }

                    if (preflightUnavailable)
                    {
                        return CreateResult(
                            false,
                            false,
                            "menu_item_not_found_or_disabled",
                            CreateFallbackMessage(
                                menuPath,
                                existsKnown,
                                exists,
                                submenuKnown,
                                reflectionFailure),
                            capturedLogs);
                    }

                    return CreateResult(
                        false,
                        false,
                        "menu_item_refused",
                        $"Unity refused to execute menu item '{menuPath}' for an unknown reason.",
                        capturedLogs);
                }

                if (capturedLogs.Count > 0)
                {
                    string errorCode = exceptionCount > 0
                        ? "menu_item_threw"
                        : "menu_item_logged_errors";
                    return CreateResult(
                        false,
                        true,
                        errorCode,
                        $"Menu item '{menuPath}' dispatched, but {capturedLogs.Count} error(s) " +
                        "were logged during its synchronous call window.",
                        capturedLogs);
                }

                return CreateResult(
                    true,
                    true,
                    null,
                    $"Successfully executed menu item: {menuPath}",
                    capturedLogs);
            }
            finally
            {
                Application.logMessageReceived -= captureLog;
            }
        }

        private static void ProbePreflight(
            string menuPath,
            out bool existsKnown,
            out bool exists,
            out bool submenuKnown,
            out bool isSubmenu,
            out string reflectionFailure)
        {
            existsKnown = false;
            exists = false;
            submenuKnown = false;
            isSubmenu = false;
            reflectionFailure = null;

            if (_menuItemExists == null)
            {
                AppendReflectionFailure(
                    ref reflectionFailure,
                    "UnityEditor.Menu.MenuItemExists could not be resolved via reflection");
            }
            else
            {
                try
                {
                    exists = _menuItemExists(menuPath);
                    existsKnown = true;
                }
                catch (Exception ex)
                {
                    AppendReflectionFailure(
                        ref reflectionFailure,
                        $"UnityEditor.Menu.MenuItemExists could not be invoked via reflection " +
                        $"({ex.GetType().Name})");
                }
            }

            if (existsKnown && !exists)
                return;

            if (_getMenuItemCount == null)
            {
                AppendReflectionFailure(
                    ref reflectionFailure,
                    "UnityEditor.Menu.GetMenuItems could not be resolved via reflection");
                return;
            }

            try
            {
                int? itemCount = _getMenuItemCount(menuPath);
                if (!itemCount.HasValue)
                {
                    AppendReflectionFailure(
                        ref reflectionFailure,
                        "UnityEditor.Menu.GetMenuItems returned an unreadable result via reflection");
                    return;
                }

                submenuKnown = true;
                isSubmenu = itemCount.Value > 0;
            }
            catch (Exception ex)
            {
                AppendReflectionFailure(
                    ref reflectionFailure,
                    $"UnityEditor.Menu.GetMenuItems could not be invoked via reflection " +
                    $"({ex.GetType().Name})");
            }
        }

        private static void AppendReflectionFailure(ref string failures, string failure)
        {
            failures = string.IsNullOrEmpty(failures)
                ? failure
                : failures + "; " + failure;
        }

        private static string CreateFallbackMessage(
            string menuPath,
            bool existsKnown,
            bool exists,
            bool submenuKnown,
            string reflectionFailure)
        {
            string reason = string.IsNullOrEmpty(reflectionFailure)
                ? "Unity's internal menu preflight was unavailable"
                : reflectionFailure;

            if (existsKnown && exists)
            {
                string unknownState = submenuKnown
                    ? "the remaining menu state"
                    : "submenu status";
                return $"Unity refused to execute menu item '{menuPath}'. The path is known to " +
                    $"exist, but {unknownState} could not be determined because {reason}.";
            }

            return $"Unity refused to execute menu item '{menuPath}', and not-found, submenu, " +
                $"and disabled states cannot be distinguished because {reason}.";
        }

        private static Func<string, bool> ResolveMenuItemExists()
        {
            MethodInfo method = typeof(Menu).GetMethod(
                "MenuItemExists",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (method == null)
                return null;

            return menuPath => (bool)method.Invoke(null, new object[] { menuPath });
        }

        private static Func<string, int?> ResolveGetMenuItemCount()
        {
            MethodInfo method = typeof(Menu).GetMethod(
                "GetMenuItems",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(bool), typeof(bool) },
                null);
            if (method == null)
                return null;

            return menuPath =>
            {
                object result = method.Invoke(
                    null,
                    new object[] { menuPath, false, false });
                return result is Array items ? items.Length : (int?)null;
            };
        }

        private static JObject CreateResult(
            bool success,
            bool dispatched,
            string errorCode,
            string message,
            JArray capturedLogs)
        {
            var result = new JObject
            {
                ["success"] = success,
                ["type"] = "text",
                ["message"] = message,
                ["dispatched"] = dispatched,
                ["capturedLogs"] = capturedLogs
            };

            if (!string.IsNullOrEmpty(errorCode))
                result["error_code"] = errorCode;

            return result;
        }
    }
}
