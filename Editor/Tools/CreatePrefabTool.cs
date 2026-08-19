using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for creating prefabs with optional MonoBehaviour scripts
    /// </summary>
    public class CreatePrefabTool : McpToolBase
    {
        public CreatePrefabTool()
        {
            Name = "create_prefab";
            Description = "Creates a prefab with optional MonoBehaviour script and serialized field values. Supports creating Prefab Variants by specifying a basePrefabPath.";
        }
        
        /// <summary>
        /// Execute the CreatePrefab tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string componentName = parameters["componentName"]?.ToObject<string>();
            string prefabName = parameters["prefabName"]?.ToObject<string>();
            string basePrefabPath = parameters["basePrefabPath"]?.ToObject<string>();
            JObject fieldValues = parameters["fieldValues"]?.ToObject<JObject>();

            // Validate required parameters
            if (string.IsNullOrEmpty(prefabName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'prefabName' not provided",
                    "validation_error"
                );
            }

            // Validate basePrefabPath if provided
            if (!string.IsNullOrEmpty(basePrefabPath))
            {
                var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
                if (baseAsset == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Base prefab not found at path '{basePrefabPath}'",
                        "validation_error"
                    );
                }
                if (!PrefabUtility.IsPartOfPrefabAsset(baseAsset))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Asset at '{basePrefabPath}' is not a prefab",
                        "validation_error"
                    );
                }
            }

            GameObject tempObject;

            if (!string.IsNullOrEmpty(basePrefabPath))
            {
                // Create Prefab Variant: instantiate base prefab (preserving prefab link)
                var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
                tempObject = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                tempObject.name = prefabName;
            }
            else
            {
                // Create a new empty GameObject
                tempObject = new GameObject(prefabName);
            }

            var updatedFields = new List<string>();
            var failedFields = new List<JObject>();
            var warnings = new List<string>();

            // Add component if provided
            if (!string.IsNullOrEmpty(componentName))
            {
                try
                {
                    // Add component
                    Component component = AddComponent(tempObject, componentName);

                    // Apply field values if provided and component exists
                    ApplyFieldValues(fieldValues, component, updatedFields, failedFields, warnings);
                }
                catch (Exception)
                {
                    UnityEngine.Object.DestroyImmediate(tempObject);
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to add component '{componentName}' to GameObject",
                        "component_error"
                    );
                }
            }

            // For safety, we'll create a unique name if prefab already exists
            int counter = 1;
            string prefabPath = $"{prefabName}.prefab";
            while (AssetDatabase.AssetPathToGUID(prefabPath) != "")
            {
                prefabPath = $"{prefabName}_{counter}.prefab";
                counter++;
            }

            // Create the prefab (SaveAsPrefabAsset automatically creates a Variant when the source has a prefab link)
            bool success = false;
            PrefabUtility.SaveAsPrefabAsset(tempObject, prefabPath, out success);

            // Clean up temporary object
            UnityEngine.Object.DestroyImmediate(tempObject);

            // Refresh the asset database
            AssetDatabase.Refresh();

            if (!success)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create prefab '{prefabName}' at path '{prefabPath}'",
                    "tool_execution_error"
                );
            }

            bool isVariant = !string.IsNullOrEmpty(basePrefabPath);
            string variantLabel = isVariant ? "Prefab Variant" : "prefab";

            // Log the action
            McpLogger.LogInfo($"Created {variantLabel} '{prefabName}' at path '{prefabPath}'" +
                (isVariant ? $" based on '{basePrefabPath}'" : $" from script '{componentName}'"));

            string message = $"Created {variantLabel} '{prefabName}' at path '{prefabPath}'" +
                (isVariant ? $" based on '{basePrefabPath}'" : "") +
                $": {updatedFields.Count} field(s) succeeded, {failedFields.Count} field(s) failed";
            if (warnings.Count > 0)
            {
                message += $" (with {warnings.Count} warning(s))";
            }

            // Create the response
            var response = new JObject
            {
                ["success"] = failedFields.Count == 0,
                ["type"] = "text",
                ["message"] = message,
                ["prefabPath"] = prefabPath,
                ["isVariant"] = isVariant,
                ["updatedFields"] = new JArray(updatedFields.ToArray()),
                ["failedFields"] = new JArray(failedFields.ToArray())
            };

            if (warnings.Count > 0)
            {
                response["warnings"] = new JArray(warnings.ToArray());
            }

            return response;
        }

        private Component AddComponent(GameObject gameObject, string componentName)
        {
            // Find the script type
            Type scriptType = Type.GetType($"{componentName}, Assembly-CSharp");
            if (scriptType == null)
            {
                // Try with just the class name
                scriptType = Type.GetType(componentName);
            }
                
            if (scriptType == null)
            {
                // Try to find the type using AppDomain
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    scriptType = assembly.GetType(componentName);
                    if (scriptType != null)
                        break;
                }
            }
                
            // Throw an error if the type was not found
            if (scriptType == null)
            {
                return null;
            }
                
            // Check if the type is a MonoBehaviour
            if (!typeof(MonoBehaviour).IsAssignableFrom(scriptType))
            {
                return null;
            }
            
            return gameObject.AddComponent(scriptType);
        }

        private void ApplyFieldValues(
            JObject fieldValues,
            Component component,
            List<string> updatedFields,
            List<JObject> failedFields,
            List<string> warnings)
        {
            // Apply field values if provided and component exists
            if (fieldValues == null || fieldValues.Count == 0)
            {
                return;
            }
            
            Undo.RecordObject(component, "Set field values");
                
            foreach (var property in fieldValues.Properties())
            {
                string fieldName = property.Name;
                JToken fieldValue = property.Value;

                try
                {
                    // Get the field/property info
                    var fieldInfo = component.GetType().GetField(fieldName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

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

                    // Try property
                    var propInfo = component.GetType().GetProperty(fieldName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    if (propInfo == null || !propInfo.CanWrite)
                    {
                        failedFields.Add(CreateFieldFailure(
                            fieldName,
                            $"Field '{fieldName}' was not found after checking reflection field and " +
                            $"writable reflection property on component '{component.GetType().Name}'"));
                        continue;
                    }

                    var propertyConversionFailures = new List<string>();
                    object propertyValue = SerializedFieldConverter.ConvertJTokenToValue(
                        fieldValue, propInfo.PropertyType, propertyConversionFailures);
                    if (CannotAssignConvertedValue(propertyValue, fieldValue, propInfo.PropertyType))
                    {
                        failedFields.Add(CreateFieldFailure(
                            fieldName,
                            GetConversionFailureReason(propInfo.PropertyType, propertyConversionFailures)));
                        continue;
                    }

                    propInfo.SetValue(component, propertyValue);
                    updatedFields.Add(fieldName);
                }
                catch (Exception ex)
                {
                    failedFields.Add(CreateFieldFailure(fieldName, $"Exception while setting field: {ex.Message}"));
                }
            }
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
