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
        public ScreenshotGameViewTool()
        {
            Name = "screenshot_game_view";
            Description = "Captures a screenshot from the Game View, reflecting what the player sees. Set force_focus=true to force-focus the Game View tab before capturing (prevents accidentally capturing the Scene View when it's the active tab).";
            IsAsync = true;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;
                bool forceFocus = parameters?["force_focus"]?.ToObject<bool?>() ?? false;

                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    // focus flag on GetWindow controls whether the window is brought to front + focused
                    var gameView = EditorWindow.GetWindow(gameViewType, false, null, forceFocus);
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
                            tcs.TrySetResult(CaptureGameView(width, height));
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
                    tcs.TrySetResult(CaptureGameView(width, height));
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

        private static JObject CaptureGameView(int width, int height)
        {
            // Primary: capture the real composited Game View via the editor's own render path
            // (PlayModeView.RenderView). This is focus-independent (no need to bring the Game View tab to
            // front) and DOES include screen-space-camera overlay UI — unlike camera.Render() / a Standard
            // render request, which skip the URP overlay stack, and unlike ScreenCapture which samples
            // whichever editor view currently has focus (often the Scene View).
            var viaRenderView = TryCaptureViaRenderView(width, height);
            if (viaRenderView != null)
                return viaRenderView;

            // Fallback: ScreenCapture (works best during Play Mode; samples the focused view's backbuffer)
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
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

                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "image",
                        ["mimeType"] = "image/png",
                        ["data"] = base64,
                        ["message"] = $"Game View screenshot captured ({width}x{height})"
                    };
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(screenshot);
                }
            }

            // Fallback: render from Main Camera (Edit Mode when Game View isn't actively rendering)
            Camera cam = Camera.main;
            if (cam == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Failed to capture Game View screenshot. ScreenCapture returned null and no Main Camera found as fallback.",
                    "tool_execution_error"
                );
            }

            McpLogger.LogInfo("ScreenCapture unavailable, falling back to Main Camera render");
            return ScreenshotHelper.CaptureFromCamera(cam, width, height, "Game View (via Main Camera)");
        }

        /// <summary>
        /// Capture the real composited Game View frame via the editor's own render path
        /// (UnityEditor.PlayModeView.RenderView), which INCLUDES render-pipeline overlay UI (URP
        /// ScreenSpace-Camera canvases) — something no off-screen camera render can do, because URP overlay
        /// cameras only composite into the live Game View swapchain. Focus-independent (RenderView renders on
        /// demand regardless of which editor tab is active). Reflection because RenderView is protected editor
        /// API. Returns null (caller falls back) when the Game View / method can't be resolved.
        /// </summary>
        private static JObject TryCaptureViaRenderView(int width, int height)
        {
            var previousActiveRT = RenderTexture.active;
            RenderTexture dst = null;
            Texture2D tex = null;
            try
            {
                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null)
                    return null;

                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView == null)
                    return null;

                // UnityEditor.PlayModeView.RenderView(Vector2 mousePosition, bool clearTexture) → RenderTexture
                var playModeViewType = gameViewType.BaseType;
                var renderViewMethod = playModeViewType?.GetMethod("RenderView",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (renderViewMethod == null)
                    return null;

                var srcRt = renderViewMethod.Invoke(gameView, new object[] { Vector2.zero, false }) as RenderTexture;
                if (srcRt == null)
                    return null;

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

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "image",
                    ["mimeType"] = "image/png",
                    ["data"] = base64,
                    ["message"] = $"Game View screenshot captured via RenderView ({width}x{height})"
                };
            }
            catch (Exception ex)
            {
                McpLogger.LogWarning($"GameView RenderView capture failed, falling back: {ex.Message}");
                return null;
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
    }

    /// <summary>
    /// Tool for capturing a screenshot from the Scene View
    /// </summary>
    public class ScreenshotSceneViewTool : McpToolBase
    {
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

                SceneView sceneView = SceneView.lastActiveSceneView;
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
                if (PrefabEditingService.IsEditing && PrefabEditingService.PrefabRoot != null)
                {
                    Selection.activeGameObject = PrefabEditingService.PrefabRoot;
                    sceneView.FrameSelected();
                    sceneView.Repaint();
                    needsDelayedCapture = true;
                }

                if (needsDelayedCapture)
                {
                    // Delay one frame to allow Repaint to complete before capturing
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            Camera sceneCamera = sceneView.camera;
                            if (sceneCamera == null)
                            {
                                tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                                    "Scene View camera is not available.",
                                    "tool_execution_error"
                                ));
                                return;
                            }
                            tcs.TrySetResult(ScreenshotHelper.CaptureFromCamera(sceneCamera, width, height, "Scene View"));
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                                $"Error capturing Scene View screenshot: {ex.Message}",
                                "tool_execution_error"
                            ));
                        }
                    };
                }
                else
                {
                    Camera sceneCamera = sceneView.camera;
                    if (sceneCamera == null)
                    {
                        tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
                            "Scene View camera is not available.",
                            "tool_execution_error"
                        ));
                        return;
                    }
                    tcs.TrySetResult(ScreenshotHelper.CaptureFromCamera(sceneCamera, width, height, "Scene View"));
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
    }

    /// <summary>
    /// Tool for capturing a screenshot from a specific Camera
    /// </summary>
    public class ScreenshotCameraTool : McpToolBase
    {
        public ScreenshotCameraTool()
        {
            Name = "screenshot_camera";
            Description = "Captures a screenshot from a specific Camera in the scene";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;
                string cameraPath = parameters?["cameraPath"]?.ToObject<string>();
                int? cameraInstanceId = parameters?["cameraInstanceId"]?.ToObject<int?>();

                Camera cam = null;

                if (cameraInstanceId.HasValue)
                {
                    var obj = EditorUtility.InstanceIDToObject(cameraInstanceId.Value) as GameObject;
                    if (obj != null)
                        cam = obj.GetComponent<Camera>();
                }
                else if (!string.IsNullOrEmpty(cameraPath))
                {
                    var obj = GameObject.Find(cameraPath);
                    if (obj != null)
                        cam = obj.GetComponent<Camera>();
                }
                else
                {
                    cam = Camera.main;
                }

                if (cam == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Camera not found. Specify a valid cameraPath, cameraInstanceId, or ensure a Main Camera exists.",
                        "tool_execution_error"
                    );
                }

                return ScreenshotHelper.CaptureFromCamera(cam, width, height, cam.gameObject.name);
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing camera screenshot: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }
    }

    /// <summary>
    /// Helper class for screenshot operations
    /// </summary>
    internal static class ScreenshotHelper
    {
        /// <summary>
        /// Captures a screenshot from a given camera using RenderTexture
        /// </summary>
        public static JObject CaptureFromCamera(Camera camera, int width, int height, string cameraName)
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

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "image",
                    ["mimeType"] = "image/png",
                    ["data"] = base64,
                    ["message"] = $"{cameraName} screenshot captured ({width}x{height})"
                };
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
