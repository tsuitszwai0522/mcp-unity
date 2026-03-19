using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared utility for finding and setting SerializedProperty values.
    /// Consolidates logic previously duplicated across UpdateComponentTool and SerializedFieldTools.
    /// </summary>
    public static class SerializedPropertyHelper
    {
        /// <summary>
        /// Find a SerializedProperty by name, with bidirectional m_ prefix mapping.
        /// Tries: exact name → m_Name → name without m_ prefix.
        /// </summary>
        public static SerializedProperty FindProperty(SerializedObject so, string name)
        {
            // Try direct name
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null) return prop;

            // Try with m_ prefix (e.g., "color" -> "m_Color")
            if (!name.StartsWith("m_"))
            {
                string serializedName = "m_" + char.ToUpper(name[0]) + name.Substring(1);
                prop = so.FindProperty(serializedName);
                if (prop != null) return prop;
            }

            // Try without m_ prefix (e.g., "m_Color" -> "color")
            if (name.StartsWith("m_") && name.Length > 2)
            {
                string withoutPrefix = char.ToLower(name[2]) + name.Substring(3);
                prop = so.FindProperty(withoutPrefix);
                if (prop != null) return prop;
            }

            return null;
        }

        /// <summary>
        /// Set a SerializedProperty value from a JToken.
        /// Supports: Integer, Boolean, Float, String, Color, Vector2/3/4, Rect,
        /// Enum, ObjectReference (asset path/instanceId/GUID/structured with both assetPath and objectPath),
        /// Bounds, Quaternion, and null clearing for ObjectReference.
        /// </summary>
        public static bool SetValue(SerializedProperty prop, JToken value, List<string> warnings, string fieldName)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.ToObject<int>();
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.ToObject<bool>();
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.ToObject<float>();
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.ToObject<string>();
                        return true;
                    case SerializedPropertyType.Color:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject c = (JObject)value;
                            prop.colorValue = new Color(
                                c["r"]?.ToObject<float>() ?? 0f,
                                c["g"]?.ToObject<float>() ?? 0f,
                                c["b"]?.ToObject<float>() ?? 0f,
                                c["a"]?.ToObject<float>() ?? 1f
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Vector2:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject v = (JObject)value;
                            prop.vector2Value = new Vector2(
                                v["x"]?.ToObject<float>() ?? 0f,
                                v["y"]?.ToObject<float>() ?? 0f
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Vector3:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject v = (JObject)value;
                            prop.vector3Value = new Vector3(
                                v["x"]?.ToObject<float>() ?? 0f,
                                v["y"]?.ToObject<float>() ?? 0f,
                                v["z"]?.ToObject<float>() ?? 0f
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Vector4:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject v = (JObject)value;
                            prop.vector4Value = new Vector4(
                                v["x"]?.ToObject<float>() ?? 0f,
                                v["y"]?.ToObject<float>() ?? 0f,
                                v["z"]?.ToObject<float>() ?? 0f,
                                v["w"]?.ToObject<float>() ?? 0f
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Rect:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject r = (JObject)value;
                            prop.rectValue = new Rect(
                                r["x"]?.ToObject<float>() ?? 0f,
                                r["y"]?.ToObject<float>() ?? 0f,
                                r["width"]?.ToObject<float>() ?? 0f,
                                r["height"]?.ToObject<float>() ?? 0f
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Enum:
                        if (value.Type == JTokenType.String)
                        {
                            string strValue = value.ToObject<string>();

                            // Try display names first (non-obsolete API)
                            string[] displayNames = prop.enumDisplayNames;
                            for (int i = 0; i < displayNames.Length; i++)
                            {
                                if (string.Equals(displayNames[i], strValue, StringComparison.OrdinalIgnoreCase))
                                {
                                    prop.enumValueIndex = i;
                                    return true;
                                }
                            }

                            // Fallback: try internal C# enum names (agents typically send these)
                            // enumNames is obsolete but there is no non-obsolete replacement for internal names
#pragma warning disable CS0618 // Type or member is obsolete
                            string[] internalNames = prop.enumNames;
#pragma warning restore CS0618
                            for (int i = 0; i < internalNames.Length; i++)
                            {
                                if (string.Equals(internalNames[i], strValue, StringComparison.OrdinalIgnoreCase))
                                {
                                    prop.enumValueIndex = i;
                                    return true;
                                }
                            }

                            warnings?.Add($"Enum value '{strValue}' not found for '{fieldName}'. Valid: {string.Join(", ", internalNames)}");
                        }
                        else if (value.Type == JTokenType.Integer)
                        {
                            prop.enumValueIndex = value.ToObject<int>();
                            return true;
                        }
                        break;
                    case SerializedPropertyType.ObjectReference:
                        // String: try as asset path, then as GUID
                        if (value.Type == JTokenType.String)
                        {
                            string assetRef = value.ToObject<string>();
                            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetRef);
                            if (asset == null)
                            {
                                string guidPath = AssetDatabase.GUIDToAssetPath(assetRef);
                                if (!string.IsNullOrEmpty(guidPath))
                                    asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(guidPath);
                            }
                            if (asset != null)
                            {
                                prop.objectReferenceValue = asset;
                                return true;
                            }
                            warnings?.Add($"Asset not found at '{assetRef}' for '{fieldName}'");
                        }
                        // Integer: try as instance ID
                        else if (value.Type == JTokenType.Integer)
                        {
                            int id = value.ToObject<int>();
                            var obj = EditorUtility.InstanceIDToObject(id);
                            if (obj != null)
                            {
                                prop.objectReferenceValue = obj;
                                return true;
                            }
                            warnings?.Add($"Object not found with instance ID {id} for '{fieldName}'");
                        }
                        // Structured reference: { instanceId, assetPath, objectPath }
                        else if (value.Type == JTokenType.Object)
                        {
                            JObject refObj = (JObject)value;
                            if (refObj.ContainsKey("instanceId"))
                            {
                                int id = refObj["instanceId"].ToObject<int>();
                                var obj = EditorUtility.InstanceIDToObject(id);
                                if (obj != null)
                                {
                                    prop.objectReferenceValue = obj;
                                    return true;
                                }
                            }
                            if (refObj.ContainsKey("assetPath"))
                            {
                                string path = refObj["assetPath"].ToObject<string>();
                                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                                if (asset != null)
                                {
                                    prop.objectReferenceValue = asset;
                                    return true;
                                }
                            }
                            if (refObj.ContainsKey("objectPath"))
                            {
                                string path = refObj["objectPath"].ToObject<string>();
                                var go = GameObject.Find(path);
                                if (go != null)
                                {
                                    prop.objectReferenceValue = go;
                                    return true;
                                }
                            }
                            warnings?.Add($"Object reference could not be resolved for '{fieldName}'");
                        }
                        // Null: clear the reference
                        else if (value.Type == JTokenType.Null)
                        {
                            prop.objectReferenceValue = null;
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Bounds:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject bObj = (JObject)value;
                            JObject center = bObj["center"] as JObject;
                            JObject size = bObj["size"] as JObject;
                            prop.boundsValue = new Bounds(
                                center != null ? new Vector3(
                                    center["x"]?.ToObject<float>() ?? 0f,
                                    center["y"]?.ToObject<float>() ?? 0f,
                                    center["z"]?.ToObject<float>() ?? 0f
                                ) : Vector3.zero,
                                size != null ? new Vector3(
                                    size["x"]?.ToObject<float>() ?? 0f,
                                    size["y"]?.ToObject<float>() ?? 0f,
                                    size["z"]?.ToObject<float>() ?? 0f
                                ) : Vector3.zero
                            );
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Quaternion:
                        if (value.Type == JTokenType.Object)
                        {
                            JObject q = (JObject)value;
                            prop.quaternionValue = new Quaternion(
                                q["x"]?.ToObject<float>() ?? 0f,
                                q["y"]?.ToObject<float>() ?? 0f,
                                q["z"]?.ToObject<float>() ?? 0f,
                                q["w"]?.ToObject<float>() ?? 1f
                            );
                            return true;
                        }
                        break;
                    default:
                        warnings?.Add($"Property type '{prop.propertyType}' not supported for '{fieldName}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"Error setting '{fieldName}': {ex.Message}");
            }
            return false;
        }
    }
}
