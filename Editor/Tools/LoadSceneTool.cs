using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for loading a Unity scene, optionally additively
    /// </summary>
    public class LoadSceneTool : McpToolBase
    {
        public LoadSceneTool()
        {
            Name = "load_scene";
            Description = "Loads a scene by path or name. Supports additive loading (default: false). " +
                "A non-additive load first saves every dirty open scene (reported in savedScenes; any that stay dirty are " +
                "replaced without saving and reported in discardedScenes). It is refused with untitled_dirty_scene when an " +
                "untitled scene has unsaved changes, because saving it would open a blocking Save Scene dialog";
        }

        // Seams so the save and open side effects can be tested without replacing the test runner's scene.
        internal static Func<IReadOnlyList<DirtySceneInfo>> DescribeDirtyScenes = SceneDirtyState.DescribeDirtyLoadedScenes;
        internal static Func<bool> SaveOpenScenes = UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes;
        internal static Action<string, UnityEditor.SceneManagement.OpenSceneMode> OpenScene =
            (path, mode) => UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, mode);

        /// <summary>
        /// Execute the LoadScene tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            string scenePath = parameters["scenePath"]?.ToObject<string>();
            string sceneName = parameters["sceneName"]?.ToObject<string>();
            string folderPath = parameters["folderPath"]?.ToObject<string>();
            bool additive = parameters["additive"]?.ToObject<bool?>() ?? false;

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
                // Avoid any save prompts: save open scenes before replacing them (non-additive).
                // This can persist unsaved changes that belong to someone else, and a scene that stays
                // dirty is then replaced without saving, so report both lists instead of doing it silently.
                var savedScenes = new List<DirtySceneInfo>();
                var discardedScenes = new List<DirtySceneInfo>();
                if (!additive)
                {
                    IReadOnlyList<DirtySceneInfo> dirtyBefore = DescribeDirtyScenes();
                    // Saving an untitled scene opens a modal Save Scene file dialog that blocks the Editor
                    // main thread, so refuse before anything is saved or opened.
                    List<DirtySceneInfo> untitled = dirtyBefore.Where(scene => scene.IsUntitled).ToList();
                    if (untitled.Count > 0)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"{untitled.Count} untitled scene(s) have unsaved changes: {string.Join(", ", untitled.Select(s => s.Describe()))}. " +
                            "A non-additive load saves open scenes first, and saving an untitled scene opens a Save Scene file dialog " +
                            "that blocks the Editor until someone answers. Nothing was saved or loaded. Save the scene to a path " +
                            "(save_scene with scenePath) or discard its changes deliberately, or load additively, then retry.",
                            "untitled_dirty_scene"
                        );
                    }

                    SaveOpenScenes();
                    var stillDirty = new HashSet<string>(DescribeDirtyScenes().Select(scene => scene.Path));
                    foreach (DirtySceneInfo scene in dirtyBefore)
                    {
                        (stillDirty.Contains(scene.Path) ? discardedScenes : savedScenes).Add(scene);
                    }
                }

                var mode = additive
                    ? UnityEditor.SceneManagement.OpenSceneMode.Additive
                    : UnityEditor.SceneManagement.OpenSceneMode.Single;

                OpenScene(scenePath, mode);

                // For non-additive, scene becomes active automatically. For additive, we do not change active scene.

                McpLogger.LogInfo($"Loaded scene at path '{scenePath}' (additive={additive})");

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully loaded scene at path '{scenePath}' (additive={additive.ToString().ToLower()})" +
                        (savedScenes.Count > 0
                            ? $"; saved {savedScenes.Count} dirty scene(s) before switching: {string.Join(", ", savedScenes.Select(s => s.Describe()))}"
                            : "") +
                        (discardedScenes.Count > 0
                            ? $"; {discardedScenes.Count} dirty scene(s) could not be saved and were replaced without saving: {string.Join(", ", discardedScenes.Select(s => s.Describe()))}"
                            : ""),
                    ["scenePath"] = scenePath,
                    ["additive"] = additive,
                    ["savedScenes"] = SceneDirtyState.ToJson(savedScenes),
                    ["discardedScenes"] = SceneDirtyState.ToJson(discardedScenes)
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error loading scene: {ex.Message}",
                    "scene_load_error"
                );
            }
        }
    }
}


