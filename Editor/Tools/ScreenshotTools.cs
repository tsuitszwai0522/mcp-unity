using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;
using McpUnity.Services;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for capturing a screenshot from the Game View
    /// </summary>
    public class ScreenshotGameViewTool : McpToolBase
    {
        private static Func<Type> _resolveGameViewType =
            () => typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        private static Func<Type, System.Reflection.MethodInfo> _resolveRenderViewMethod =
            type => type.BaseType?.GetMethod(
                "RenderView",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
        private static Func<System.Reflection.MethodInfo, EditorWindow, RenderTexture>
            _invokeRenderView = (method, window) =>
                method.Invoke(window, new object[] { Vector2.zero, false }) as RenderTexture;
        private static Func<Texture2D> _captureScreenshotAsTexture =
            ScreenCapture.CaptureScreenshotAsTexture;
        private static Func<Camera> _findMainCamera = () => Camera.main;
        private static Func<Tuple<JObject, GameObject>> _resolvePrefabRoot = () =>
        {
            JObject error = PrefabSessionScope.TryGetPrefabRoot(out GameObject root);
            return Tuple.Create(error, root);
        };
        private static Func<Type, bool> _hasExistingEditorWindow =
            type => UnityEngine.Resources.FindObjectsOfTypeAll(type).Length > 0;
        private static Func<Type, bool, EditorWindow> _getGameViewWindow =
            (type, focus) => EditorWindow.GetWindow(type, false, null, focus);

        private sealed class CaptureDiagnosticsState
        {
            public string DegradedReason;
            public bool GameViewWindowCreated;
        }

        public ScreenshotGameViewTool()
        {
            Name = "screenshot_game_view";
            Description = "Captures a screenshot from the Game View, reflecting what the player sees. " +
                          "Set force_focus=true to force-focus the Game View tab before capturing. " +
                          "While Prefab contents are open, failed Game View capture never falls " +
                          "back to a loaded scene Main Camera.";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;
                bool forceFocus = parameters?["force_focus"]?.ToObject<bool?>() ?? false;
                JObject dimensionError = ScreenshotHelper.ValidateDimensions(width, height);
                if (dimensionError != null)
                {
                    tcs.TrySetResult(dimensionError);
                    return;
                }

                bool gameViewWindowCreated = false;
                var gameViewType = _resolveGameViewType();
                if (gameViewType != null)
                {
                    bool hadExistingWindow = _hasExistingEditorWindow(gameViewType);
                    // focus flag on GetWindow controls whether the window is brought to front + focused
                    var gameView = _getGameViewWindow(gameViewType, forceFocus);
                    gameViewWindowCreated = !hadExistingWindow && gameView != null;
                    if (forceFocus && gameView != null)
                    {
                        gameView.Focus();
                        gameView.Repaint();
                    }
                }

                if (forceFocus)
                {
                    // EditorApplication.delayCall can be starved in headless / automated (MCP) contexts
                    // → the capture lambda never runs → tcs never completes → the MCP request times out.
                    // EditorApplication.update ticks every editor frame, so use it with a small frame
                    // counter: this both lets the Game View repaint after Focus() before ScreenCapture
                    // samples it, and guarantees the result is produced.
                    int framesToWait = 2;
                    EditorApplication.CallbackFunction handler = null;
                    handler = () =>
                    {
                        if (--framesToWait > 0) return;
                        EditorApplication.update -= handler;
                        try
                        {
                            tcs.TrySetResult(CaptureGameView(
                                width, height, gameViewWindowCreated));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                                $"Error capturing Game View screenshot: {ex.Message}",
                                "tool_execution_error"
                            ));
                        }
                    };
                    EditorApplication.update += handler;
                }
                else
                {
                    tcs.TrySetResult(CaptureGameView(width, height, gameViewWindowCreated));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing Game View screenshot: {ex.Message}",
                    "tool_execution_error"
                ));
            }
        }

        private static JObject CaptureGameView(
            int width,
            int height,
            bool gameViewWindowCreated)
        {
            var diagnostics = new CaptureDiagnosticsState
            {
                GameViewWindowCreated = gameViewWindowCreated
            };

            try
            {
                return CaptureGameViewCore(width, height, diagnostics);
            }
            catch (Exception ex)
            {
                return AddFailureDiagnostics(
                    McpUnitySocketHandler.CreateErrorResponse(
                        $"Error capturing Game View screenshot: {ex.Message}",
                        "tool_execution_error"),
                    diagnostics.DegradedReason,
                    diagnostics.GameViewWindowCreated);
            }
        }

        private static JObject CaptureGameViewCore(
            int width,
            int height,
            CaptureDiagnosticsState diagnostics)
        {
            // Primary: capture the real composited Game View via the editor's own render path
            // (PlayModeView.RenderView). This is focus-independent (no need to bring the Game View tab to
            // front) and DOES include screen-space-camera overlay UI — unlike camera.Render() / a Standard
            // render request, which skip the URP overlay stack, and unlike ScreenCapture which samples
            // whichever editor view currently has focus (often the Scene View).
            Tuple<JObject, string, bool> renderViewAttempt =
                TryCaptureViaRenderView(width, height);
            diagnostics.GameViewWindowCreated |= renderViewAttempt.Item3;
            if (renderViewAttempt.Item1 != null)
            {
                return ScreenshotHelper.AddCaptureMetadata(
                    renderViewAttempt.Item1,
                    "render_view",
                    false,
                    null,
                    diagnostics.GameViewWindowCreated);
            }

            diagnostics.DegradedReason = renderViewAttempt.Item2;

            // Fallback: ScreenCapture (works best during Play Mode; samples the focused view's backbuffer)
            var screenshot = _captureScreenshotAsTexture();
            if (screenshot != null)
            {
                try
                {
                    var resized = ScreenshotHelper.ResizeTexture(screenshot, width, height);
                    byte[] pngBytes = resized.EncodeToPNG();
                    string base64 = Convert.ToBase64String(pngBytes);

                    if (resized != screenshot)
                        UnityEngine.Object.DestroyImmediate(resized);

                    McpLogger.LogInfo($"Game View screenshot captured ({width}x{height})");

                    return ScreenshotHelper.AddCaptureMetadata(new JObject
                    {
                        ["success"] = true,
                        ["type"] = "image",
                        ["mimeType"] = "image/png",
                        ["data"] = base64,
                        ["message"] = $"Game View screenshot captured ({width}x{height})"
                    },
                    "screen_capture",
                    true,
                    diagnostics.DegradedReason,
                    diagnostics.GameViewWindowCreated);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(screenshot);
                }
            }

            diagnostics.DegradedReason = string.IsNullOrEmpty(diagnostics.DegradedReason)
                ? "screen_capture_returned_null"
                : diagnostics.DegradedReason + ";screen_capture_returned_null";

            Tuple<JObject, GameObject> prefabScope = _resolvePrefabRoot();
            JObject scopeError = prefabScope.Item1;
            GameObject prefabRoot = prefabScope.Item2;
            if (scopeError != null)
            {
                return AddFailureDiagnostics(
                    scopeError,
                    diagnostics.DegradedReason,
                    diagnostics.GameViewWindowCreated);
            }
            if (prefabRoot != null)
            {
                return AddFailureDiagnostics(
                    McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to capture the Game View while Prefab contents " +
                        $"'{PrefabEditingService.AssetPath}' (root '{prefabRoot.name}') are open. " +
                        "screenshot_game_view does not fall back to a loaded scene Main Camera " +
                        "during a Prefab editing session.",
                        "tool_execution_error"),
                    diagnostics.DegradedReason,
                    diagnostics.GameViewWindowCreated);
            }

            // Fallback: render from Main Camera (Edit Mode when Game View isn't actively rendering)
            Camera cam = _findMainCamera();
            if (cam == null)
            {
                return AddFailureDiagnostics(
                    McpUnitySocketHandler.CreateErrorResponse(
                        "Failed to capture Game View screenshot. ScreenCapture returned null and no Main Camera found as fallback.",
                        "tool_execution_error"),
                    diagnostics.DegradedReason,
                    diagnostics.GameViewWindowCreated);
            }

            McpLogger.LogInfo("ScreenCapture unavailable, falling back to Main Camera render");
            return ScreenshotHelper.CaptureFromCamera(
                cam,
                width,
                height,
                "Game View (via Main Camera)",
                "main_camera_fallback",
                true,
                diagnostics.DegradedReason,
                diagnostics.GameViewWindowCreated);
        }

        /// <summary>
        /// Capture the real composited Game View frame via the editor's own render path
        /// (UnityEditor.PlayModeView.RenderView), which INCLUDES render-pipeline overlay UI (URP
        /// ScreenSpace-Camera canvases) — something no off-screen camera render can do, because URP overlay
        /// cameras only composite into the live Game View swapchain. Focus-independent (RenderView renders on
        /// demand regardless of which editor tab is active). Reflection because RenderView is protected editor
        /// API. Returns the image result, an unavailable reason, and whether it created a Game View window.
        /// </summary>
        private static Tuple<JObject, string, bool> TryCaptureViaRenderView(int width, int height)
        {
            var previousActiveRT = RenderTexture.active;
            RenderTexture dst = null;
            Texture2D tex = null;
            bool gameViewWindowCreated = false;
            try
            {
                var gameViewType = _resolveGameViewType();
                if (gameViewType == null)
                {
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:gameview_type_missing",
                        false);
                }

                bool hadExistingWindow = _hasExistingEditorWindow(gameViewType);
                var gameView = _getGameViewWindow(gameViewType, false);
                gameViewWindowCreated = !hadExistingWindow && gameView != null;
                if (gameView == null)
                {
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:window_null",
                        false);
                }

                // UnityEditor.PlayModeView.RenderView(Vector2 mousePosition, bool clearTexture) → RenderTexture
                var renderViewMethod = _resolveRenderViewMethod(gameViewType);
                if (renderViewMethod == null)
                {
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:method_missing",
                        gameViewWindowCreated);
                }

                var srcRt = _invokeRenderView(renderViewMethod, gameView);
                if (srcRt == null)
                {
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:rendertexture_null",
                        gameViewWindowCreated);
                }

                dst = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                // RenderView's RenderTexture has a flipped Y origin vs Texture2D.ReadPixels → blit with a
                // vertical flip (scale.y = -1, offset.y = 1) so the readback is upright.
                Graphics.Blit(srcRt, dst, new Vector2(1f, -1f), new Vector2(0f, 1f));

                RenderTexture.active = dst;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] pngBytes = tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(pngBytes);

                McpLogger.LogInfo($"Game View screenshot via PlayModeView.RenderView ({width}x{height})");

                return Tuple.Create<JObject, string, bool>(
                    new JObject
                    {
                        ["success"] = true,
                        ["type"] = "image",
                        ["mimeType"] = "image/png",
                        ["data"] = base64,
                        ["message"] = $"Game View screenshot captured via RenderView ({width}x{height})"
                    },
                    null,
                    gameViewWindowCreated);
            }
            catch (Exception ex)
            {
                Exception cause = GetRenderViewFailureCause(ex);
                McpLogger.LogWarning(
                    $"GameView RenderView capture failed, falling back: {cause.Message}");
                return Tuple.Create<JObject, string, bool>(
                    null,
                    $"render_view_unavailable:exception:{cause.GetType().Name}",
                    gameViewWindowCreated);
            }
            finally
            {
                RenderTexture.active = previousActiveRT;
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
                if (dst != null)
                    RenderTexture.ReleaseTemporary(dst);
            }
        }

        private static Exception GetRenderViewFailureCause(Exception exception)
        {
            if (!(exception is System.Reflection.TargetInvocationException))
                return exception;

            Exception cause = exception;
            while (cause.InnerException != null)
                cause = cause.InnerException;
            return cause;
        }

        private static JObject AddFailureDiagnostics(
            JObject errorResponse,
            string degradedReason,
            bool gameViewWindowCreated)
        {
            if (!(errorResponse?["error"] is JObject error))
                return errorResponse;

            string diagnostics =
                $"degraded=true degradedReason={degradedReason}";
            if (gameViewWindowCreated)
                diagnostics += " gameViewWindowCreated=true";

            string message = error["message"]?.ToString();
            error["message"] = string.IsNullOrEmpty(message)
                ? diagnostics
                : $"{message} [{diagnostics}]";
            return errorResponse;
        }
    }

    /// <summary>
    /// Tool for capturing a screenshot from the Scene View
    /// </summary>
    public class ScreenshotSceneViewTool : McpToolBase
    {
        private static Func<SceneView> _getLastActiveSceneView =
            () => SceneView.lastActiveSceneView;
        private static Func<SceneView, Camera> _getSceneViewCamera =
            sceneView => sceneView.camera;
        private static Action<SceneView> _frameSelected = sceneView => sceneView.FrameSelected();
        private static Action<SceneView> _repaintSceneView = sceneView => sceneView.Repaint();
        private static Action<EditorApplication.CallbackFunction> _subscribeToEditorUpdate =
            handler => EditorApplication.update += handler;
        private static Action<EditorApplication.CallbackFunction> _unsubscribeFromEditorUpdate =
            handler => EditorApplication.update -= handler;

        public ScreenshotSceneViewTool()
        {
            Name = "screenshot_scene_view";
            Description = "Captures a screenshot from the Scene View, reflecting the editor camera perspective";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;
                JObject dimensionError = ScreenshotHelper.ValidateDimensions(width, height);
                if (dimensionError != null)
                {
                    tcs.TrySetResult(dimensionError);
                    return;
                }

                SceneView sceneView = _getLastActiveSceneView();
                if (sceneView == null)
                {
                    tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                        "No active Scene View found. Please open a Scene View window.",
                        "tool_execution_error"
                    ));
                    return;
                }

                bool needsDelayedCapture = false;

                // When in prefab editing mode, auto-focus the scene view on the prefab root
                if (PrefabEditingService.Status == PrefabEditingSessionStatus.Active
                    && PrefabEditingService.PrefabRoot != null)
                {
                    Selection.activeGameObject = PrefabEditingService.PrefabRoot;
                    _frameSelected(sceneView);
                    _repaintSceneView(sceneView);
                    needsDelayedCapture = true;
                }

                if (needsDelayedCapture)
                {
                    // Delay one frame to allow Repaint to complete before capturing
                    ScheduleAfterEditorFrames(1, () =>
                    {
                        try
                        {
                            Camera sceneCamera = _getSceneViewCamera(sceneView);
                            if (sceneCamera == null)
                            {
                                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                                    "Scene View camera is not available.",
                                    "tool_execution_error"
                                ));
                                return;
                            }
                            tcs.TrySetResult(ScreenshotHelper.CaptureFromCamera(
                                sceneCamera,
                                width,
                                height,
                                "Scene View",
                                "scene_view_camera",
                                false));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                                $"Error capturing Scene View screenshot: {ex.Message}",
                                "tool_execution_error"
                            ));
                        }
                    });
                }
                else
                {
                    Camera sceneCamera = _getSceneViewCamera(sceneView);
                    if (sceneCamera == null)
                    {
                        tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                            "Scene View camera is not available.",
                            "tool_execution_error"
                        ));
                        return;
                    }
                    tcs.TrySetResult(ScreenshotHelper.CaptureFromCamera(
                        sceneCamera,
                        width,
                        height,
                        "Scene View",
                        "scene_view_camera",
                        false));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing Scene View screenshot: {ex.Message}",
                    "tool_execution_error"
                ));
            }
        }

        private static void ScheduleAfterEditorFrames(int framesToWait, Action action)
        {
            EditorApplication.CallbackFunction handler = null;
            handler = () =>
            {
                if (--framesToWait > 0)
                    return;

                _unsubscribeFromEditorUpdate(handler);
                action();
            };
            _subscribeToEditorUpdate(handler);
        }
    }

    /// <summary>
    /// Tool for capturing a screenshot from a specific Camera
    /// </summary>
    public class ScreenshotCameraTool : McpToolBase
    {
        public ScreenshotCameraTool()
        {
            Name = "screenshot_camera";
            Description = "Captures a specific Camera in the active context. Without a locator, " +
                          "uses Camera.main when no Prefab session is active, or an enabled " +
                          "MainCamera-tagged Camera inside the active Prefab contents.";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;
                string cameraPath = parameters?["cameraPath"]?.ToObject<string>();
                int? cameraInstanceId = parameters?["cameraInstanceId"]?.ToObject<int?>();
                JObject dimensionError = ScreenshotHelper.ValidateDimensions(width, height);
                if (dimensionError != null)
                    return dimensionError;

                Camera cam = null;
                JObject scopeError;
                string capturePath;

                if (cameraInstanceId.HasValue)
                {
                    capturePath = "explicit_camera";
                    scopeError = PrefabSessionScope.TryResolveGameObject(
                        cameraInstanceId, null, out GameObject obj);
                    if (scopeError != null) return scopeError;
                    if (obj != null)
                        cam = obj.GetComponent<Camera>();
                }
                else if (!string.IsNullOrEmpty(cameraPath))
                {
                    capturePath = "explicit_camera";
                    scopeError = PrefabSessionScope.TryResolveGameObject(
                        null, cameraPath, out GameObject obj);
                    if (scopeError != null) return scopeError;
                    if (obj != null)
                        cam = obj.GetComponent<Camera>();
                }
                else
                {
                    scopeError = PrefabSessionScope.TryGetPrefabRoot(out GameObject prefabRoot);
                    if (scopeError != null) return scopeError;

                    if (prefabRoot == null)
                    {
                        capturePath = "camera_main";
                        cam = Camera.main;
                    }
                    else
                    {
                        capturePath = "prefab_main_camera";
                        cam = FindMainCameraInPrefab(prefabRoot);
                        if (cam == null)
                        {
                            return McpUnitySocketHandler.CreateErrorResponse(
                                $"No enabled Camera tagged 'MainCamera' exists inside the active " +
                                $"Prefab contents '{PrefabEditingService.AssetPath}' (root " +
                                $"'{prefabRoot.name}'). screenshot_camera without a locator does " +
                                "not fall back to loaded scene cameras while a Prefab editing " +
                                "session is active. Specify cameraPath or cameraInstanceId inside " +
                                "the Prefab contents, or add an enabled MainCamera-tagged Camera.",
                                "tool_execution_error");
                        }
                    }
                }

                if (cam == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Camera not found. Specify a valid cameraPath, cameraInstanceId, or ensure a Main Camera exists.",
                        "tool_execution_error"
                    );
                }

                return ScreenshotHelper.CaptureFromCamera(
                    cam,
                    width,
                    height,
                    cam.gameObject.name,
                    capturePath,
                    false);
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing camera screenshot: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }

        private static Camera FindMainCameraInPrefab(GameObject prefabRoot)
        {
            foreach (Camera candidate in prefabRoot.GetComponentsInChildren<Camera>(true))
            {
                if (!candidate.isActiveAndEnabled)
                    continue;

                try
                {
                    if (candidate.CompareTag("MainCamera"))
                        return candidate;
                }
                catch (UnityException ex)
                {
                    // A deleted custom tag can leave a loaded Prefab Camera with an invalid
                    // serialized tag. Skip that candidate so a later valid MainCamera remains
                    // discoverable instead of failing the entire scan.
                    McpLogger.LogWarning(
                        $"Skipping Camera '{candidate.gameObject.name}' with an invalid tag: {ex.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Helper class for screenshot operations
    /// </summary>
    internal static class ScreenshotHelper
    {
        private const int MaxDimension = 4096;

        public static JObject ValidateDimensions(int width, int height)
        {
            if (width >= 1 && width <= MaxDimension && height >= 1 && height <= MaxDimension)
                return null;

            return McpUnitySocketHandler.CreateErrorResponse(
                $"Screenshot width and height must each be between 1 and {MaxDimension} pixels " +
                $"(maximum {MaxDimension}). Received width={width}, height={height}.",
                "validation_error");
        }

        public static JObject AddCaptureMetadata(
            JObject response,
            string capturePath,
            bool degraded,
            string degradedReason = null,
            bool gameViewWindowCreated = false)
        {
            response["capturePath"] = capturePath;
            response["degraded"] = degraded;
            if (degraded && !string.IsNullOrEmpty(degradedReason))
                response["degradedReason"] = degradedReason;
            else
                response.Remove("degradedReason");

            string diagnostics =
                $"capturePath={capturePath} degraded={degraded.ToString().ToLowerInvariant()}";
            if (degraded && !string.IsNullOrEmpty(degradedReason))
                diagnostics += $" degradedReason={degradedReason}";

            if (gameViewWindowCreated)
            {
                response["gameViewWindowCreated"] = true;
                diagnostics += " gameViewWindowCreated=true";
            }

            string message = response["message"]?.ToString();
            response["message"] = string.IsNullOrEmpty(message)
                ? diagnostics
                : $"{message} [{diagnostics}]";
            return response;
        }

        /// <summary>
        /// Captures a screenshot from a given camera using RenderTexture
        /// </summary>
        public static JObject CaptureFromCamera(
            Camera camera,
            int width,
            int height,
            string cameraName,
            string capturePath,
            bool degraded,
            string degradedReason = null,
            bool gameViewWindowCreated = false)
        {
            var previousTargetTexture = camera.targetTexture;
            var previousActiveRT = RenderTexture.active;

            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                rt = new RenderTexture(width, height, 24);
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] pngBytes = tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(pngBytes);

                McpLogger.LogInfo($"{cameraName} screenshot captured ({width}x{height})");

                return AddCaptureMetadata(new JObject
                {
                    ["success"] = true,
                    ["type"] = "image",
                    ["mimeType"] = "image/png",
                    ["data"] = base64,
                    ["message"] = $"{cameraName} screenshot captured ({width}x{height})"
                }, capturePath, degraded, degradedReason, gameViewWindowCreated);
            }
            finally
            {
                // Restore original state
                camera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActiveRT;

                if (rt != null)
                    UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Resizes a texture to the target dimensions using RenderTexture blit
        /// </summary>
        public static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            if (source.width == targetWidth && source.height == targetHeight)
                return source;

            var previousActiveRT = RenderTexture.active;
            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, rt);

                RenderTexture.active = rt;
                var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                result.Apply();

                return result;
            }
            finally
            {
                RenderTexture.active = previousActiveRT;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
