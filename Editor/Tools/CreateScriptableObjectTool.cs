using System;
using System.Linq;
using System.Reflection;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tools
{
    /// <summary>
    /// MCP Tool that creates a ScriptableObject asset in the Unity project
    /// </summary>
    public class CreateScriptableObjectTool : McpToolBase
    {
        public CreateScriptableObjectTool()
        {
            Name = "create_scriptable_object";
            Description = "Creates a ScriptableObject asset with optional field values and saves it to the project";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string typeName = parameters["typeName"]?.ToObject<string>();
            string savePath = parameters["savePath"]?.ToObject<string>();
            JObject fieldValues = parameters["fieldValues"] as JObject;

            // Validate required parameters
            if (string.IsNullOrEmpty(typeName))
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'typeName' not provided",
                    "validation_error"
                );
            }

            if (string.IsNullOrEmpty(savePath))
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'savePath' not provided",
                    "validation_error"
                );
            }

            // Ensure path starts with "Assets/"
            if (!savePath.StartsWith("Assets/"))
            {
                savePath = "Assets/" + savePath;
            }

            // Ensure path ends with ".asset"
            if (!savePath.EndsWith(".asset"))
            {
                savePath += ".asset";
            }

            // Find the ScriptableObject type
            Type scriptableObjectType = FindScriptableObjectType(typeName);
            if (scriptableObjectType == null)
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    $"ScriptableObject type '{typeName}' not found. Make sure the class exists and inherits from ScriptableObject.",
                    "validation_error"
                );
            }

            // Verify the type inherits from ScriptableObject
            if (!typeof(ScriptableObject).IsAssignableFrom(scriptableObjectType))
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    $"Type '{typeName}' does not inherit from ScriptableObject",
                    "validation_error"
                );
            }

            try
            {
                // Create the ScriptableObject instance
                ScriptableObject scriptableObject = ScriptableObject.CreateInstance(scriptableObjectType);

                if (scriptableObject == null)
                {
                    return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to create instance of ScriptableObject type '{typeName}'",
                        "tool_execution_error"
                    );
                }

                // Apply field values if provided
                if (fieldValues != null && fieldValues.Count > 0)
                {
                    Undo.RecordObject(scriptableObject, "Set ScriptableObject field values");
                    ApplyFieldValues(scriptableObject, fieldValues);
                }

                // Ensure the directory exists
                string directory = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
                {
                    CreateFolderRecursively(directory);
                }

                // Save the asset
                AssetDatabase.CreateAsset(scriptableObject, savePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Verify the asset was created
                ScriptableObject createdAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(savePath);
                if (createdAsset == null)
                {
                    return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to save ScriptableObject at path '{savePath}'",
                        "tool_execution_error"
                    );
                }

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully created ScriptableObject '{typeName}' at '{savePath}'",
                    ["assetPath"] = savePath,
                    ["typeName"] = scriptableObjectType.FullName
                };
            }
            catch (Exception ex)
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    $"Error creating ScriptableObject: {ex.Message}",
                    "tool_execution_error"
                );
            }
        }

        /// <summary>
        /// Finds a ScriptableObject type by name, searching all loaded assemblies
        /// </summary>
        private Type FindScriptableObjectType(string typeName)
        {
            // Try direct type lookup first
            Type type = Type.GetType(typeName);
            if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
            {
                return type;
            }

            // Search all loaded assemblies
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    // Try exact match first
                    type = assembly.GetType(typeName);
                    if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
                    {
                        return type;
                    }

                    // Try finding by class name only (without namespace)
                    type = assembly.GetTypes()
                        .FirstOrDefault(t =>
                            typeof(ScriptableObject).IsAssignableFrom(t) &&
                            !t.IsAbstract &&
                            (t.Name == typeName || t.FullName == typeName));

                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some assemblies may fail to load types, skip them
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies field values from a JObject to a ScriptableObject using reflection
        /// </summary>
        private void ApplyFieldValues(ScriptableObject scriptableObject, JObject fieldValues)
        {
            Type type = scriptableObject.GetType();

            foreach (var property in fieldValues.Properties())
            {
                string fieldName = property.Name;
                JToken value = property.Value;

                // Try to find a field (including private fields with [SerializeField])
                FieldInfo field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    try
                    {
                        object convertedValue = SerializedFieldConverter.ConvertJTokenToValue(value, field.FieldType);
                        if (convertedValue != null || !field.FieldType.IsValueType)
                        {
                            field.SetValue(scriptableObject, convertedValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MCP] Failed to set field '{fieldName}': {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[MCP] Field '{fieldName}' not found on type '{type.Name}'");
                }
            }

            EditorUtility.SetDirty(scriptableObject);
        }

        /// <summary>
        /// Creates a folder path recursively in the AssetDatabase
        /// </summary>
        private void CreateFolderRecursively(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string currentPath = parts[0]; // Should be "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string parentPath = currentPath;
                currentPath = currentPath + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(parentPath, parts[i]);
                }
            }
        }
    }
}
