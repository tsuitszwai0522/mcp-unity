using System;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

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
            Description = "Captures a screenshot from the Game View, reflecting what the player sees";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;

                // Ensure Game View is open (without stealing focus)
                var gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    EditorWindow.GetWindow(gameViewType, false, null, false);
                }

                // Try ScreenCapture first (works best during Play Mode)
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

                // Fallback: render from Main Camera (works in Edit Mode when Game View isn't actively rendering)
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
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing Game View screenshot: {ex.Message}",
                    "tool_execution_error"
                );
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
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                int width = parameters?["width"]?.ToObject<int>() ?? 960;
                int height = parameters?["height"]?.ToObject<int>() ?? 540;

                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "No active Scene View found. Please open a Scene View window.",
                        "tool_execution_error"
                    );
                }

                Camera sceneCamera = sceneView.camera;
                if (sceneCamera == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Scene View camera is not available.",
                        "tool_execution_error"
                    );
                }

                return ScreenshotHelper.CaptureFromCamera(sceneCamera, width, height, "Scene View");
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error capturing Scene View screenshot: {ex.Message}",
                    "tool_execution_error"
                );
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
