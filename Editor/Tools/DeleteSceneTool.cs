using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for deleting a Unity scene and removing it from Build Settings
    /// </summary>
    public class DeleteSceneTool : McpToolBase
    {
        public DeleteSceneTool()
        {
            Name = "delete_scene";
            Description = "Deletes a scene by path or name and removes it from Build Settings. " +
                "A loaded scene is closed without saving first and the response reports discarded unsaved changes; " +
                "the only loaded scene is refused because Unity cannot close the last loaded scene";
        }

        // Seams for the refusal branches; production reads and closes the real Editor scenes.
        internal static Func<int> LoadedSceneCount = () => SceneManager.loadedSceneCount;
        internal static Func<Scene, bool> CloseLoadedScene =
            scene => UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);

        /// <summary>
        /// Execute the DeleteScene tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            string scenePath = parameters["scenePath"]?.ToObject<string>();
            string sceneName = parameters["sceneName"]?.ToObject<string>();
            string folderPath = parameters["folderPath"]?.ToObject<string>();

            if (string.IsNullOrEmpty(scenePath))
            {
                if (string.IsNullOrEmpty(sceneName))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Provide either 'scenePath' or 'sceneName'",
                        "validation_error"
                    );
                }

                // Resolve scene path by name (optionally within folderPath)
                string filter = $"{sceneName} t:Scene";
                string[] searchInFolders = null;
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // Ensure folder exists
                    if (!AssetDatabase.IsValidFolder(folderPath))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Folder '{folderPath}' does not exist",
                            "not_found_error"
                        );
                    }
                    searchInFolders = new[] { folderPath };
                }

                var guids = AssetDatabase.FindAssets(filter, searchInFolders);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    {
                        scenePath = path;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(scenePath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Scene named '{sceneName}' not found",
                        "not_found_error"
                    );
                }
            }

            try
            {
                // If the scene is open, close it without saving changes. Unity refuses to close the last
                // loaded scene (it only logs an error), so refuse first instead of deleting the asset under a
                // scene that stays loaded; a failed close likewise leaves the asset untouched.
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
                bool wasLoaded = scene.IsValid() && scene.isLoaded;
                bool discardedUnsavedChanges = wasLoaded && scene.isDirty;
                if (wasLoaded)
                {
                    if (LoadedSceneCount() <= 1)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Scene '{scenePath}' is the only loaded scene and Unity cannot close the last loaded scene, " +
                            "so it was not deleted. Open another scene first, then retry.",
                            "validation_error"
                        );
                    }

                    if (!CloseLoadedScene(scene))
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Failed to close loaded scene '{scenePath}', so it was not deleted.",
                            "scene_close_error"
                        );
                    }
                }

                // Remove from Build Settings
                RemoveSceneFromBuildSettings(scenePath);

                // Delete asset
                bool deleted = AssetDatabase.DeleteAsset(scenePath);
                AssetDatabase.Refresh();

                if (!deleted)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to delete scene at '{scenePath}'",
                        "delete_error"
                    );
                }

                McpLogger.LogInfo($"Deleted scene at path '{scenePath}' and removed from Build Settings");

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully deleted scene at path '{scenePath}' and removed from Build Settings" +
                        (discardedUnsavedChanges ? "; the loaded scene was closed and its unsaved changes were discarded" : ""),
                    ["scenePath"] = scenePath,
                    ["closedLoadedScene"] = wasLoaded,
                    ["discardedUnsavedChanges"] = discardedUnsavedChanges
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error deleting scene: {ex.Message}",
                    "scene_delete_error"
                );
            }
        }

        private void RemoveSceneFromBuildSettings(string scenePath)
        {
            var scenes = UnityEditor.EditorBuildSettings.scenes;
            var filtered = scenes.Where(s => s.path != scenePath).ToArray();
            if (filtered.Length != scenes.Length)
            {
                UnityEditor.EditorBuildSettings.scenes = filtered;
            }
        }
    }
}


