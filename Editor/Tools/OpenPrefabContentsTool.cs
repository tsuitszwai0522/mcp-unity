using System;
using UnityEngine;
using UnityEditor;
using McpUnity.Unity;
using McpUnity.Services;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for loading a Prefab's contents into an isolated editing environment.
    /// Uses PrefabUtility.LoadPrefabContents() to enable structural edits to Prefab assets.
    /// </summary>
    public class OpenPrefabContentsTool : McpToolBase
    {
        public OpenPrefabContentsTool()
        {
            Name = "open_prefab_contents";
            Description = "Loads a Prefab asset into an isolated editing environment using PrefabUtility.LoadPrefabContents(). " +
                          "While open, other tools (create_ui_element, reparent_gameobject, update_component, etc.) can modify the Prefab's internal structure. " +
                          "Call save_prefab_contents to save changes or discard them.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            string prefabPath = parameters["prefabPath"]?.ToObject<string>();

            if (string.IsNullOrEmpty(prefabPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'prefabPath' must be provided.",
                    "validation_error"
                );
            }

            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Path must end with '.prefab': '{prefabPath}'.",
                    "validation_error"
                );
            }

            // Verify the asset exists
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Prefab asset not found at path: '{prefabPath}'.",
                    "not_found_error"
                );
            }

            if (PrefabEditingService.IsEditing)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"A Prefab is already being edited: '{PrefabEditingService.AssetPath}'. " +
                    "Call save_prefab_contents first.",
                    "validation_error"
                );
            }

            try
            {
                GameObject root = PrefabEditingService.Open(prefabPath);

                // Build hierarchy info
                JArray children = BuildChildrenArray(root.transform);

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Opened Prefab contents for editing: '{prefabPath}'. " +
                                  "Use other tools to modify the Prefab structure, then call save_prefab_contents to save.",
                    ["prefabPath"] = prefabPath,
                    ["rootInstanceId"] = root.GetInstanceID(),
                    ["rootName"] = root.name,
                    ["children"] = children
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to open Prefab contents: {ex.Message}",
                    "internal_error"
                );
            }
        }

        private JArray BuildChildrenArray(Transform parent)
        {
            var children = new JArray();
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                var childObj = new JObject
                {
                    ["instanceId"] = child.gameObject.GetInstanceID(),
                    ["name"] = child.gameObject.name,
                    ["childCount"] = child.childCount
                };

                if (child.childCount > 0)
                {
                    childObj["children"] = BuildChildrenArray(child);
                }

                children.Add(childObj);
            }
            return children;
        }
    }
}
