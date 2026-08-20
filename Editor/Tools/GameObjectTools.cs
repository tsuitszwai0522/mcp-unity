using System;
using UnityEngine;
using UnityEditor;
using McpUnity.Unity;
using McpUnity.Services;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Utility class for common GameObject operations
    /// </summary>
    public static class GameObjectToolUtils
    {
        /// <summary>
        /// Find a GameObject by instance ID or hierarchy path
        /// </summary>
        /// <param name="instanceId">Optional instance ID</param>
        /// <param name="objectPath">Optional hierarchy path</param>
        /// <param name="gameObject">Output GameObject if found</param>
        /// <param name="identifierInfo">Description of how the object was identified</param>
        /// <returns>Error JObject if not found, null if successful</returns>
        public static JObject FindGameObject(int? instanceId, string objectPath, out GameObject gameObject, out string identifierInfo)
        {
            gameObject = null;
            identifierInfo = "";

            if (instanceId.HasValue)
            {
                identifierInfo = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                identifierInfo = $"path '{objectPath}'";
            }
            else
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'instanceId' or 'objectPath' must be provided.",
                    "validation_error"
                );
            }

            JObject scopeError = PrefabSessionScope.TryResolveGameObject(
                instanceId, objectPath, out gameObject);
            if (scopeError != null)
                return scopeError;

            if (gameObject == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject not found using {identifierInfo}.",
                    "not_found_error"
                );
            }

            return null; // Success
        }

        /// <summary>
        /// Get the full hierarchy path of a GameObject
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return null;
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }

        internal static JObject AddResolutionRole(JObject error, string role)
        {
            JToken message = error?["error"]?["message"];
            if (message != null)
                error["error"]["message"] = $"{role} resolution failed: {message}";
            return error;
        }
    }

    /// <summary>
    /// Tool for duplicating GameObjects in the Unity Editor
    /// </summary>
    public class DuplicateGameObjectTool : McpToolBase
    {
        public DuplicateGameObjectTool()
        {
            Name = "duplicate_gameobject";
            Description = "Duplicates a GameObject in the Unity scene. Can create multiple copies and optionally rename or reparent them.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string newName = parameters["newName"]?.ToObject<string>();
            string newParentPath = parameters["newParent"]?.ToObject<string>();
            int? newParentId = parameters["newParentId"]?.ToObject<int?>();
            int count = parameters["count"]?.ToObject<int?>() ?? 1;

            // Validate count
            if (count < 1 || count > 100)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Count must be between 1 and 100.",
                    "validation_error"
                );
            }

            // Find source GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject sourceObject, out string identifierInfo);
            if (error != null) return error;

            // Find new parent if specified
            GameObject newParent = null;
            if (newParentId.HasValue || !string.IsNullOrEmpty(newParentPath))
            {
                JObject parentError = GameObjectToolUtils.FindGameObject(
                    newParentId, newParentPath, out newParent, out _);
                if (parentError != null)
                    return GameObjectToolUtils.AddResolutionRole(parentError, "New parent");
            }

            // Create duplicates
            JArray duplicatedObjects = new JArray();

            for (int i = 0; i < count; i++)
            {
                GameObject duplicate = UnityEngine.Object.Instantiate(sourceObject);
                if (!PrefabSessionScope.HasActiveSession)
                    Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate {sourceObject.name}");

                // Set name
                if (!string.IsNullOrEmpty(newName))
                {
                    duplicate.name = count > 1 ? $"{newName} ({i + 1})" : newName;
                }
                else
                {
                    // Remove "(Clone)" suffix and optionally add number
                    string baseName = sourceObject.name;
                    duplicate.name = count > 1 ? $"{baseName} ({i + 1})" : baseName;
                }

                // Set parent
                Transform targetParent = newParent != null ? newParent.transform : sourceObject.transform.parent;
                if (targetParent != null)
                {
                    duplicate.transform.SetParent(targetParent, true);
                }

                duplicatedObjects.Add(new JObject
                {
                    ["instanceId"] = duplicate.GetInstanceID(),
                    ["name"] = duplicate.name,
                    ["path"] = GameObjectToolUtils.GetGameObjectPath(duplicate)
                });
            }

            EditorUtility.SetDirty(sourceObject.scene.GetRootGameObjects()[0]);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = count == 1
                    ? $"Successfully duplicated GameObject '{sourceObject.name}'."
                    : $"Successfully created {count} duplicates of GameObject '{sourceObject.name}'.",
                ["duplicatedObjects"] = duplicatedObjects
            };
        }
    }

    /// <summary>
    /// Tool for deleting GameObjects in the Unity Editor
    /// </summary>
    public class DeleteGameObjectTool : McpToolBase
    {
        public DeleteGameObjectTool()
        {
            Name = "delete_gameobject";
            Description = "Deletes a GameObject from the Unity scene. By default, also deletes all children.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? true;

            // Find target GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (error != null) return error;

            if (PrefabSessionScope.HasActiveSession)
            {
                GameObject prefabRoot = PrefabEditingService.PrefabRoot;
                bool destroysPrefabRoot = targetObject == prefabRoot
                    || (includeChildren
                        && prefabRoot.transform.IsChildOf(targetObject.transform));
                if (destroysPrefabRoot)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Cannot delete '{targetObject.name}' because the active Prefab contents " +
                        $"root '{prefabRoot.name}' is in the subtree that would be destroyed. " +
                        "Save or discard the Prefab editing session instead.",
                        "validation_error");
                }
            }

            string deletedName = targetObject.name;
            string deletedPath = GameObjectToolUtils.GetGameObjectPath(targetObject);
            int childCount = targetObject.transform.childCount;

            if (!includeChildren && childCount > 0)
            {
                // Move children to parent before deleting
                Transform parent = targetObject.transform.parent;
                Transform[] children = new Transform[childCount];

                for (int i = 0; i < childCount; i++)
                {
                    children[i] = targetObject.transform.GetChild(i);
                }

                foreach (Transform child in children)
                {
                    if (PrefabSessionScope.HasActiveSession)
                        child.SetParent(parent, true);
                    else
                        Undo.SetTransformParent(child, parent, "Reparent before delete");
                }
            }

            // Delete the GameObject
            if (PrefabSessionScope.HasActiveSession)
                UnityEngine.Object.DestroyImmediate(targetObject);
            else
                Undo.DestroyObjectImmediate(targetObject);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = includeChildren && childCount > 0
                    ? $"Successfully deleted GameObject '{deletedName}' and {childCount} children."
                    : $"Successfully deleted GameObject '{deletedName}'.",
                ["deletedPath"] = deletedPath,
                ["childrenPreserved"] = !includeChildren && childCount > 0 ? childCount : 0
            };
        }
    }

    /// <summary>
    /// Tool for changing the parent of GameObjects in the Unity Editor
    /// </summary>
    public class ReparentGameObjectTool : McpToolBase
    {
        public ReparentGameObjectTool()
        {
            Name = "reparent_gameobject";
            Description = "Changes the parent of a GameObject. Can move to a new parent or to the root level (null parent).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string newParentPath = parameters["newParent"]?.ToObject<string>();
            int? newParentId = parameters["newParentId"]?.ToObject<int?>();
            bool worldPositionStays = parameters["worldPositionStays"]?.ToObject<bool?>() ?? true;

            // Find target GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (error != null) return error;

            if (PrefabSessionScope.HasActiveSession)
            {
                GameObject prefabRoot = PrefabEditingService.PrefabRoot;
                if (targetObject == prefabRoot
                    || prefabRoot.transform.IsChildOf(targetObject.transform))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Cannot reparent the active Prefab contents root '{prefabRoot.name}' " +
                        "or an ancestor that contains it. Save or discard the Prefab editing " +
                        "session instead.",
                        "validation_error");
                }
            }

            string oldPath = GameObjectToolUtils.GetGameObjectPath(targetObject);
            Transform oldParent = targetObject.transform.parent;

            // Find new parent (null means root level)
            Transform newParentTransform = null;
            bool moveToRoot = false;

            // Check if explicitly moving to root (newParent is null or empty string)
            if (parameters["newParent"] != null && parameters["newParent"].Type == JTokenType.Null)
            {
                moveToRoot = true;
            }
            else if (newParentId.HasValue)
            {
                JObject parentError = GameObjectToolUtils.FindGameObject(
                    newParentId, null, out GameObject newParent, out _);
                if (parentError != null)
                    return GameObjectToolUtils.AddResolutionRole(parentError, "New parent");
                newParentTransform = newParent.transform;
            }
            else if (!string.IsNullOrEmpty(newParentPath))
            {
                JObject parentError = GameObjectToolUtils.FindGameObject(
                    null, newParentPath, out GameObject newParent, out _);
                if (parentError != null)
                    return GameObjectToolUtils.AddResolutionRole(parentError, "New parent");
                newParentTransform = newParent.transform;
            }
            else if (parameters["newParent"] == null && parameters["newParentId"] == null)
            {
                // Neither specified - move to root
                moveToRoot = true;
            }

            // A Prefab contents save serializes only the tree rooted at PrefabRoot. Detaching a
            // child to the preview-scene root level would create a second root that Save() then
            // silently discards when the preview scene unloads.
            if (PrefabSessionScope.HasActiveSession && newParentTransform == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot move GameObject '{targetObject.name}' to the preview-scene root " +
                    $"while Prefab contents '{PrefabEditingService.AssetPath}' are open. " +
                    "That would create a second preview root which is not serialized by " +
                    "SaveAsPrefabAsset. Reparent it under the active Prefab contents root instead.",
                    "validation_error");
            }

            // Prevent parenting to self or descendants
            if (newParentTransform != null)
            {
                if (newParentTransform == targetObject.transform)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Cannot parent a GameObject to itself.",
                        "validation_error"
                    );
                }

                if (newParentTransform.IsChildOf(targetObject.transform))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "Cannot parent a GameObject to one of its descendants.",
                        "validation_error"
                    );
                }
            }

            // Check if already at target parent
            if (moveToRoot && oldParent == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"GameObject '{targetObject.name}' is already at the root level.",
                    ["instanceId"] = targetObject.GetInstanceID(),
                    ["name"] = targetObject.name,
                    ["path"] = oldPath,
                    ["changed"] = false
                };
            }

            if (!moveToRoot && newParentTransform == oldParent)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"GameObject '{targetObject.name}' is already a child of the specified parent.",
                    ["instanceId"] = targetObject.GetInstanceID(),
                    ["name"] = targetObject.name,
                    ["path"] = oldPath,
                    ["changed"] = false
                };
            }

            // Perform reparenting
            // In prefab editing mode (LoadPrefabContents), use SetParent directly
            // because Undo.SetTransformParent can lose children in isolated prefab editing
            if (PrefabSessionScope.HasActiveSession)
            {
                targetObject.transform.SetParent(newParentTransform, worldPositionStays);
                if (!worldPositionStays)
                {
                    targetObject.transform.localPosition = Vector3.zero;
                    targetObject.transform.localRotation = Quaternion.identity;
                    targetObject.transform.localScale = Vector3.one;
                }
            }
            else
            {
                Undo.SetTransformParent(targetObject.transform, newParentTransform, "Reparent GameObject");
                if (!worldPositionStays)
                {
                    Undo.RecordObject(targetObject.transform, "Reset Local Position");
                    targetObject.transform.localPosition = Vector3.zero;
                    targetObject.transform.localRotation = Quaternion.identity;
                    targetObject.transform.localScale = Vector3.one;
                }
            }

            string newPath = GameObjectToolUtils.GetGameObjectPath(targetObject);
            string parentDescription = newParentTransform != null
                ? $"'{newParentTransform.gameObject.name}'"
                : "root level";

            EditorUtility.SetDirty(targetObject);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully reparented GameObject '{targetObject.name}' to {parentDescription}.",
                ["instanceId"] = targetObject.GetInstanceID(),
                ["name"] = targetObject.name,
                ["oldPath"] = oldPath,
                ["newPath"] = newPath,
                ["changed"] = true
            };
        }
    }

    /// <summary>
    /// Tool for setting the sibling index of a GameObject, controlling render/hierarchy order
    /// </summary>
    public class SetSiblingIndexTool : McpToolBase
    {
        public SetSiblingIndexTool()
        {
            Name = "set_sibling_index";
            Description = "Sets the sibling index of a GameObject, controlling its order among siblings. Affects UI rendering order (higher index = rendered on top).";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            int? siblingIndex = parameters["siblingIndex"]?.ToObject<int?>();

            // Find target GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject targetObject, out string identifierInfo);
            if (error != null) return error;

            if (!siblingIndex.HasValue)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'siblingIndex' not provided.",
                    "validation_error"
                );
            }

            int oldIndex = targetObject.transform.GetSiblingIndex();
            int siblingCount = targetObject.transform.parent != null
                ? targetObject.transform.parent.childCount
                : targetObject.scene.GetRootGameObjects().Length;

            Undo.RecordObject(targetObject.transform, "Set Sibling Index");
            targetObject.transform.SetSiblingIndex(siblingIndex.Value);

            int newIndex = targetObject.transform.GetSiblingIndex();
            EditorUtility.SetDirty(targetObject);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully set sibling index of '{targetObject.name}' from {oldIndex} to {newIndex} (of {siblingCount} siblings).",
                ["instanceId"] = targetObject.GetInstanceID(),
                ["name"] = targetObject.name,
                ["path"] = GameObjectToolUtils.GetGameObjectPath(targetObject),
                ["oldSiblingIndex"] = oldIndex,
                ["newSiblingIndex"] = newIndex,
                ["siblingCount"] = siblingCount
            };
        }
    }
}
