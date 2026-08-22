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
        internal const int DefaultMaxDepth = 8;
        internal const int MaximumMaxDepth = 8;
        internal const int DefaultMaxElements = 100;
        internal const int MaximumMaxElements = 100;

        public ReadSerializedFieldsTool()
        {
            Name = "read_serialized_fields";
            Description = "Reads serialized fields from a component using Unity's SerializedProperty API. " +
                "Supports both serialized names (m_Color) and property names (color). Returns field names, " +
                "types, and current values. For enums, 'value' is the underlying enum value and 'index' " +
                "is the enumValueIndex. Generic fields and arrays are expanded recursively up to maxDepth " +
                $"(default {DefaultMaxDepth}, range 0-{MaximumMaxDepth}); lower maxDepth to reduce payload size. " +
                $"maxElements is one global returned-element budget across all arrays (default {DefaultMaxElements}, " +
                $"range 0-{MaximumMaxElements}); arrayMetadata and the scalar message report totals and truncation causes. " +
                "Ambiguous short or partial component names require exactly one " +
                "exact candidate type on the target; otherwise use a fully-qualified name.";
        }

        public override JObject Execute(JObject parameters)
        {
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            JArray fieldNames = parameters["fieldNames"] as JArray;
            int maxDepth = parameters["maxDepth"]?.ToObject<int?>() ?? DefaultMaxDepth;
            int maxElements = parameters["maxElements"]?.ToObject<int?>() ?? DefaultMaxElements;

            if (maxDepth < 0 || maxDepth > MaximumMaxDepth)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter 'maxDepth' must be between 0 and {MaximumMaxDepth}",
                    "validation_error"
                );
            }

            if (maxElements < 0 || maxElements > MaximumMaxElements)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter 'maxElements' must be between 0 and {MaximumMaxElements}",
                    "validation_error"
                );
            }

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
            Type componentType = ComponentResolver.FindComponentType(
                componentName,
                gameObject,
                out string resolutionWarning,
                out string ambiguityError);
            if (!string.IsNullOrEmpty(ambiguityError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    ambiguityError,
                    "component_ambiguity_error");
            }
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
            var arrayMetadata = new JObject();
            var elementBudget = new ArrayElementBudget(maxElements);

            if (fieldNames != null && fieldNames.Count > 0)
            {
                // Read specific fields
                foreach (var fieldNameToken in fieldNames)
                {
                    string fieldName = fieldNameToken.ToObject<string>();
                    SerializedProperty prop = SerializedPropertyHelper.FindProperty(serializedObject, fieldName);
                    if (prop != null)
                    {
                        fields[prop.name] = SerializeProperty(
                            prop,
                            0,
                            maxDepth,
                            arrayMetadata,
                            elementBudget);
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
                    fields[iterator.name] = SerializeProperty(
                        iterator,
                        0,
                        maxDepth,
                        arrayMetadata,
                        elementBudget);
                }
            }

            var response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = BuildReadMessage(
                    fields.Count,
                    componentName,
                    gameObject.name,
                    maxElements,
                    arrayMetadata),
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = componentName,
                ["fields"] = fields,
                ["maxDepth"] = maxDepth,
                ["maxElements"] = maxElements,
                ["arrayMetadata"] = arrayMetadata
            };

            if (!string.IsNullOrEmpty(resolutionWarning))
            {
                response["warnings"] = new JArray(resolutionWarning);
            }

            return response;
        }

        internal static JToken SerializedPropertyToJToken(
            SerializedProperty prop,
            int currentDepth = 0,
            int maxDepth = DefaultMaxDepth,
            int maxElements = DefaultMaxElements,
            JObject arrayMetadata = null)
        {
            return SerializeProperty(
                prop,
                currentDepth,
                maxDepth,
                arrayMetadata,
                new ArrayElementBudget(maxElements));
        }

        private static JToken SerializeProperty(
            SerializedProperty prop,
            int currentDepth,
            int maxDepth,
            JObject arrayMetadata,
            ArrayElementBudget elementBudget)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Generic:
                    return SerializeGenericProperty(
                        prop,
                        currentDepth,
                        maxDepth,
                        arrayMetadata,
                        elementBudget);
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
                        ["value"] = prop.intValue,
                        ["index"] = prop.enumValueIndex,
                        ["name"] = enumNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < enumNames.Length
                            ? enumNames[prop.enumValueIndex]
                            : prop.intValue.ToString()
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

        private static JToken SerializeGenericProperty(
            SerializedProperty prop,
            int currentDepth,
            int maxDepth,
            JObject arrayMetadata,
            ArrayElementBudget elementBudget)
        {
            if (prop.isArray)
            {
                return SerializeArrayProperty(
                    prop,
                    currentDepth,
                    maxDepth,
                    arrayMetadata,
                    elementBudget);
            }

            if (currentDepth >= maxDepth)
            {
                return new JObject
                {
                    ["_type"] = prop.type,
                    ["_truncated"] = true,
                    ["_info"] = $"Maximum serialized-property depth {maxDepth} reached"
                };
            }

            var children = new JObject();
            SerializedProperty iterator = prop.Copy();
            SerializedProperty end = prop.GetEndProperty(true);
            bool enterChildren = true;
            while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                children[iterator.name] = SerializeProperty(
                    iterator,
                    currentDepth + 1,
                    maxDepth,
                    arrayMetadata,
                    elementBudget);
            }
            return children;
        }

        private static JArray SerializeArrayProperty(
            SerializedProperty prop,
            int currentDepth,
            int maxDepth,
            JObject arrayMetadata,
            ArrayElementBudget elementBudget)
        {
            var elements = new JArray();
            int total = prop.arraySize;
            bool depthTruncated = currentDepth >= maxDepth && total > 0;

            if (!depthTruncated)
            {
                for (int i = 0; i < total && elementBudget.TryConsume(); i++)
                {
                    SerializedProperty element = prop.GetArrayElementAtIndex(i);
                    elements.Add(SerializeProperty(
                        element,
                        currentDepth + 1,
                        maxDepth,
                        arrayMetadata,
                        elementBudget));
                }
            }

            int returned = elements.Count;
            bool budgetTruncated = !depthTruncated && returned < total;
            if (arrayMetadata != null)
            {
                arrayMetadata[prop.propertyPath] = new JObject
                {
                    ["total"] = total,
                    ["returned"] = returned,
                    ["truncated"] = returned < total,
                    ["depthTruncated"] = depthTruncated,
                    ["budgetTruncated"] = budgetTruncated
                };
            }
            return elements;
        }

        private static string BuildReadMessage(
            int fieldCount,
            string componentName,
            string gameObjectName,
            int maxElements,
            JObject arrayMetadata)
        {
            string message = $"Read {fieldCount} fields from '{componentName}' on '{gameObjectName}'";
            if (arrayMetadata.Count == 0)
            {
                return message;
            }

            long total = 0;
            long returned = 0;
            int truncated = 0;
            int depthTruncated = 0;
            int budgetTruncated = 0;
            foreach (JProperty metadataProperty in arrayMetadata.Properties())
            {
                JObject metadata = metadataProperty.Value as JObject;
                if (metadata == null)
                {
                    continue;
                }

                total += metadata["total"]?.ToObject<long>() ?? 0;
                returned += metadata["returned"]?.ToObject<long>() ?? 0;
                if (metadata["truncated"]?.ToObject<bool>() == true)
                {
                    truncated++;
                }
                if (metadata["depthTruncated"]?.ToObject<bool>() == true)
                {
                    depthTruncated++;
                }
                if (metadata["budgetTruncated"]?.ToObject<bool>() == true)
                {
                    budgetTruncated++;
                }
            }

            return message + $". Array traversal returned {returned} of {total} visited element slots " +
                $"across {arrayMetadata.Count} array(s) under global maxElements={maxElements}; " +
                $"truncated arrays={truncated} (depth={depthTruncated}, budget={budgetTruncated}).";
        }

        private sealed class ArrayElementBudget
        {
            private int _remaining;

            public ArrayElementBudget(int limit)
            {
                _remaining = Math.Max(0, limit);
            }

            public bool TryConsume()
            {
                if (_remaining <= 0)
                {
                    return false;
                }

                _remaining--;
                return true;
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
            Description = "Writes serialized fields on a component using Unity's SerializedProperty API. " +
                "Accepts both serialized names (m_Color, m_Sprite) and property names (color, sprite). " +
                "Enums accept a name, underlying integer, or reader shape {value,index,name}; value is " +
                "authoritative and inconsistent metadata warns. Invalid values are rejected with legal " +
                "names listed. Partial struct writes (for example, {\"r\":1}) " +
                "preserve unmentioned components of the current value; on freshly-created objects, " +
                "unmentioned components are the type's default. Arrays require a JArray and replace the " +
                "whole collection; shrink discards removed elements with a warning, grown elements start " +
                "from type defaults, and nested objects are partial merges. Read output can be truncated " +
                "by the element budget: check arrayMetadata before writing it back; shrink warns. Direct " +
                $"Array.size writes accept 0-{SerializedPropertyHelper.MaxDirectArraySize}; growth warns " +
                "and follows Unity's direct resize behavior. If object-reference read-back verification " +
                "fails, collected reference writes are restored where safe; non-reference children and " +
                "array-size changes remain applied, and missing-reference previous values are not written " +
                "as null. Direct writes do not support Character, AnimationCurve, Gradient, " +
                "ExposedReference, FixedBufferSize, Vector2Int, Vector3Int, RectInt, BoundsInt, " +
                "ManagedReference, or Hash128. Direct " +
                "m_PersistentCalls writes warn that mode derivation is not validated and recommend " +
                "wire_unity_event. More reliable than update_component for " +
                "Unity built-in component fields. Ambiguous short or partial component names require " +
                "exactly one exact candidate type on the target; otherwise use a fully-qualified name.";
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
            Type componentType = ComponentResolver.FindComponentType(
                componentName,
                gameObject,
                out string resolutionWarning,
                out string ambiguityError);
            if (!string.IsNullOrEmpty(ambiguityError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    ambiguityError,
                    "component_ambiguity_error");
            }
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

            var updatedFields = new List<string>();
            var failedFields = new List<JObject>();
            var warnings = new List<string>();
            if (!string.IsNullOrEmpty(resolutionWarning))
            {
                warnings.Add(resolutionWarning);
            }

            foreach (var property in fieldData.Properties())
            {
                string fieldName = property.Name;
                JToken fieldValue = property.Value;

                try
                {
                    var serializedObject = new SerializedObject(component);
                    SerializedProperty prop = SerializedPropertyHelper.FindProperty(serializedObject, fieldName);
                    if (prop == null)
                    {
                        failedFields.Add(CreateFieldFailure(
                            fieldName,
                            $"Field '{fieldName}' was not found after checking SerializedProperty " +
                            $"on component '{component.GetType().Name}'"));
                        continue;
                    }

                    string serializedFieldName = prop.propertyPath;
                    var fieldWarnings = new List<string>();
                    if (!SerializedPropertyHelper.SetValue(
                        prop,
                        fieldValue,
                        fieldWarnings,
                        fieldName,
                        out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> objectReferenceWrites))
                    {
                        string reason = fieldWarnings.Count > 0
                            ? string.Join("; ", fieldWarnings.ToArray())
                            : $"Value could not be assigned to serialized field '{serializedFieldName}'";
                        failedFields.Add(CreateFieldFailure(fieldName, reason));
                        continue;
                    }

                    serializedObject.ApplyModifiedProperties();
                    if (!SerializedPropertyHelper.VerifyObjectReferenceWrites(
                        component,
                        objectReferenceWrites,
                        out string verificationFailure))
                    {
                        string reason = fieldWarnings.Count > 0
                            ? verificationFailure + "; warnings: " +
                                string.Join("; ", fieldWarnings.ToArray())
                            : verificationFailure;
                        failedFields.Add(CreateFieldFailure(fieldName, reason));
                        continue;
                    }

                    warnings.AddRange(fieldWarnings);
                    updatedFields.Add(serializedFieldName);
                }
                catch (Exception ex)
                {
                    failedFields.Add(CreateFieldFailure(
                        fieldName, $"Exception while setting field: {ex.Message}"));
                }
            }

            if (updatedFields.Count > 0)
            {
                EditorUtility.SetDirty(gameObject);
            }

            string message = $"Updated fields on '{componentName}' on '{gameObject.name}': " +
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
                ["instanceId"] = gameObject.GetInstanceID(),
                ["updatedFields"] = new JArray(updatedFields.ToArray()),
                ["failedFields"] = new JArray(failedFields.ToArray())
            };

            if (warnings.Count > 0)
            {
                response["warnings"] = new JArray(warnings.ToArray());
            }

            return response;
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
