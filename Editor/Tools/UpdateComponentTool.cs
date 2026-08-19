using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Unity;
using McpUnity.Services;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using ComponentResolver = McpUnity.Utils.ComponentTypeResolver;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for updating component data in the Unity Editor
    /// </summary>
    public class UpdateComponentTool : McpToolBase
    {
        public UpdateComponentTool()
        {
            Name = "update_component";
            Description = "Updates component fields on a GameObject or adds it to the GameObject if it does not contain the component. Prefer passing componentData in the same call to avoid duplicate additions.";
        }
        
        /// <summary>
        /// Execute the UpdateComponent tool with the provided parameters synchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            JObject componentData = parameters["componentData"] as JObject;
            
            // Validate parameters - require either instanceId or objectPath
            if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'instanceId' or 'objectPath' must be provided", 
                    "validation_error"
                );
            }
            
            if (string.IsNullOrEmpty(componentName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'componentName' not provided", 
                    "validation_error"
                );
            }
            
            // Find the GameObject by instance ID or path
            GameObject gameObject = null;
            string identifier = "unknown";
            
            if (instanceId.HasValue)
            {
                gameObject = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifier = $"ID {instanceId.Value}";
            }
            else
            {
                identifier = $"path '{objectPath}'";

                // Prefer prefab contents when editing a prefab
                if (PrefabEditingService.IsEditing)
                {
                    gameObject = PrefabEditingService.FindByPath(objectPath);
                }
                // Fall back to scene hierarchy
                if (gameObject == null)
                {
                    gameObject = GameObject.Find(objectPath);
                }
                if (gameObject == null)
                {
                    gameObject = FindGameObjectByPath(objectPath);
                }
            }
                    
            if (gameObject == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject with path '{objectPath}' or instance ID {instanceId} not found", 
                    "not_found_error"
                );
            }
            
            McpLogger.LogInfo($"[MCP Unity] Updating component '{componentName}' on GameObject '{gameObject.name}' (found by {identifier})");
            
            // Resolve the component type first for reliable lookup
            Type componentType = ComponentResolver.FindComponentType(componentName);

            // Try to find the existing component using resolved Type (preferred) or string fallback
            // Use GetComponents (plural) to ensure we find all instances and take the first
            Component component = componentType != null
                ? gameObject.GetComponents(componentType).FirstOrDefault()
                : gameObject.GetComponent(componentName);

            // If component not found, try to add it
            if (component == null)
            {
                if (componentType == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Component type '{componentName}' not found in Unity",
                        "component_error"
                    );
                }

                // Defensive re-check to prevent duplicate additions (e.g., in batch operations)
                var existing = gameObject.GetComponents(componentType);
                if (existing.Length > 0)
                {
                    component = existing[0];
                }
                else
                {
                    component = Undo.AddComponent(gameObject, componentType);

                    // Ensure changes are saved
                    EditorUtility.SetDirty(gameObject);
                    if (PrefabUtility.IsPartOfAnyPrefab(gameObject))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }

                    McpLogger.LogInfo($"[MCP Unity] Added component '{componentName}' to GameObject '{gameObject.name}'");
                }
            }
            // Update component fields
            var updateWarnings = new List<string>();
            var updatedFields = new List<string>();
            var failedFields = new List<JObject>();
            if (componentData != null && componentData.Count > 0)
            {
                bool success = UpdateComponentData(
                    component,
                    componentData,
                    out string errorMessage,
                    out updateWarnings,
                    out updatedFields,
                    out failedFields);
                // If update failed, return error
                if (!success)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(errorMessage, "update_error");
                }

                // Ensure field changes are saved
                EditorUtility.SetDirty(gameObject);
                if (PrefabUtility.IsPartOfAnyPrefab(gameObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }

            // Create the response
            string message = $"Updated component '{componentName}' on GameObject '{gameObject.name}': " +
                $"{updatedFields.Count} field(s) succeeded, {failedFields.Count} field(s) failed";
            if (updateWarnings.Count > 0)
            {
                message += $" (with {updateWarnings.Count} warning(s))";
            }

            var response = new JObject
            {
                ["success"] = failedFields.Count == 0,
                ["type"] = "text",
                ["message"] = message,
                ["updatedFields"] = new JArray(updatedFields.ToArray()),
                ["failedFields"] = new JArray(failedFields.ToArray())
            };

            if (updateWarnings.Count > 0)
            {
                response["warnings"] = new JArray(updateWarnings.ToArray());
            }

            return response;
        }
        
        /// <summary>
        /// Find a GameObject by its hierarchy path
        /// </summary>
        /// <param name="path">The path to the GameObject (e.g. "Canvas/Panel/Button")</param>
        /// <returns>The GameObject if found, null otherwise</returns>
        private GameObject FindGameObjectByPath(string path)
        {
            // Split the path by '/'
            string[] pathParts = path.Split('/');
            GameObject[] rootGameObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            
            // If the path is empty, return null
            if (pathParts.Length == 0)
            {
                return null;
            }
            
            // Search through all root GameObjects in all scenes
            foreach (GameObject rootObj in rootGameObjects)
            {
                if (rootObj.name == pathParts[0])
                {
                    // Found the root object, now traverse down the path
                    GameObject current = rootObj;
                    
                    // Start from index 1 since we've already matched the root
                    for (int i = 1; i < pathParts.Length; i++)
                    {
                        Transform child = current.transform.Find(pathParts[i]);
                        if (child == null)
                        {
                            // Path segment not found
                            return null;
                        }
                        
                        // Move to the next level
                        current = child.gameObject;
                    }
                    
                    // If we got here, we found the full path
                    return current;
                }
            }
            
            // Not found
            return null;
        }
        
        /// <summary>
        /// Update component data based on the provided JObject
        /// </summary>
        /// <param name="component">The component to update</param>
        /// <param name="componentData">The data to apply to the component</param>
        /// <param name="errorMessage">Error message if update fails</param>
        /// <param name="warnings">List of non-fatal warnings</param>
        /// <param name="updatedFields">Fields that were written successfully</param>
        /// <param name="failedFields">Fields that could not be written</param>
        /// <returns>True if the component was updated successfully</returns>
        private bool UpdateComponentData(
            Component component,
            JObject componentData,
            out string errorMessage,
            out List<string> warnings,
            out List<string> updatedFields,
            out List<JObject> failedFields)
        {
            errorMessage = "";
            warnings = new List<string>();
            updatedFields = new List<string>();
            failedFields = new List<JObject>();

            if (component == null || componentData == null)
            {
                errorMessage = "Component or component data is null";
                return false;
            }

            Type componentType = component.GetType();
            // Record object for undo
            Undo.RecordObject(component, $"Update {componentType.Name} fields");

            // Process each field or property in the component data
            foreach (var property in componentData.Properties())
            {
                string fieldName = property.Name;
                JToken fieldValue = property.Value;

                if (string.IsNullOrEmpty(fieldName))
                {
                    warnings.Add("Ignored a component field with an empty name");
                    continue;
                }

                try
                {
                    // Try to update field
                    FieldInfo fieldInfo = componentType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (fieldInfo != null)
                    {
                        var conversionFailures = new List<string>();
                        object value = SerializedFieldConverter.ConvertJTokenToValue(
                            fieldValue, fieldInfo.FieldType, conversionFailures);
                        if (CannotAssignConvertedValue(value, fieldValue, fieldInfo.FieldType))
                        {
                            failedFields.Add(CreateFieldFailure(
                                fieldName,
                                GetConversionFailureReason(fieldInfo.FieldType, conversionFailures)));
                            continue;
                        }
                        fieldInfo.SetValue(component, value);
                        updatedFields.Add(fieldName);
                        continue;
                    }

                    // Try to update property if not found as a field
                    PropertyInfo propertyInfo = componentType.GetProperty(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (propertyInfo != null)
                    {
                        var conversionFailures = new List<string>();
                        object value = SerializedFieldConverter.ConvertJTokenToValue(
                            fieldValue, propertyInfo.PropertyType, conversionFailures);
                        if (CannotAssignConvertedValue(value, fieldValue, propertyInfo.PropertyType))
                        {
                            failedFields.Add(CreateFieldFailure(
                                fieldName,
                                GetConversionFailureReason(propertyInfo.PropertyType, conversionFailures)));
                            continue;
                        }
                        propertyInfo.SetValue(component, value);
                        updatedFields.Add(fieldName);
                        continue;
                    }

                    // Fallback: try SerializedProperty which handles both serialized names (m_Color)
                    // and property names (color) through Unity's serialization system
                    if (TrySetViaSerializedProperty(
                        component, fieldName, fieldValue, out bool propertyFound, out string failureReason))
                    {
                        updatedFields.Add(fieldName);
                        continue;
                    }

                    if (propertyFound)
                    {
                        failedFields.Add(CreateFieldFailure(fieldName, failureReason));
                    }
                    else
                    {
                        failedFields.Add(CreateFieldFailure(
                            fieldName,
                            $"Field '{fieldName}' was not found after checking reflection field, " +
                            $"reflection property, and SerializedProperty on component '{componentType.Name}'"));
                    }
                }
                catch (Exception ex)
                {
                    failedFields.Add(CreateFieldFailure(
                        fieldName, $"Exception while setting field '{fieldName}': {ex.Message}"));
                }
            }

            return true;
        }

        /// <summary>
        /// Try to set a field via Unity's SerializedProperty system.
        /// This handles both serialized names (m_Color, m_Sprite) and their property equivalents.
        /// </summary>
        private bool TrySetViaSerializedProperty(
            Component component,
            string fieldName,
            JToken fieldValue,
            out bool propertyFound,
            out string failureReason)
        {
            propertyFound = false;
            failureReason = null;
            var serializedObject = new SerializedObject(component);

            SerializedProperty prop = SerializedPropertyHelper.FindProperty(serializedObject, fieldName);
            if (prop == null)
            {
                return false;
            }
            propertyFound = true;
            string serializedFieldName = prop.propertyPath;

            var fieldWarnings = new List<string>();
            if (!SerializedPropertyHelper.SetValue(
                prop,
                fieldValue,
                fieldWarnings,
                fieldName,
                out SerializedPropertyHelper.ObjectReferenceWrite objectReferenceWrite))
            {
                failureReason = fieldWarnings.Count > 0
                    ? string.Join("; ", fieldWarnings.ToArray())
                    : $"Value could not be assigned to serialized field '{serializedFieldName}'";
                return false;
            }

            serializedObject.ApplyModifiedProperties();
            return SerializedPropertyHelper.VerifyObjectReferenceWrite(
                component,
                serializedObject,
                prop,
                serializedFieldName,
                objectReferenceWrite,
                out failureReason);
        }

        private static bool CannotAssignConvertedValue(object value, JToken token, Type targetType)
        {
            if (value != null)
            {
                return false;
            }

            return token.Type != JTokenType.Null
                || (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null);
        }

        private static string GetConversionFailureReason(Type targetType, List<string> conversionFailures)
        {
            return conversionFailures.Count > 0
                ? string.Join("; ", conversionFailures.ToArray())
                : $"Input value could not be converted to {targetType.Name}";
        }

        private static JObject CreateFieldFailure(string fieldName, string reason)
        {
            return new JObject
            {
                ["field"] = fieldName,
                ["reason"] = reason
            };
        }

    }
}
