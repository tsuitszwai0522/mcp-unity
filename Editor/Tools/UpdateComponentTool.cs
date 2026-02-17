using System;
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
                // Find by path
                gameObject = GameObject.Find(objectPath);
                identifier = $"path '{objectPath}'";

                if (gameObject == null)
                {
                    // Try to find using the Unity Scene hierarchy path
                    gameObject = FindGameObjectByPath(objectPath);
                }
                // Fallback: search in Prefab edit mode contents
                if (gameObject == null && PrefabEditingService.IsEditing)
                {
                    gameObject = PrefabEditingService.FindByPath(objectPath);
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
            if (componentData != null && componentData.Count > 0)
            {
                bool success = UpdateComponentData(component, componentData, out string errorMessage);
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
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully updated component '{componentName}' on GameObject '{gameObject.name}'"
            };
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
        /// <returns>True if the component was updated successfully</returns>
        private bool UpdateComponentData(Component component, JObject componentData, out string errorMessage)
        {
            errorMessage = "";
            
            if (component == null || componentData == null)
            {
                errorMessage = "Component or component data is null";
                return false;
            }

            Type componentType = component.GetType();
            bool fullSuccess = true;

            // Record object for undo
            Undo.RecordObject(component, $"Update {componentType.Name} fields");
            
            // Process each field or property in the component data
            foreach (var property in componentData.Properties())
            {
                string fieldName = property.Name;
                JToken fieldValue = property.Value;
                
                // Skip null values
                if (string.IsNullOrEmpty(fieldName) || fieldValue.Type == JTokenType.Null)
                {
                    continue;
                }
                
                // Try to update field
                FieldInfo fieldInfo = componentType.GetField(fieldName, 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                if (fieldInfo != null)
                {
                    object value = SerializedFieldConverter.ConvertJTokenToValue(fieldValue, fieldInfo.FieldType);
                    fieldInfo.SetValue(component, value);
                    continue;
                }
                
                // Try to update property if not found as a field
                PropertyInfo propertyInfo = componentType.GetProperty(fieldName, 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (propertyInfo != null)
                {
                    object value = SerializedFieldConverter.ConvertJTokenToValue(fieldValue, propertyInfo.PropertyType);
                    propertyInfo.SetValue(component, value);
                    continue;
                }
                
                fullSuccess = false;
                errorMessage = $"Field or Property  with name '{fieldName}' not found on component '{componentType.Name}'";
            }

            return fullSuccess;
        }
    }
}
