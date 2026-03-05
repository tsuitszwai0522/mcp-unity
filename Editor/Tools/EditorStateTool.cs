using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for querying the current Unity Editor state
    /// </summary>
    public class GetEditorStateTool : McpToolBase
    {
        public GetEditorStateTool()
        {
            Name = "get_editor_state";
            Description = "Gets the current Unity Editor state including play mode, compilation status, active scene, and build platform";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                var result = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Editor state retrieved",
                    ["state"] = new JObject
                    {
                        ["isPlaying"] = EditorApplication.isPlaying,
                        ["isPaused"] = EditorApplication.isPaused,
                        ["isCompiling"] = EditorApplication.isCompiling,
                        ["currentScene"] = SceneManager.GetActiveScene().path,
                        ["platform"] = EditorUserBuildSettings.activeBuildTarget.ToString()
                    }
                };

                McpLogger.LogInfo("Editor state retrieved");
                return result;
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error getting editor state: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for controlling the Unity Editor play mode state
    /// </summary>
    public class SetEditorStateTool : McpToolBase
    {
        public SetEditorStateTool()
        {
            Name = "set_editor_state";
            Description = "Controls Unity Editor play mode: play, pause, unpause, or stop";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                string action = parameters?["action"]?.ToObject<string>();

                if (string.IsNullOrEmpty(action))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Missing required parameter: action (play, pause, unpause, stop)",
                        "validation_error"
                    );
                }

                switch (action.ToLower())
                {
                    case "play":
                        EditorApplication.isPlaying = true;
                        break;
                    case "pause":
                        EditorApplication.isPaused = true;
                        break;
                    case "unpause":
                        EditorApplication.isPaused = false;
                        break;
                    case "stop":
                        EditorApplication.isPlaying = false;
                        break;
                    default:
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Invalid action: '{action}'. Must be one of: play, pause, unpause, stop",
                            "validation_error"
                        );
                }

                McpLogger.LogInfo($"Editor state action executed: {action}");

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Editor state action '{action}' executed successfully",
                    ["state"] = new JObject
                    {
                        ["isPlaying"] = EditorApplication.isPlaying,
                        ["isPaused"] = EditorApplication.isPaused
                    }
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error setting editor state: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }
    }

    /// <summary>
    /// Tool for reading the current Unity Editor selection
    /// </summary>
    public class GetSelectionTool : McpToolBase
    {
        public GetSelectionTool()
        {
            Name = "get_selection";
            Description = "Gets the currently selected objects in the Unity Editor (GameObjects in hierarchy and/or assets in Project window)";
        }

        public override JObject Execute(JObject parameters)
        {
            try
            {
                var selectionData = new JObject();

                // Active GameObject (hierarchy selection)
                if (Selection.activeGameObject != null)
                {
                    var go = Selection.activeGameObject;
                    selectionData["activeGameObject"] = new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["path"] = GetGameObjectPath(go)
                    };
                }

                // All selected GameObjects
                var gameObjects = new JArray();
                foreach (var go in Selection.gameObjects)
                {
                    gameObjects.Add(new JObject
                    {
                        ["name"] = go.name,
                        ["instanceId"] = go.GetInstanceID(),
                        ["path"] = GetGameObjectPath(go)
                    });
                }
                selectionData["gameObjects"] = gameObjects;

                // Active Object (could be an asset in the Project window)
                if (Selection.activeObject != null && !(Selection.activeObject is GameObject))
                {
                    var obj = Selection.activeObject;
                    selectionData["activeObject"] = new JObject
                    {
                        ["name"] = obj.name,
                        ["instanceId"] = obj.GetInstanceID(),
                        ["type"] = obj.GetType().Name,
                        ["assetPath"] = AssetDatabase.GetAssetPath(obj)
                    };
                }

                selectionData["count"] = Selection.objects.Length;

                McpLogger.LogInfo($"Selection retrieved: {Selection.objects.Length} object(s)");

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Current selection: {Selection.objects.Length} object(s)",
                    ["selection"] = selectionData
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error getting selection: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
