using System;
using System.Collections.Generic;
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
                var updatedFields = new List<string>();
                var failedFields = new List<JObject>();
                var warnings = new List<string>();
                ApplyFieldValues(scriptableObject, fieldValues, updatedFields, failedFields, warnings);

                if (updatedFields.Count > 0)
                {
                    EditorUtility.SetDirty(scriptableObject);
                    AssetDatabase.SaveAssets();
                }

                string message = $"Updated ScriptableObject at '{assetPath}' (type: {scriptableObject.GetType().Name}): " +
                    $"{updatedFields.Count} field(s) succeeded, {failedFields.Count} field(s) failed";
                if (warnings.Count > 0)
                {
                    message += $" (with {warnings.Count} warning(s))";
                }

                var response = new JObject
                {
                    ["success"] = failedFields.Count == 0,
                    ["type"] = "text",
                    ["message"] = message,
                    ["assetPath"] = assetPath,
                    ["typeName"] = scriptableObject.GetType().FullName,
                    ["updatedFields"] = new JArray(updatedFields.ToArray()),
                    ["failedFields"] = new JArray(failedFields.ToArray())
                };

                if (warnings.Count > 0)
                {
                    response["warnings"] = new JArray(warnings.ToArray());
                }

                return response;
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
        private void ApplyFieldValues(
            ScriptableObject scriptableObject,
            JObject fieldValues,
            List<string> updatedFields,
            List<JObject> failedFields,
            List<string> warnings)
        {
            Type type = scriptableObject.GetType();

            foreach (var property in fieldValues.Properties())
            {
                string fieldName = property.Name;
                JToken value = property.Value;

                try
                {
                    // Try to find a field (including private fields with [SerializeField])
                    FieldInfo field = type.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field == null)
                    {
                        failedFields.Add(CreateFieldFailure(
                            fieldName,
                            $"Field '{fieldName}' was not found after checking reflection field " +
                            $"on type '{type.Name}'"));
                        continue;
                    }

                    var conversionFailures = new List<string>();
                    object convertedValue = SerializedFieldConverter.ConvertJTokenToValue(
                        value,
                        field.FieldType,
                        SerializedFieldConverter.CloneClassSeed(field.GetValue(scriptableObject)),
                        conversionFailures,
                        warnings);
                    if (SerializedFieldConverter.CannotAssignConvertedValue(conversionFailures))
                    {
                        string reason = conversionFailures.Count > 0
                            ? string.Join("; ", conversionFailures.ToArray())
                            : $"Input value could not be converted to {field.FieldType.Name}";
                        failedFields.Add(CreateFieldFailure(fieldName, reason));
                        continue;
                    }

                    field.SetValue(scriptableObject, convertedValue);
                    updatedFields.Add(fieldName);
                }
                catch (Exception ex)
                {
                    string reason = $"Exception while setting field: {ex.Message}";
                    failedFields.Add(CreateFieldFailure(fieldName, reason));
                    Debug.LogWarning($"[MCP] Failed to set field '{fieldName}': {ex.Message}");
                }
            }
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
