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
    /// MCP Tool that updates field values on an existing ScriptableObject asset
    /// </summary>
    public class UpdateScriptableObjectTool : McpToolBase
    {
        public UpdateScriptableObjectTool()
        {
            Name = "update_scriptable_object";
            Description = "Updates field values on an existing ScriptableObject asset in the project";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            JObject fieldValues = parameters["fieldValues"] as JObject;

            // Validate required parameters
            if (string.IsNullOrEmpty(assetPath))
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'assetPath' not provided",
                    "validation_error"
                );
            }

            if (fieldValues == null || fieldValues.Count == 0)
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'fieldValues' not provided or empty",
                    "validation_error"
                );
            }

            // Ensure path starts with "Assets/"
            if (!assetPath.StartsWith("Assets/"))
            {
                assetPath = "Assets/" + assetPath;
            }

            try
            {
                // Load the existing ScriptableObject
                ScriptableObject scriptableObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (scriptableObject == null)
                {
                    return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                        $"No ScriptableObject found at path '{assetPath}'",
                        "validation_error"
                    );
                }

                // Apply field values
                Undo.RecordObject(scriptableObject, "Update ScriptableObject field values");
                ApplyFieldValues(scriptableObject, fieldValues);

                // Save changes
                AssetDatabase.SaveAssets();

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Successfully updated ScriptableObject at '{assetPath}' (type: {scriptableObject.GetType().Name})",
                    ["assetPath"] = assetPath,
                    ["typeName"] = scriptableObject.GetType().FullName
                };
            }
            catch (Exception ex)
            {
                return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                    $"Error updating ScriptableObject: {ex.Message}",
                    "tool_execution_error"
                );
            }
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
    }
}
