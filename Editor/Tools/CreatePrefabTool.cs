using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    internal delegate bool AssetPathNormalizer(
        string assetPath,
        out string normalizedAssetPath,
        out string fullPath,
        out string errorMessage);

    /// <summary>
    /// Tool for creating prefabs with optional MonoBehaviour scripts
    /// </summary>
    public class CreatePrefabTool : McpToolBase
    {
        private static AssetPathNormalizer _normalizeUniquePrefabPath =
            AssetPathUtils.TryNormalizeAssetPath;

        public CreatePrefabTool()
        {
            Name = "create_prefab";
            Description = "Creates a prefab at an explicit path inside this project's Assets directory, " +
                          "with optional MonoBehaviour script and serialized field values. Existing asset " +
                          "names are changed to _1, _2, and so on; read-only targets fail without being changed. " +
                          "Supports Prefab Variants through basePrefabPath.";
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

            string requestedPrefabPath = $"{prefabName}.prefab";
            if (!AssetPathUtils.TryNormalizeAssetPath(
                    requestedPrefabPath,
                    out string normalizedPrefabPath,
                    out _,
                    out string pathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(pathError, "validation_error");
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

            GameObject tempObject = null;
            try
            {
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

                        if (component == null)
                        {
                            return McpUnitySocketHandler.CreateErrorResponse(
                                $"Component '{componentName}' could not be added: type not found or not a MonoBehaviour",
                                "component_error"
                            );
                        }

                        // Apply field values if provided and component exists
                        ApplyFieldValues(fieldValues, component, updatedFields, failedFields, warnings);
                    }
                    catch (Exception)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Failed to add component '{componentName}' to GameObject",
                            "component_error"
                        );
                    }
                }
                else if (fieldValues != null && fieldValues.Count > 0)
                {
                    foreach (JProperty property in fieldValues.Properties())
                    {
                        failedFields.Add(CreateFieldFailure(
                            property.Name,
                            "Cannot apply field values because no componentName was provided"));
                    }
                }

                if (failedFields.Count > 0)
                {
                    bool failedVariant = !string.IsNullOrEmpty(basePrefabPath);
                    var failedResponse = new JObject
                    {
                        ["success"] = false,
                        ["type"] = "text",
                        ["message"] =
                            $"Prefab '{prefabName}' was not created because " +
                            $"{failedFields.Count} field(s) failed; nothing was created.",
                        ["isVariant"] = failedVariant,
                        ["updatedFields"] = new JArray(updatedFields.ToArray()),
                        ["failedFields"] = new JArray(failedFields.ToArray())
                    };
                    if (warnings.Count > 0)
                    {
                        failedResponse["warnings"] = new JArray(warnings.ToArray());
                    }
                    return failedResponse;
                }

                // For safety, create a unique name if an imported asset already exists.
                int counter = 1;
                string prefabPath = normalizedPrefabPath;
                string prefabPathStem = normalizedPrefabPath.Substring(
                    0, normalizedPrefabPath.Length - ".prefab".Length);
                while (AssetDatabase.AssetPathToGUID(
                    prefabPath, AssetPathToGUIDOptions.OnlyExistingAssets) != "")
                {
                    prefabPath = $"{prefabPathStem}_{counter}.prefab";
                    counter++;
                }

                if (!_normalizeUniquePrefabPath(
                        prefabPath,
                        out string normalizedUniquePath,
                        out string fullPrefabPath,
                        out string uniquePathError))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        uniquePathError, "validation_error");
                }
                prefabPath = normalizedUniquePath;
                if (AssetPathUtils.IsExistingFileReadOnly(fullPrefabPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Cannot create prefab at '{prefabPath}' because the target file is read-only.",
                        "tool_execution_error");
                }

                // SaveAsPrefabAsset automatically creates a Variant when the source has a prefab link.
                bool targetExistedBefore = File.Exists(fullPrefabPath);
                bool metaExistedBefore = File.Exists(fullPrefabPath + ".meta");
                bool success = false;
                GameObject savedPrefab;
                try
                {
                    savedPrefab = PrefabUtility.SaveAsPrefabAsset(
                        tempObject, prefabPath, out success);
                }
                catch
                {
                    CleanupFailedNewAsset(
                        prefabPath,
                        fullPrefabPath,
                        targetExistedBefore,
                        metaExistedBefore);
                    throw;
                }

                if (!success || savedPrefab == null)
                {
                    CleanupFailedNewAsset(
                        prefabPath,
                        fullPrefabPath,
                        targetExistedBefore,
                        metaExistedBefore);
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to create prefab '{prefabName}' at path '{prefabPath}'",
                        "tool_execution_error"
                    );
                }

                string actualPrefabPath = AssetDatabase.GetAssetPath(savedPrefab);
                if (string.IsNullOrEmpty(actualPrefabPath))
                {
                    CleanupFailedNewAsset(
                        prefabPath,
                        fullPrefabPath,
                        targetExistedBefore,
                        metaExistedBefore);
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Prefab '{prefabName}' was saved but its asset path could not be read back.",
                        "tool_execution_error");
                }

                bool isVariant = !string.IsNullOrEmpty(basePrefabPath);
                string variantLabel = isVariant ? "Prefab Variant" : "prefab";

                McpLogger.LogInfo($"Created {variantLabel} '{prefabName}' at path '{actualPrefabPath}'" +
                    (isVariant ? $" based on '{basePrefabPath}'" : $" from script '{componentName}'"));

                string message = $"Created {variantLabel} '{prefabName}' at path '{actualPrefabPath}'" +
                    (isVariant ? $" based on '{basePrefabPath}'" : "") +
                    $": {updatedFields.Count} field(s) succeeded, {failedFields.Count} field(s) failed";
                if (warnings.Count > 0)
                {
                    message += $" (with {warnings.Count} warning(s))";
                }

                var response = new JObject
                {
                    ["success"] = failedFields.Count == 0,
                    ["type"] = "text",
                    ["message"] = message,
                    ["prefabPath"] = actualPrefabPath,
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
            finally
            {
                if (tempObject != null)
                {
                    Undo.DestroyObjectImmediate(tempObject);
                }
            }
        }

        private static void CleanupFailedNewAsset(
            string assetPath,
            string fullPath,
            bool targetExistedBefore,
            bool metaExistedBefore)
        {
            bool pathWasEntirelyNew = !targetExistedBefore && !metaExistedBefore;
            if (pathWasEntirelyNew)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            if (!targetExistedBefore && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            if (!metaExistedBefore && File.Exists(fullPath + ".meta"))
            {
                File.Delete(fullPath + ".meta");
            }
            if (pathWasEntirelyNew)
            {
                AssetDatabase.Refresh();
            }
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
            if (component == null)
            {
                return;
            }

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
                            fieldValue,
                            fieldInfo.FieldType,
                            SerializedFieldConverter.CloneClassSeed(fieldInfo.GetValue(component)),
                            conversionFailures,
                            warnings,
                            component);
                        if (SerializedFieldConverter.CannotAssignConvertedValue(conversionFailures))
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
                        fieldValue,
                        propInfo.PropertyType,
                        SerializedFieldConverter.CloneClassSeed(
                            SerializedFieldConverter.GetSafePropertySeed(propInfo, component)),
                        propertyConversionFailures,
                        warnings,
                        component);
                    if (SerializedFieldConverter.CannotAssignConvertedValue(propertyConversionFailures))
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
