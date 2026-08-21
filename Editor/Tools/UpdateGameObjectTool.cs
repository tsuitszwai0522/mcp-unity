using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using McpUnity.Utils; // For GameObjectHierarchyCreator and McpLogger
using McpUnity.Unity; // For McpUnitySocketHandler
using McpUnity.Services; // For PrefabEditingService
using Newtonsoft.Json.Linq; // For JObject

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for updating or creating a GameObject in the Unity Editor.
    /// Supports setting name, tag, layer, active state, and static state by instance ID or hierarchy path.
    /// Returns a JObject result similar to UpdateComponentTool for consistency.
    /// </summary>
    public class UpdateGameObjectTool : McpToolBase
    {
        private static readonly HashSet<string> SupportedGameObjectFields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "name",
                "tag",
                "layer",
                "activeSelf",
                "isActiveSelf",
                "isStatic",
                "static"
            };

        public UpdateGameObjectTool()
        {
            Name = "update_gameobject";
            Description = "Updates or creates a GameObject and its properties (name, tag, layer, active state, static state) based on instance ID or object path. Every supplied gameObjectData key is reported in updatedFields or failedFields; valid fields may still be applied when another field fails.";
            IsAsync = false; // Operations are expected to be quick
        }

        /// <summary>
        /// Executes the update or creation of a GameObject based on the provided parameters.
        /// </summary>
        /// <param name="parameters">A JObject containing: instanceId (int?), objectPath (string), name (string), tag (string), layer (int?), isActiveSelf (bool?), isStatic (bool?)</param>
        /// <returns>JObject with success, message, instanceId, name, and path fields (see UpdateComponentTool for format)</returns>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters from JObject
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            JObject gameObjectData = parameters["gameObjectData"] as JObject;

            GameObject targetGameObject = null;
            string identifierInfo = "";

            // Identify or create the GameObject by instanceId or objectPath
            if (instanceId.HasValue)
            {
                JObject scopeError = PrefabSessionScope.TryResolveGameObject(
                    instanceId, null, out targetGameObject);
                if (scopeError != null) return scopeError;
                identifierInfo = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                JObject createError = GameObjectHierarchyCreator.TryFindOrCreateHierarchicalGameObject(
                    objectPath, out targetGameObject);
                if (createError != null) return createError;
                identifierInfo = $"path '{objectPath}'";
            }
            else
            {
                // Neither instanceId nor objectPath was provided
                return McpUnitySocketHandler.CreateErrorResponse("Either 'instanceId' or 'objectPath' must be provided.", "validation_error");
            }

            // Check if we could not identify or create the GameObject
            if (targetGameObject == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse($"Target GameObject could not be identified or created using {identifierInfo}.", "unknown_error");
            }

            // Record for undo in Unity Editor
            Undo.RecordObject(targetGameObject, "Update GameObject Properties");
            bool propertiesUpdated = false;
            string originalNameForLog = targetGameObject.name;
            var updatedFields = new List<string>();
            var failedFields = new List<JObject>();
            var warnings = new List<string>();

            JProperty nameProperty = gameObjectData?.Property("name");
            if (nameProperty != null)
            {
                if (nameProperty.Value.Type != JTokenType.String
                    || string.IsNullOrEmpty(nameProperty.Value.ToObject<string>()))
                {
                    failedFields.Add(CreateFieldFailure(
                        "name", "Name must be a non-empty JSON string. Name was not changed."));
                }
                else
                {
                    string newName = nameProperty.Value.ToObject<string>();
                    if (targetGameObject.name != newName)
                    {
                        targetGameObject.name = newName;
                        propertiesUpdated = true;
                    }
                    updatedFields.Add("name");
                }
            }

            JProperty tagProperty = gameObjectData?.Property("tag");
            if (tagProperty != null)
            {
                string newTag = tagProperty.Value.Type == JTokenType.String
                    ? tagProperty.Value.ToObject<string>()
                    : null;
                bool tagExists = !string.IsNullOrEmpty(newTag)
                    && Array.Exists(
                        UnityEditorInternal.InternalEditorUtility.tags,
                        tag => tag.Equals(newTag));
                if (!tagExists)
                {
                    string reason = $"Tag '{newTag}' does not exist. Tag was not changed; " +
                        "create it in Unity's Tag Manager before retrying.";
                    failedFields.Add(CreateFieldFailure("tag", reason));
                    McpLogger.LogWarning(
                        $"UpdateGameObjectTool: {reason} GameObject: '{originalNameForLog}'.");
                }
                else
                {
                    if (!targetGameObject.CompareTag(newTag))
                    {
                        targetGameObject.tag = newTag;
                        propertiesUpdated = true;
                    }
                    updatedFields.Add("tag");
                }
            }

            JProperty layerProperty = gameObjectData?.Property("layer");
            if (layerProperty != null)
            {
                if (layerProperty.Value.Type != JTokenType.Integer)
                {
                    failedFields.Add(CreateFieldFailure(
                        "layer", "Layer must be a JSON integer between 0 and 31. Layer was not changed."));
                }
                else
                {
                    long newLayer = layerProperty.Value.ToObject<long>();
                    if (newLayer < 0 || newLayer > 31)
                    {
                        string reason = $"Layer value {newLayer} is outside the valid range 0-31. " +
                            "Layer was not changed.";
                        failedFields.Add(CreateFieldFailure("layer", reason));
                        McpLogger.LogWarning(
                            $"UpdateGameObjectTool: {reason} GameObject: '{originalNameForLog}'.");
                    }
                    else
                    {
                        if (targetGameObject.layer != (int)newLayer)
                        {
                            targetGameObject.layer = (int)newLayer;
                            propertiesUpdated = true;
                        }
                        updatedFields.Add("layer");
                    }
                }
            }

            JProperty activeSelfProperty = gameObjectData?.Property("activeSelf");
            JProperty legacyActiveSelfProperty = gameObjectData?.Property("isActiveSelf");
            propertiesUpdated |= ApplyBooleanField(
                activeSelfProperty,
                value => targetGameObject.activeSelf != value,
                value => targetGameObject.SetActive(value),
                updatedFields,
                failedFields);
            if (legacyActiveSelfProperty != null && activeSelfProperty != null)
            {
                failedFields.Add(CreateFieldFailure(
                    "isActiveSelf",
                    "Both 'activeSelf' and legacy alias 'isActiveSelf' were provided; use only 'activeSelf'."));
            }
            else
            {
                propertiesUpdated |= ApplyBooleanField(
                    legacyActiveSelfProperty,
                    value => targetGameObject.activeSelf != value,
                    value => targetGameObject.SetActive(value),
                    updatedFields,
                    failedFields);
            }

            JProperty isStaticProperty = gameObjectData?.Property("isStatic");
            JProperty legacyStaticProperty = gameObjectData?.Property("static");
            propertiesUpdated |= ApplyBooleanField(
                isStaticProperty,
                value => targetGameObject.isStatic != value,
                value => targetGameObject.isStatic = value,
                updatedFields,
                failedFields);
            if (legacyStaticProperty != null && isStaticProperty != null)
            {
                failedFields.Add(CreateFieldFailure(
                    "static",
                    "Both 'isStatic' and legacy alias 'static' were provided; use only 'isStatic'."));
            }
            else
            {
                propertiesUpdated |= ApplyBooleanField(
                    legacyStaticProperty,
                    value => targetGameObject.isStatic != value,
                    value => targetGameObject.isStatic = value,
                    updatedFields,
                    failedFields);
            }

            if (gameObjectData != null)
            {
                foreach (JProperty property in gameObjectData.Properties())
                {
                    if (!SupportedGameObjectFields.Contains(property.Name))
                    {
                        failedFields.Add(CreateFieldFailure(
                            property.Name,
                            $"Unknown GameObject field '{property.Name}'. Valid fields: " +
                            "name, tag, layer, activeSelf, isActiveSelf, isStatic, static."));
                    }
                }
            }

            // Mark as dirty if any property was changed
            if (propertiesUpdated)
            {
                EditorUtility.SetDirty(targetGameObject);
            }

            // Check if the GameObject is under a Canvas but lacks RectTransform
            if (targetGameObject.GetComponentInParent<Canvas>() != null
                && targetGameObject.GetComponent<RectTransform>() == null)
            {
                warnings.Add("This GameObject is under a Canvas but has no RectTransform. "
                    + "Use 'create_ui_element' for UI objects to get RectTransform automatically, "
                    + "or add a RectTransform via 'update_component'.");
            }

            // Compose result message and return as JObject (like UpdateComponentTool)
            string message = $"Updated GameObject '{targetGameObject.name}' (identified by {identifierInfo}): " +
                $"{updatedFields.Count} field(s) succeeded, {failedFields.Count} field(s) failed";
            if (warnings.Count > 0)
            {
                message += $" (with {warnings.Count} warning(s))";
            }

            return new JObject
            {
                ["success"] = failedFields.Count == 0,
                ["type"] = "text",
                ["message"] = message,
                ["instanceId"] = targetGameObject.GetInstanceID(),
                ["name"] = targetGameObject.name,
                ["path"] = GameObjectPathUtils.GetCanonicalPath(targetGameObject),
                ["updatedFields"] = new JArray(updatedFields.ToArray()),
                ["failedFields"] = new JArray(failedFields.ToArray()),
                ["warnings"] = new JArray(warnings.ToArray())
            };
        }

        private static JObject CreateFieldFailure(string fieldName, string reason)
        {
            return new JObject
            {
                ["field"] = fieldName,
                ["reason"] = reason
            };
        }

        private static bool ApplyBooleanField(
            JProperty property,
            Func<bool, bool> differsFromCurrent,
            Action<bool> apply,
            List<string> updatedFields,
            List<JObject> failedFields)
        {
            if (property == null)
                return false;

            if (property.Value.Type != JTokenType.Boolean)
            {
                failedFields.Add(CreateFieldFailure(
                    property.Name,
                    $"Field '{property.Name}' must be a JSON boolean. Field was not changed."));
                return false;
            }

            bool value = property.Value.ToObject<bool>();
            bool changed = differsFromCurrent(value);
            if (changed)
            {
                apply(value);
            }
            updatedFields.Add(property.Name);
            return changed;
        }

    }
}
