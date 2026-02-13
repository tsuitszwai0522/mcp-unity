using System;
using System.Linq;
using System.Reflection;
using McpUnity.Unity;
using McpUnity.Services;
using McpUnity.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
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
                    object value = ConvertJTokenToValue(fieldValue, fieldInfo.FieldType);
                    fieldInfo.SetValue(component, value);
                    continue;
                }
                
                // Try to update property if not found as a field
                PropertyInfo propertyInfo = componentType.GetProperty(fieldName, 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (propertyInfo != null)
                {
                    object value = ConvertJTokenToValue(fieldValue, propertyInfo.PropertyType);
                    propertyInfo.SetValue(component, value);
                    continue;
                }
                
                fullSuccess = false;
                errorMessage = $"Field or Property  with name '{fieldName}' not found on component '{componentType.Name}'";
            }

            return fullSuccess;
        }

        /// <summary>
        /// Convert a JToken to a value of the specified type
        /// </summary>
        /// <param name="token">The JToken to convert</param>
        /// <param name="targetType">The target type to convert to</param>
        /// <returns>The converted value</returns>
        private object ConvertJTokenToValue(JToken token, Type targetType)
        {
            if (token == null)
            {
                return null;
            }
            
            // Handle Unity Vector types
            if (targetType == typeof(Vector2) && token.Type == JTokenType.Object)
            {
                JObject vector = (JObject)token;
                return new Vector2(
                    vector["x"]?.ToObject<float>() ?? 0f,
                    vector["y"]?.ToObject<float>() ?? 0f
                );
            }
            
            if (targetType == typeof(Vector3) && token.Type == JTokenType.Object)
            {
                JObject vector = (JObject)token;
                return new Vector3(
                    vector["x"]?.ToObject<float>() ?? 0f,
                    vector["y"]?.ToObject<float>() ?? 0f,
                    vector["z"]?.ToObject<float>() ?? 0f
                );
            }
            
            if (targetType == typeof(Vector4) && token.Type == JTokenType.Object)
            {
                JObject vector = (JObject)token;
                return new Vector4(
                    vector["x"]?.ToObject<float>() ?? 0f,
                    vector["y"]?.ToObject<float>() ?? 0f,
                    vector["z"]?.ToObject<float>() ?? 0f,
                    vector["w"]?.ToObject<float>() ?? 0f
                );
            }
            
            if (targetType == typeof(Quaternion) && token.Type == JTokenType.Object)
            {
                JObject quaternion = (JObject)token;
                return new Quaternion(
                    quaternion["x"]?.ToObject<float>() ?? 0f,
                    quaternion["y"]?.ToObject<float>() ?? 0f,
                    quaternion["z"]?.ToObject<float>() ?? 0f,
                    quaternion["w"]?.ToObject<float>() ?? 1f
                );
            }
            
            if (targetType == typeof(Color) && token.Type == JTokenType.Object)
            {
                JObject color = (JObject)token;
                return new Color(
                    color["r"]?.ToObject<float>() ?? 0f,
                    color["g"]?.ToObject<float>() ?? 0f,
                    color["b"]?.ToObject<float>() ?? 0f,
                    color["a"]?.ToObject<float>() ?? 1f
                );
            }
            
            if (targetType == typeof(Bounds) && token.Type == JTokenType.Object)
            {
                JObject bounds = (JObject)token;
                Vector3 center = bounds["center"]?.ToObject<Vector3>() ?? Vector3.zero;
                Vector3 size = bounds["size"]?.ToObject<Vector3>() ?? Vector3.one;
                return new Bounds(center, size);
            }
            
            if (targetType == typeof(Rect) && token.Type == JTokenType.Object)
            {
                JObject rect = (JObject)token;
                return new Rect(
                    rect["x"]?.ToObject<float>() ?? 0f,
                    rect["y"]?.ToObject<float>() ?? 0f,
                    rect["width"]?.ToObject<float>() ?? 0f,
                    rect["height"]?.ToObject<float>() ?? 0f
                );
            }
            
            // Handle scene object references via instance ID (integer value)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Integer)
            {
                int id = token.ToObject<int>();
                UnityEngine.Object obj = EditorUtility.InstanceIDToObject(id);
                if (obj != null)
                {
                    // If target is GameObject, return directly or extract from component
                    if (targetType == typeof(GameObject))
                        return obj is GameObject go ? go : (obj is Component comp ? comp.gameObject : null);

                    // If target is a Component type, try to get it from the resolved object
                    if (typeof(Component).IsAssignableFrom(targetType))
                    {
                        if (targetType.IsAssignableFrom(obj.GetType()))
                            return obj;
                        if (obj is GameObject gameObj)
                            return gameObj.GetComponent(targetType);
                        if (obj is Component c)
                            return c.gameObject.GetComponent(targetType);
                    }

                    // For other UnityEngine.Object types, return if assignable
                    if (targetType.IsAssignableFrom(obj.GetType()))
                        return obj;
                }
                McpLogger.LogWarning($"[MCP Unity] Could not resolve instance ID {id} to type {targetType.Name}");
                return null;
            }

            // Handle scene object references via structured reference ({"instanceId": 123} or {"objectPath": "Path/To/Object"})
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Object)
            {
                JObject refObj = (JObject)token;
                // Skip if this looks like a Vector/Color/etc. (has x/y/z/r/g/b keys) — those are handled above
                if (refObj.ContainsKey("instanceId") || refObj.ContainsKey("objectPath"))
                {
                    UnityEngine.Object resolvedObj = null;

                    // 1) Try instanceId first
                    if (refObj["instanceId"] != null && refObj["instanceId"].Type != JTokenType.Null)
                    {
                        int id = refObj["instanceId"].ToObject<int>();
                        resolvedObj = EditorUtility.InstanceIDToObject(id);
                    }

                    // 2) Fallback to objectPath if instanceId failed or was not provided
                    if (resolvedObj == null && refObj["objectPath"] != null && refObj["objectPath"].Type != JTokenType.Null)
                    {
                        string objPath = refObj["objectPath"].ToObject<string>();
                        GameObject found = GameObject.Find(objPath);

                        // Search across all loaded scenes
                        if (found == null)
                        {
                            for (int i = 0; i < SceneManager.sceneCount && found == null; i++)
                            {
                                Scene scene = SceneManager.GetSceneAt(i);
                                if (!scene.isLoaded) continue;
                                foreach (GameObject root in scene.GetRootGameObjects())
                                {
                                    Transform t = root.transform.Find(objPath);
                                    if (t == null && root.name == objPath.Split('/')[0])
                                    {
                                        // Try relative path from root
                                        string relativePath = objPath.Contains("/")
                                            ? objPath.Substring(objPath.IndexOf('/') + 1)
                                            : null;
                                        if (relativePath != null)
                                            t = root.transform.Find(relativePath);
                                        else if (root.name == objPath)
                                            t = root.transform;
                                    }
                                    if (t != null)
                                    {
                                        found = t.gameObject;
                                        break;
                                    }
                                }
                            }
                        }
                        resolvedObj = found;
                    }

                    if (resolvedObj == null)
                    {
                        McpLogger.LogWarning($"[MCP Unity] Could not resolve scene object reference to type {targetType.Name}");
                        return null;
                    }

                    // Resolve to the requested target type
                    if (targetType == typeof(GameObject))
                        return resolvedObj is GameObject g ? g : (resolvedObj is Component c ? c.gameObject : null);

                    if (typeof(Component).IsAssignableFrom(targetType))
                    {
                        if (targetType.IsAssignableFrom(resolvedObj.GetType()))
                            return resolvedObj;
                        GameObject host = resolvedObj is GameObject go ? go : (resolvedObj as Component)?.gameObject;
                        return host?.GetComponent(targetType);
                    }

                    // Cover UnityEngine.Object base type and other assignable types
                    if (targetType.IsAssignableFrom(resolvedObj.GetType()))
                        return resolvedObj;

                    McpLogger.LogWarning($"[MCP Unity] Resolved object type {resolvedObj.GetType().Name} is not assignable to {targetType.Name}");
                    return null;
                }
            }

            // Handle UnityEngine.Object derived types (Sprite, Material, Font, AudioClip, etc.)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.String)
            {
                string assetRef = token.ToObject<string>();
                if (string.IsNullOrEmpty(assetRef))
                {
                    return null;
                }

                // Try as asset path first (e.g. "Assets/Sprites/tomato.png")
                var asset = AssetDatabase.LoadAssetAtPath(assetRef, targetType);
                if (asset != null)
                {
                    return asset;
                }

                // Try as GUID
                string guidPath = AssetDatabase.GUIDToAssetPath(assetRef);
                if (!string.IsNullOrEmpty(guidPath))
                {
                    asset = AssetDatabase.LoadAssetAtPath(guidPath, targetType);
                    if (asset != null)
                    {
                        return asset;
                    }
                }

                McpLogger.LogWarning($"[MCP Unity] Could not load asset of type {targetType.Name} from '{assetRef}'");
                return null;
            }
            
            // Handle enum types
            if (targetType.IsEnum)
            {
                // If JToken is a string, try to parse as enum name
                if (token.Type == JTokenType.String)
                {
                    string enumName = token.ToObject<string>();
                    if (Enum.TryParse(targetType, enumName, true, out object result))
                    {
                        return result;
                    }
                    
                    // If parsing fails, try to convert numeric value
                    if (int.TryParse(enumName, out int enumValue))
                    {
                        return Enum.ToObject(targetType, enumValue);
                    }
                }
                // If JToken is a number, convert directly to enum
                else if (token.Type == JTokenType.Integer)
                {
                    return Enum.ToObject(targetType, token.ToObject<int>());
                }
            }
            
            // For other types, use JToken's ToObject method
            try
            {
                return token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                McpLogger.LogError($"[MCP Unity] Error converting value to type {targetType.Name}: {ex.Message}");
                return null;
            }
        }
    }
}
