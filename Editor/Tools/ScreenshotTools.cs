using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
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
        private static Func<EditorWindow, object> _resolveGameViewHost = window =>
        {
            var parentField = typeof(EditorWindow).GetField(
                "m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
            if (parentField == null)
                throw new MissingFieldException(typeof(EditorWindow).FullName, "m_Parent");
            return parentField.GetValue(window);
        };
        private static Func<object, EditorWindow> _resolveActualView = host =>
        {
            PropertyInfo actualViewProperty = host?.GetType().GetProperty(
                "actualView",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return actualViewProperty?.GetValue(host, null) as EditorWindow;
        };
        private static Func<object, MethodInfo> _resolveRepaintImmediatelyMethod = host =>
            host?.GetType().GetMethod(
                "RepaintImmediately",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
        private static Action<MethodInfo, object> _invokeRepaintImmediately =
            (method, host) => method.Invoke(host, null);
        private static Action<Action<ScriptableRenderContext, Camera>>
            _subscribeBeginCameraRendering = handler =>
                RenderPipelineManager.beginCameraRendering += handler;
        private static Action<Action<ScriptableRenderContext, Camera>>
            _unsubscribeBeginCameraRendering = handler =>
                RenderPipelineManager.beginCameraRendering -= handler;
        private static Action<Camera.CameraCallback> _subscribeCameraPreRender =
            handler => Camera.onPreRender += handler;
        private static Action<Camera.CameraCallback> _unsubscribeCameraPreRender =
            handler => Camera.onPreRender -= handler;
        private static Func<IEnumerable<Camera>> _findAllCameras =
            () => UnityEngine.Resources.FindObjectsOfTypeAll<Camera>();
        private static Func<HashSet<int>> _findLoadedSceneHandles = () =>
        {
            var handles = new HashSet<int>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
                if (loadedScene.IsValid())
                    handles.Add(loadedScene.handle);
            }
            return handles;
        };
        private static Action<Camera, bool> _setCameraEnabled =
            (camera, enabled) => camera.enabled = enabled;

        private const int MaxDisclosedCameras = 8;
        private const string ForceFocusRemediation = "retry_with_force_focus=true";

        private enum RerenderEvidence
        {
            Observed,
            KnownAbsent,
            Unknown
        }

        private sealed class FreshnessMeasurement
        {
            public RerenderEvidence Evidence { get; }
            public int CameraRenders { get; }
            public string Reason { get; }

            public FreshnessMeasurement(
                RerenderEvidence evidence,
                int cameraRenders,
                string reason)
            {
                Evidence = evidence;
                CameraRenders = cameraRenders;
                Reason = reason;
            }
        }

        private sealed class CaptureDecision
        {
            public string FrameFresh { get; }
            public string FrameFreshReason { get; }
            public string DegradedReason { get; }
            public string Remediation { get; }

            public CaptureDecision(
                string frameFresh,
                string frameFreshReason,
                string degradedReason,
                string remediation)
            {
                FrameFresh = frameFresh;
                FrameFreshReason = frameFreshReason;
                DegradedReason = degradedReason;
                Remediation = remediation;
            }

            public CaptureDecision WithAdditionalDegradedReason(string reason)
            {
                return new CaptureDecision(
                    FrameFresh,
                    FrameFreshReason,
                    ScreenshotGameViewTool.AppendDegradedReason(DegradedReason, reason),
                    Remediation);
            }
        }

        private sealed class CaptureDiagnosticsState
        {
            public bool GameViewWindowCreated;
            public FreshnessMeasurement Freshness;
            public readonly List<CameraDisclosure> ContextCameras =
                new List<CameraDisclosure>();
        }

        private sealed class CameraDisclosure
        {
            public string Name;
            public string ScenePath;
        }

        private sealed class CameraIsolationScope : IDisposable
        {
            private readonly List<Camera> _disabledCameras = new List<Camera>();

            public int IsolatedCount => _disabledCameras.Count;
            public IEnumerable<Camera> IsolatedCameras => _disabledCameras;

            public static CameraIsolationScope Create(
                CaptureDiagnosticsState diagnostics,
                out Camera[] isolatedCamerasOnFailure,
                out string restoreFailureReason)
            {
                var scope = new CameraIsolationScope();
                isolatedCamerasOnFailure = Array.Empty<Camera>();
                restoreFailureReason = null;
                try
                {
                    scope.IsolateCameras(diagnostics);
                    return scope;
                }
                catch
                {
                    isolatedCamerasOnFailure = scope.IsolatedCameras.ToArray();
                    restoreFailureReason = scope.RestoreCameras();
                    throw;
                }
            }

            public void Dispose()
            {
                RestoreCameras();
            }

            public string RestoreCameras()
            {
                string failureReason = null;
                foreach (Camera camera in _disabledCameras)
                {
                    try
                    {
                        if (camera != null)
                            _setCameraEnabled(camera, true);
                    }
                    catch (Exception ex)
                    {
                        failureReason = "camera_restore_failed";
                        McpLogger.LogWarning(
                            $"Failed to restore isolated Camera " +
                            $"'{(camera == null ? "<destroyed>" : camera.name)}': {ex.Message}");
                    }
                }
                _disabledCameras.Clear();
                return failureReason;
            }

            private void IsolateCameras(CaptureDiagnosticsState diagnostics)
            {
                HashSet<int> loadedSceneHandles = _findLoadedSceneHandles();

                var contextSceneHandles = new HashSet<int>();
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null && prefabStage.scene.IsValid())
                    contextSceneHandles.Add(prefabStage.scene.handle);

                if (PrefabEditingService.Status != PrefabEditingSessionStatus.None)
                {
                    GameObject prefabRoot = PrefabEditingService.PrefabRoot;
                    if (prefabRoot != null && prefabRoot.scene.IsValid())
                        contextSceneHandles.Add(prefabRoot.scene.handle);
                }

                foreach (Camera camera in _findAllCameras())
                {
                    if (camera == null
                        || !camera.enabled
                        || !camera.gameObject.activeInHierarchy
                        || camera.cameraType != CameraType.Game)
                    {
                        continue;
                    }

                    Scene cameraScene = camera.gameObject.scene;
                    if (!cameraScene.IsValid() || loadedSceneHandles.Contains(cameraScene.handle))
                        continue;

                    var disclosure = new CameraDisclosure
                    {
                        Name = camera.name,
                        ScenePath = cameraScene.path ?? string.Empty
                    };
                    if (contextSceneHandles.Contains(cameraScene.handle))
                    {
                        diagnostics.ContextCameras.Add(disclosure);
                        continue;
                    }

                    if (!ShouldIsolateCameraScene(
                        cameraScene, loadedSceneHandles, contextSceneHandles))
                    {
                        continue;
                    }

                    try
                    {
                        // Track before invoking the setter: a custom/native setter may change
                        // state and then throw, and such a camera still needs a restore attempt.
                        _disabledCameras.Add(camera);
                        _setCameraEnabled(camera, false);
                    }
                    catch (Exception ex)
                    {
                        McpLogger.LogWarning(
                            $"Failed to isolate Camera '{camera.name}': {ex.Message}");
                    }
                }
            }

            private static bool ShouldIsolateCameraScene(
                Scene cameraScene,
                ISet<int> loadedSceneHandles,
                ISet<int> contextSceneHandles)
            {
                return cameraScene.IsValid()
                    && !loadedSceneHandles.Contains(cameraScene.handle)
                    && !contextSceneHandles.Contains(cameraScene.handle)
                    && EditorSceneManager.IsPreviewScene(cameraScene);
            }

        }

        public ScreenshotGameViewTool()
        {
            Name = "screenshot_game_view";
            Description = "Captures a screenshot from the Game View, reflecting what the player sees. " +
                          "Only frameFresh=verified means the pixels reflect the current scene. " +
                          "When frameFreshReason includes game_view_not_active_tab, retry with " +
                          "force_focus=true so the Game View becomes the active tab and rerenders " +
                          "before capture. When it includes repaint_immediately_unavailable:, retry " +
                          "with force_focus=true only when isolatedCameraCount=0; focus cannot repair " +
                          "the post-isolation frame while isolated cameras exist. no_camera_render " +
                          "has no force-focus remediation. " +
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
            CameraIsolationScope cameraIsolation = null;
            int isolatedCount = 0;
            Tuple<JObject, CaptureDecision> outcome;
            Camera[] isolatedCameras = Array.Empty<Camera>();
            Camera[] createFailureCameras = Array.Empty<Camera>();
            string createRestoreFailureReason = null;
            string cameraRestoreFailureReason = null;

            try
            {
                try
                {
                    cameraIsolation = CameraIsolationScope.Create(
                        diagnostics,
                        out createFailureCameras,
                        out createRestoreFailureReason);
                    isolatedCount = cameraIsolation.IsolatedCount;
                    outcome = CaptureGameViewCore(width, height, diagnostics, isolatedCount);
                }
                catch (Exception ex)
                {
                    if (cameraIsolation == null)
                    {
                        isolatedCameras = createFailureCameras;
                        isolatedCount = createFailureCameras.Length;
                        cameraRestoreFailureReason = createRestoreFailureReason;
                    }
                    CaptureDecision decision = DecideCapture(
                        diagnostics.Freshness,
                        isolatedCount,
                        diagnostics.ContextCameras.Count,
                        true,
                        false,
                        $"capture_failed:{ex.GetType().Name}",
                        null);
                    outcome = Tuple.Create(
                        AddFailureDiagnostics(
                            McpUnitySocketHandler.CreateErrorResponse(
                                $"Error capturing Game View screenshot: {ex.Message}",
                                "tool_execution_error"),
                            decision,
                            diagnostics.GameViewWindowCreated),
                        decision);
                }
                if (cameraIsolation != null)
                    isolatedCameras = cameraIsolation.IsolatedCameras.ToArray();
            }
            finally
            {
                if (cameraIsolation != null)
                    cameraRestoreFailureReason = cameraIsolation.RestoreCameras();
            }

            CaptureDecision finalDecision = ApplyPostCaptureDegradation(
                outcome.Item1,
                outcome.Item2,
                cameraRestoreFailureReason);
            return AddGameViewDiagnostics(
                outcome.Item1,
                diagnostics,
                isolatedCameras,
                isolatedCount,
                finalDecision);
        }

        private static Tuple<JObject, CaptureDecision> CaptureGameViewCore(
            int width,
            int height,
            CaptureDiagnosticsState diagnostics,
            int isolatedCount)
        {
            // Primary: capture the real composited Game View via the editor's own render path
            // (PlayModeView.RenderView). This is focus-independent (no need to bring the Game View tab to
            // front) and DOES include screen-space-camera overlay UI — unlike camera.Render() / a Standard
            // render request, which skip the URP overlay stack, and unlike ScreenCapture which samples
            // whichever editor view currently has focus (often the Scene View).
            Tuple<JObject, string, bool> renderViewAttempt =
                TryCaptureViaRenderView(width, height, diagnostics);
            diagnostics.GameViewWindowCreated |= renderViewAttempt.Item3;
            if (renderViewAttempt.Item1 != null)
            {
                CaptureDecision renderViewDecision = DecideCapture(
                    diagnostics.Freshness,
                    isolatedCount,
                    diagnostics.ContextCameras.Count,
                    false,
                    true,
                    null,
                    null);

                return Tuple.Create(
                    ApplyCaptureDecision(
                        renderViewAttempt.Item1,
                        "render_view",
                        renderViewDecision,
                        diagnostics.GameViewWindowCreated),
                    renderViewDecision);
            }

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

                    CaptureDecision decision = DecideCapture(
                        diagnostics.Freshness,
                        isolatedCount,
                        diagnostics.ContextCameras.Count,
                        true,
                        true,
                        renderViewAttempt.Item2,
                        null);
                    return Tuple.Create(ApplyCaptureDecision(new JObject
                    {
                        ["success"] = true,
                        ["type"] = "image",
                        ["mimeType"] = "image/png",
                        ["data"] = base64,
                        ["message"] = $"Game View screenshot captured ({width}x{height})"
                    },
                    "screen_capture",
                    decision,
                    diagnostics.GameViewWindowCreated), decision);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(screenshot);
                }
            }

            string capturePathReason = AppendDegradedReason(
                renderViewAttempt.Item2,
                "screen_capture_returned_null");

            return CaptureViaMainCameraFallback(
                width,
                height,
                diagnostics,
                isolatedCount,
                capturePathReason);
        }

        private static Tuple<JObject, CaptureDecision> CaptureViaMainCameraFallback(
            int width,
            int height,
            CaptureDiagnosticsState diagnostics,
            int isolatedCount,
            string capturePathReason)
        {
            Tuple<JObject, GameObject> prefabScope;
            try
            {
                prefabScope = _resolvePrefabRoot();
            }
            catch (Exception ex)
            {
                CaptureDecision failureDecision = DecideCapture(
                    diagnostics.Freshness,
                    isolatedCount,
                    diagnostics.ContextCameras.Count,
                    true,
                    false,
                    capturePathReason,
                    $"main_camera_fallback_failed:{ex.GetType().Name}");
                return Tuple.Create(
                    AddFailureDiagnostics(
                        McpUnitySocketHandler.CreateErrorResponse(
                            $"Error resolving the Main Camera fallback: {ex.Message}",
                            "tool_execution_error"),
                        failureDecision,
                        diagnostics.GameViewWindowCreated),
                    failureDecision);
            }

            JObject scopeError = prefabScope.Item1;
            GameObject prefabRoot = prefabScope.Item2;
            if (scopeError != null)
            {
                CaptureDecision failureDecision = DecideCapture(
                    diagnostics.Freshness,
                    isolatedCount,
                    diagnostics.ContextCameras.Count,
                    true,
                    false,
                    capturePathReason,
                    "main_camera_fallback_blocked:prefab_scope_error");
                return Tuple.Create(
                    AddFailureDiagnostics(
                        scopeError,
                        failureDecision,
                        diagnostics.GameViewWindowCreated),
                    failureDecision);
            }
            if (prefabRoot != null)
            {
                CaptureDecision failureDecision = DecideCapture(
                    diagnostics.Freshness,
                    isolatedCount,
                    diagnostics.ContextCameras.Count,
                    true,
                    false,
                    capturePathReason,
                    "main_camera_fallback_blocked:prefab_session");
                return Tuple.Create(AddFailureDiagnostics(
                    McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to capture the Game View. Prefab contents " +
                        $"'{PrefabEditingService.AssetPath}' (root '{prefabRoot.name}') are open. " +
                        "screenshot_game_view does not fall back to a loaded scene Main Camera " +
                        "during a Prefab editing session.",
                        "tool_execution_error"),
                    failureDecision,
                    diagnostics.GameViewWindowCreated), failureDecision);
            }

            // Fallback: render from Main Camera (Edit Mode when Game View isn't actively rendering)
            Camera cam = _findMainCamera();
            if (cam == null)
            {
                const string unavailableReason =
                    "main_camera_fallback_unavailable:no_main_camera";
                CaptureDecision failureDecision = DecideCapture(
                    diagnostics.Freshness,
                    isolatedCount,
                    diagnostics.ContextCameras.Count,
                    true,
                    false,
                    capturePathReason,
                    unavailableReason);
                return Tuple.Create(AddFailureDiagnostics(
                    McpUnitySocketHandler.CreateErrorResponse(
                        "Failed to capture Game View screenshot. " +
                        "No Main Camera was found for the camera fallback.",
                        "tool_execution_error"),
                    failureDecision,
                    diagnostics.GameViewWindowCreated), failureDecision);
            }

            McpLogger.LogInfo("ScreenCapture unavailable, falling back to Main Camera render");
            CaptureDecision decision = DecideCapture(
                diagnostics.Freshness,
                isolatedCount,
                diagnostics.ContextCameras.Count,
                true,
                false,
                capturePathReason,
                null);
            return Tuple.Create(ScreenshotHelper.CaptureFromCamera(
                cam,
                width,
                height,
                "Game View (via Main Camera)",
                "main_camera_fallback",
                !string.IsNullOrEmpty(decision.DegradedReason),
                decision.DegradedReason,
                diagnostics.GameViewWindowCreated), decision);
        }

        /// <summary>
        /// Capture the real composited Game View frame via the editor's own render path
        /// (UnityEditor.PlayModeView.RenderView), which INCLUDES render-pipeline overlay UI (URP
        /// ScreenSpace-Camera canvases) — something no off-screen camera render can do, because URP overlay
        /// cameras only composite into the live Game View swapchain. Focus-independent (RenderView renders on
        /// demand regardless of which editor tab is active). Reflection because RenderView is protected editor
        /// API. Returns the image result, an unavailable reason, and whether it created a Game View window.
        /// </summary>
        private static Tuple<JObject, string, bool> TryCaptureViaRenderView(
            int width,
            int height,
            CaptureDiagnosticsState diagnostics)
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
                    diagnostics.Freshness = new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "repaint_immediately_not_attempted");
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
                    diagnostics.Freshness = new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "repaint_immediately_not_attempted");
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:window_null",
                        false);
                }

                // UnityEditor.PlayModeView.RenderView(Vector2 mousePosition, bool clearTexture) → RenderTexture
                var renderViewMethod = _resolveRenderViewMethod(gameViewType);
                if (renderViewMethod == null)
                {
                    diagnostics.Freshness = new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "repaint_immediately_not_attempted");
                    return Tuple.Create<JObject, string, bool>(
                        null,
                        "render_view_unavailable:method_missing",
                        gameViewWindowCreated);
                }

                diagnostics.Freshness = PerformFreshnessHandshake(gameView);
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

        private static FreshnessMeasurement PerformFreshnessHandshake(EditorWindow gameView)
        {
            object host;
            EditorWindow actualView;
            MethodInfo repaintImmediately;
            try
            {
                host = _resolveGameViewHost(gameView);
                if (host == null)
                {
                    return new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "repaint_immediately_unavailable:host_null");
                }

                try
                {
                    actualView = _resolveActualView(host);
                }
                catch (Exception ex)
                {
                    Exception cause = GetRenderViewFailureCause(ex);
                    McpLogger.LogWarning(
                        $"GameView active-tab resolution failed; continuing capture: " +
                        cause.Message);
                    return new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "actual_view_unresolved");
                }
                if (actualView == null)
                {
                    return new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "actual_view_unresolved");
                }
                if (!ReferenceEquals(actualView, gameView))
                {
                    return new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "game_view_not_active_tab");
                }

                repaintImmediately = _resolveRepaintImmediatelyMethod(host);
                if (repaintImmediately == null)
                {
                    return new FreshnessMeasurement(
                        RerenderEvidence.KnownAbsent,
                        0,
                        "repaint_immediately_unavailable:method_missing");
                }
            }
            catch (Exception ex)
            {
                Exception cause = GetRenderViewFailureCause(ex);
                return new FreshnessMeasurement(
                    RerenderEvidence.KnownAbsent,
                    0,
                    $"repaint_immediately_unavailable:{cause.GetType().Name}");
            }

            int cameraRenders = 0;
            Action<ScriptableRenderContext, Camera> srpHandler =
                (context, camera) =>
                {
                    if (camera != null
                        && camera.enabled
                        && camera.gameObject.activeInHierarchy
                        && camera.cameraType == CameraType.Game)
                        cameraRenders++;
                };
            Camera.CameraCallback builtInHandler = camera =>
            {
                if (camera != null
                    && camera.enabled
                    && camera.gameObject.activeInHierarchy
                    && camera.cameraType == CameraType.Game)
                    cameraRenders++;
            };
            bool srpSubscribed = false;
            bool builtInSubscribed = false;
            RerenderEvidence evidence;
            string evidenceReason;
            Exception counterCleanupFailure = null;
            try
            {
                _subscribeBeginCameraRendering(srpHandler);
                srpSubscribed = true;
                _subscribeCameraPreRender(builtInHandler);
                builtInSubscribed = true;
                _invokeRepaintImmediately(repaintImmediately, host);
                evidence = cameraRenders > 0
                    ? RerenderEvidence.Observed
                    : RerenderEvidence.KnownAbsent;
                evidenceReason = cameraRenders > 0
                    ? "camera_render_observed"
                    : "no_camera_render";
            }
            catch (Exception ex)
            {
                Exception cause = GetRenderViewFailureCause(ex);
                evidence = srpSubscribed && builtInSubscribed && cameraRenders == 0
                    ? RerenderEvidence.KnownAbsent
                    : RerenderEvidence.Unknown;
                evidenceReason =
                    $"repaint_immediately_unavailable:{cause.GetType().Name}";
                McpLogger.LogWarning(
                    $"GameView synchronous repaint unavailable, continuing capture: " +
                    cause.Message);
            }
            finally
            {
                if (builtInSubscribed)
                {
                    try
                    {
                        _unsubscribeCameraPreRender(builtInHandler);
                    }
                    catch (Exception ex)
                    {
                        counterCleanupFailure = GetRenderViewFailureCause(ex);
                    }
                }
                if (srpSubscribed)
                {
                    try
                    {
                        _unsubscribeBeginCameraRendering(srpHandler);
                    }
                    catch (Exception ex)
                    {
                        if (counterCleanupFailure == null)
                            counterCleanupFailure = GetRenderViewFailureCause(ex);
                    }
                }

                if (counterCleanupFailure != null)
                {
                    McpLogger.LogWarning(
                        "GameView repaint render-counter cleanup failed; continuing capture: " +
                        counterCleanupFailure.Message);
                }
            }

            return new FreshnessMeasurement(
                evidence,
                cameraRenders,
                counterCleanupFailure == null
                    ? evidenceReason
                    : AppendReason(
                        $"render_counter_cleanup_failed:{counterCleanupFailure.GetType().Name}",
                        evidenceReason));
        }

        private static string AppendDegradedReason(string existing, string additional)
        {
            if (string.IsNullOrEmpty(additional))
                return existing;
            return string.IsNullOrEmpty(existing)
                ? additional
                : existing + ";" + additional;
        }

        private static string AppendReason(string primary, string underlying)
        {
            if (string.IsNullOrEmpty(underlying) || primary == underlying)
                return primary;
            return string.IsNullOrEmpty(primary)
                ? underlying
                : primary + ";" + underlying;
        }

        private static bool HasReason(string reasons, string exactReason)
        {
            return !string.IsNullOrEmpty(reasons)
                && reasons.Split(';').Any(reason => reason == exactReason);
        }

        private static bool HasReasonPrefix(string reasons, string prefix)
        {
            return !string.IsNullOrEmpty(reasons)
                && reasons.Split(';').Any(
                    reason => reason.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static CaptureDecision DecideCapture(
            FreshnessMeasurement freshness,
            int isolatedCount,
            int contextCount,
            bool capturePathFallback,
            bool pixelsMayPredateIsolation,
            string capturePathReason,
            string fallbackFailureReason)
        {
            freshness = freshness ?? new FreshnessMeasurement(
                RerenderEvidence.Unknown,
                0,
                "repaint_immediately_not_attempted");

            bool isolatedFramePredatesIsolation = pixelsMayPredateIsolation
                && isolatedCount > 0
                && freshness.Evidence == RerenderEvidence.KnownAbsent
                && !HasReasonPrefix(
                    freshness.Reason,
                    "render_counter_cleanup_failed:");
            string frameFresh;
            string frameFreshReason;
            if (capturePathFallback)
            {
                frameFresh = "not_applicable";
                frameFreshReason = AppendReason(
                    "capture_path_not_render_view", freshness.Reason);
            }
            else if (contextCount > 0)
            {
                frameFresh = "unknown";
                frameFreshReason = AppendReason(
                    "context_camera_may_compose", freshness.Reason);
            }
            else if (freshness.Reason.StartsWith(
                "render_counter_cleanup_failed:",
                StringComparison.Ordinal))
            {
                frameFresh = "unknown";
                frameFreshReason = freshness.Reason;
            }
            else if (freshness.Evidence == RerenderEvidence.Observed)
            {
                frameFresh = "verified";
                frameFreshReason = freshness.Reason;
            }
            else if (freshness.Evidence == RerenderEvidence.KnownAbsent)
            {
                frameFresh = "not_fresh";
                frameFreshReason = freshness.Reason;
            }
            else
            {
                frameFresh = "unknown";
                frameFreshReason = freshness.Reason;
            }

            string degradedReason = isolatedFramePredatesIsolation
                ? "isolated_frame_predates_isolation"
                : null;
            if (capturePathFallback)
                degradedReason = AppendDegradedReason(degradedReason, capturePathReason);
            degradedReason = AppendDegradedReason(degradedReason, fallbackFailureReason);

            bool inactiveTabCanBeRemediated =
                HasReason(frameFreshReason, "game_view_not_active_tab");
            bool repaintUnavailableCanBeRemediated = isolatedCount == 0
                && HasReasonPrefix(
                    frameFreshReason,
                    "repaint_immediately_unavailable:");

            return new CaptureDecision(
                frameFresh,
                frameFreshReason,
                degradedReason,
                inactiveTabCanBeRemediated || repaintUnavailableCanBeRemediated
                    ? ForceFocusRemediation
                    : null);
        }

        private static CaptureDecision ApplyPostCaptureDegradation(
            JObject response,
            CaptureDecision decision,
            string additionalReason)
        {
            if (string.IsNullOrEmpty(additionalReason))
                return decision;

            string previousReason = decision.DegradedReason;
            string previousDiagnostics = BuildDegradedDiagnostics(previousReason);
            CaptureDecision updatedDecision =
                decision.WithAdditionalDegradedReason(additionalReason);
            string updatedDiagnostics =
                BuildDegradedDiagnostics(updatedDecision.DegradedReason);

            if (response?["error"] is JObject error)
            {
                error["message"] = error["message"]?.ToString()
                    .Replace(previousDiagnostics, updatedDiagnostics);
                return updatedDecision;
            }

            response["degraded"] = true;
            response["degradedReason"] = updatedDecision.DegradedReason;
            response["message"] = response["message"]?.ToString()
                .Replace(previousDiagnostics, updatedDiagnostics);
            return updatedDecision;
        }

        private static string BuildDegradedDiagnostics(string degradedReason)
        {
            return string.IsNullOrEmpty(degradedReason)
                ? "degraded=false"
                : $"degraded=true degradedReason={degradedReason}";
        }

        private static JObject ApplyCaptureDecision(
            JObject response,
            string capturePath,
            CaptureDecision decision,
            bool gameViewWindowCreated)
        {
            return ScreenshotHelper.AddCaptureMetadata(
                response,
                capturePath,
                !string.IsNullOrEmpty(decision.DegradedReason),
                decision.DegradedReason,
                gameViewWindowCreated);
        }

        private static JObject AddGameViewDiagnostics(
            JObject response,
            CaptureDiagnosticsState diagnostics,
            IEnumerable<Camera> isolatedCameraObjects,
            int isolatedCount,
            CaptureDecision decision)
        {
            JArray isolatedCameras = BuildCameraDisclosures(isolatedCameraObjects);
            JArray contextCameras = BuildCameraDisclosures(diagnostics.ContextCameras);
            int cameraRenders = diagnostics.Freshness?.CameraRenders ?? 0;
            string diagnosticText =
                $"frameFresh={decision.FrameFresh} " +
                $"cameraRenders={cameraRenders} " +
                $"frameFreshReason={decision.FrameFreshReason} " +
                $"isolatedCameraCount={isolatedCount} " +
                $"contextCameraCount={diagnostics.ContextCameras.Count}";
            if (!string.IsNullOrEmpty(decision.Remediation))
                diagnosticText += $" remediation={decision.Remediation}";

            if (response?["error"] is JObject error)
            {
                string errorMessage = error["message"]?.ToString();
                error["message"] = AppendMessageDiagnostics(errorMessage, diagnosticText);
                return response;
            }

            response["frameFresh"] = decision.FrameFresh;
            response["cameraRenders"] = cameraRenders;
            response["frameFreshReason"] = decision.FrameFreshReason;
            response["isolatedCameras"] = isolatedCameras;
            response["contextCameras"] = contextCameras;
            response["isolatedCameraCount"] = isolatedCount;
            response["contextCameraCount"] = diagnostics.ContextCameras.Count;

            string message = response["message"]?.ToString();
            response["message"] = AppendMessageDiagnostics(message, diagnosticText);
            return response;
        }

        private static string AppendMessageDiagnostics(string message, string diagnostics)
        {
            if (string.IsNullOrEmpty(message))
                return diagnostics;

            int openBracket = message.LastIndexOf('[');
            int closeBracket = message.LastIndexOf(']');
            if (openBracket >= 0 && closeBracket == message.Length - 1 && openBracket < closeBracket)
                return message.Insert(closeBracket, " " + diagnostics);

            return $"{message} [{diagnostics}]";
        }

        private static JArray BuildCameraDisclosures(
            IEnumerable<CameraDisclosure> disclosures)
        {
            var result = new JArray();
            foreach (CameraDisclosure disclosure in disclosures)
            {
                if (result.Count >= MaxDisclosedCameras)
                    break;
                result.Add(new JObject
                {
                    ["name"] = disclosure.Name ?? string.Empty,
                    ["scenePath"] = disclosure.ScenePath ?? string.Empty
                });
            }
            return result;
        }

        private static JArray BuildCameraDisclosures(IEnumerable<Camera> cameras)
        {
            var result = new JArray();
            foreach (Camera camera in cameras)
            {
                if (result.Count >= MaxDisclosedCameras)
                    break;
                if (camera == null)
                    continue;
                result.Add(new JObject
                {
                    ["name"] = camera.name ?? string.Empty,
                    ["scenePath"] = camera.gameObject.scene.path ?? string.Empty
                });
            }
            return result;
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
            CaptureDecision decision,
            bool gameViewWindowCreated)
        {
            if (!(errorResponse?["error"] is JObject error))
                return errorResponse;

            bool degraded = !string.IsNullOrEmpty(decision.DegradedReason);
            string diagnostics = $"degraded={degraded.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrEmpty(decision.DegradedReason))
                diagnostics += $" degradedReason={decision.DegradedReason}";
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
