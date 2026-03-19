using System;
using System.Collections.Generic;
using McpUnity.Unity;
using McpUnity.Services;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using ComponentResolver = McpUnity.Utils.ComponentTypeResolver;
using System.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for reading serialized fields from a component using Unity's SerializedObject API.
    /// More reliable than reflection for accessing Unity-serialized data (handles m_Color, m_Sprite, etc.)
    /// </summary>
    public class ReadSerializedFieldsTool : McpToolBase
    {
        public ReadSerializedFieldsTool()
        {
            Name = "read_serialized_fields";
            Description = "Reads serialized fields from a component using Unity's SerializedProperty API. Supports both serialized names (m_Color) and property names (color). Returns field names, types, and current values.";
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            JArray fieldNames = parameters["fieldNames"] as JArray;

            // Find the GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject gameObject, out string identifierInfo);
            if (error != null) return error;

            if (string.IsNullOrEmpty(componentName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'componentName' not provided",
                    "validation_error"
                );
            }

            // Resolve component
            Type componentType = ComponentResolver.FindComponentType(componentName);
            Component component = componentType != null
                ? gameObject.GetComponents(componentType).FirstOrDefault()
                : gameObject.GetComponent(componentName);

            if (component == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Component '{componentName}' not found on GameObject '{gameObject.name}'",
                    "not_found_error"
                );
            }

            var serializedObject = new SerializedObject(component);
            var fields = new JObject();

            if (fieldNames != null && fieldNames.Count > 0)
            {
                // Read specific fields
                foreach (var fieldNameToken in fieldNames)
                {
                    string fieldName = fieldNameToken.ToObject<string>();
                    SerializedProperty prop = SerializedPropertyHelper.FindProperty(serializedObject, fieldName);
                    if (prop != null)
                    {
                        fields[prop.name] = SerializedPropertyToJToken(prop);
                    }
                    else
                    {
                        fields[fieldName] = JValue.CreateNull();
                    }
                }
            }
            else
            {
                // Read all visible serialized fields
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    // Skip the script reference
                    if (iterator.name == "m_Script") continue;
                    fields[iterator.name] = SerializedPropertyToJToken(iterator);
                }
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Read {fields.Count} fields from '{componentName}' on '{gameObject.name}'",
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = componentName,
                ["fields"] = fields
            };
        }

        private JToken SerializedPropertyToJToken(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return prop.intValue;
                case SerializedPropertyType.Boolean:
                    return prop.boolValue;
                case SerializedPropertyType.Float:
                    return prop.floatValue;
                case SerializedPropertyType.String:
                    return prop.stringValue;
                case SerializedPropertyType.Color:
                    Color c = prop.colorValue;
                    return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case SerializedPropertyType.Vector2:
                    Vector2 v2 = prop.vector2Value;
                    return new JObject { ["x"] = v2.x, ["y"] = v2.y };
                case SerializedPropertyType.Vector3:
                    Vector3 v3 = prop.vector3Value;
                    return new JObject { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
                case SerializedPropertyType.Vector4:
                    Vector4 v4 = prop.vector4Value;
                    return new JObject { ["x"] = v4.x, ["y"] = v4.y, ["z"] = v4.z, ["w"] = v4.w };
                case SerializedPropertyType.Rect:
                    Rect r = prop.rectValue;
                    return new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };
                case SerializedPropertyType.Enum:
#pragma warning disable CS0618 // enumNames is obsolete but no non-obsolete API returns internal C# enum names
                    string[] enumNames = prop.enumNames;
#pragma warning restore CS0618
                    return new JObject
                    {
                        ["value"] = prop.enumValueIndex,
                        ["name"] = enumNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < enumNames.Length
                            ? enumNames[prop.enumValueIndex]
                            : prop.enumValueIndex.ToString()
                    };
                case SerializedPropertyType.ObjectReference:
                    if (prop.objectReferenceValue != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(prop.objectReferenceValue);
                        return new JObject
                        {
                            ["instanceId"] = prop.objectReferenceValue.GetInstanceID(),
                            ["name"] = prop.objectReferenceValue.name,
                            ["type"] = prop.objectReferenceValue.GetType().Name,
                            ["assetPath"] = string.IsNullOrEmpty(assetPath) ? null : assetPath
                        };
                    }
                    return JValue.CreateNull();
                case SerializedPropertyType.Bounds:
                    Bounds b = prop.boundsValue;
                    return new JObject
                    {
                        ["center"] = new JObject { ["x"] = b.center.x, ["y"] = b.center.y, ["z"] = b.center.z },
                        ["size"] = new JObject { ["x"] = b.size.x, ["y"] = b.size.y, ["z"] = b.size.z }
                    };
                case SerializedPropertyType.Quaternion:
                    Quaternion q = prop.quaternionValue;
                    return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
                case SerializedPropertyType.LayerMask:
                    return prop.intValue;
                case SerializedPropertyType.ArraySize:
                    return prop.intValue;
                default:
                    return new JObject
                    {
                        ["_type"] = prop.propertyType.ToString(),
                        ["_info"] = "Unsupported property type for direct reading"
                    };
            }
        }
    }

    /// <summary>
    /// Tool for writing serialized fields on a component using Unity's SerializedObject API.
    /// Handles both serialized names (m_Color) and property names (color).
    /// </summary>
    public class WriteSerializedFieldsTool : McpToolBase
    {
        public WriteSerializedFieldsTool()
        {
            Name = "write_serialized_fields";
            Description = "Writes serialized fields on a component using Unity's SerializedProperty API. Accepts both serialized names (m_Color, m_Sprite) and property names (color, sprite). More reliable than update_component for Unity built-in component fields.";
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            JObject fieldData = parameters["fieldData"] as JObject;

            // Find the GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject gameObject, out string identifierInfo);
            if (error != null) return error;

            if (string.IsNullOrEmpty(componentName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'componentName' not provided",
                    "validation_error"
                );
            }

            if (fieldData == null || fieldData.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'fieldData' not provided or empty",
                    "validation_error"
                );
            }

            // Resolve component
            Type componentType = ComponentResolver.FindComponentType(componentName);
            Component component = componentType != null
                ? gameObject.GetComponents(componentType).FirstOrDefault()
                : gameObject.GetComponent(componentName);

            if (component == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Component '{componentName}' not found on GameObject '{gameObject.name}'",
                    "not_found_error"
                );
            }

            var serializedObject = new SerializedObject(component);
            var updatedFields = new List<string>();
            var warnings = new List<string>();

            foreach (var property in fieldData.Properties())
            {
                string fieldName = property.Name;
                JToken fieldValue = property.Value;

                SerializedProperty prop = SerializedPropertyHelper.FindProperty(serializedObject, fieldName);
                if (prop == null)
                {
                    warnings.Add($"Field '{fieldName}' not found on '{componentName}'");
                    continue;
                }

                if (SerializedPropertyHelper.SetValue(prop, fieldValue, warnings, fieldName))
                {
                    updatedFields.Add(prop.name);
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(gameObject);

            string message = $"Updated {updatedFields.Count} field(s) on '{componentName}' on '{gameObject.name}'";
            if (warnings.Count > 0)
            {
                message += $" (with {warnings.Count} warning(s))";
            }

            var response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = message,
                ["instanceId"] = gameObject.GetInstanceID(),
                ["updatedFields"] = new JArray(updatedFields.ToArray())
            };

            if (warnings.Count > 0)
            {
                response["warnings"] = new JArray(warnings.ToArray());
            }

            return response;
        }

    }
}
