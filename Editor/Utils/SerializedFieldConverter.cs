using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared utility for converting JToken values to C# types.
    /// Supports Unity structs (Vector, Color, Quaternion, Bounds, Rect),
    /// UnityEngine.Object references (asset path, GUID, instance ID, objectPath),
    /// arrays (T[]), List&lt;T&gt;, enums, and primitive types.
    /// </summary>
    public static class SerializedFieldConverter
    {
        /// <summary>
        /// Convert a JToken to a value of the specified type.
        /// </summary>
        /// <param name="token">The JToken to convert</param>
        /// <param name="targetType">The target type to convert to</param>
        /// <returns>The converted value, or null if conversion fails</returns>
        public static object ConvertJTokenToValue(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            // --- Unity struct types ---

            if (targetType == typeof(Vector2) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Vector2(
                    obj["x"]?.ToObject<float>() ?? 0f,
                    obj["y"]?.ToObject<float>() ?? 0f
                );
            }

            if (targetType == typeof(Vector3) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Vector3(
                    obj["x"]?.ToObject<float>() ?? 0f,
                    obj["y"]?.ToObject<float>() ?? 0f,
                    obj["z"]?.ToObject<float>() ?? 0f
                );
            }

            if (targetType == typeof(Vector4) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Vector4(
                    obj["x"]?.ToObject<float>() ?? 0f,
                    obj["y"]?.ToObject<float>() ?? 0f,
                    obj["z"]?.ToObject<float>() ?? 0f,
                    obj["w"]?.ToObject<float>() ?? 0f
                );
            }

            if (targetType == typeof(Quaternion) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Quaternion(
                    obj["x"]?.ToObject<float>() ?? 0f,
                    obj["y"]?.ToObject<float>() ?? 0f,
                    obj["z"]?.ToObject<float>() ?? 0f,
                    obj["w"]?.ToObject<float>() ?? 1f
                );
            }

            if (targetType == typeof(Color) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Color(
                    obj["r"]?.ToObject<float>() ?? 0f,
                    obj["g"]?.ToObject<float>() ?? 0f,
                    obj["b"]?.ToObject<float>() ?? 0f,
                    obj["a"]?.ToObject<float>() ?? 1f
                );
            }

            if (targetType == typeof(Bounds) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                Vector3 center = obj["center"]?.ToObject<Vector3>() ?? Vector3.zero;
                Vector3 size = obj["size"]?.ToObject<Vector3>() ?? Vector3.one;
                return new Bounds(center, size);
            }

            if (targetType == typeof(Rect) && token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                return new Rect(
                    obj["x"]?.ToObject<float>() ?? 0f,
                    obj["y"]?.ToObject<float>() ?? 0f,
                    obj["width"]?.ToObject<float>() ?? 0f,
                    obj["height"]?.ToObject<float>() ?? 0f
                );
            }

            // --- UnityEngine.Object references ---

            // Scene object reference via instance ID (integer value)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Integer)
            {
                int id = token.ToObject<int>();
                return ResolveUnityObjectByInstanceId(id, targetType);
            }

            // Scene object reference via structured reference ({"instanceId": 123} or {"objectPath": "Path/To/Object"})
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Object)
            {
                JObject refObj = (JObject)token;
                // Skip if this looks like a Vector/Color/etc. (has x/y/z/r/g/b keys) — those are handled above
                if (refObj.ContainsKey("instanceId") || refObj.ContainsKey("objectPath"))
                {
                    return ResolveUnityObjectByStructuredRef(refObj, targetType);
                }
            }

            // Asset reference via path or GUID (string value)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.String)
            {
                string assetRef = token.ToObject<string>();
                return ResolveUnityObjectByAssetRef(assetRef, targetType);
            }

            // --- Enum ---

            if (targetType.IsEnum)
            {
                if (token.Type == JTokenType.String)
                {
                    string enumName = token.ToObject<string>();
                    if (Enum.TryParse(targetType, enumName, true, out object result))
                    {
                        return result;
                    }
                    // If parsing fails, try to convert numeric string
                    if (int.TryParse(enumName, out int enumValue))
                    {
                        return Enum.ToObject(targetType, enumValue);
                    }
                }
                else if (token.Type == JTokenType.Integer)
                {
                    return Enum.ToObject(targetType, token.ToObject<int>());
                }
            }

            // --- Array (T[]) ---

            if (targetType.IsArray && token.Type == JTokenType.Array)
            {
                JArray jArray = (JArray)token;
                Type elementType = targetType.GetElementType();
                Array arr = Array.CreateInstance(elementType, jArray.Count);
                for (int i = 0; i < jArray.Count; i++)
                {
                    arr.SetValue(ConvertJTokenToValue(jArray[i], elementType), i);
                }
                return arr;
            }

            // --- List<T> ---

            if (targetType.IsGenericType
                && targetType.GetGenericTypeDefinition() == typeof(List<>)
                && token.Type == JTokenType.Array)
            {
                JArray jArray = (JArray)token;
                Type elementType = targetType.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(targetType);
                for (int i = 0; i < jArray.Count; i++)
                {
                    list.Add(ConvertJTokenToValue(jArray[i], elementType));
                }
                return list;
            }

            // --- Fallback: Newtonsoft generic deserialization ---

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

        /// <summary>
        /// Resolve a UnityEngine.Object by instance ID
        /// </summary>
        private static object ResolveUnityObjectByInstanceId(int id, Type targetType)
        {
            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(id);
            if (obj != null)
            {
                object resolved = CastUnityObject(obj, targetType);
                if (resolved != null)
                    return resolved;
            }
            McpLogger.LogWarning($"[MCP Unity] Could not resolve instance ID {id} to type {targetType.Name}");
            return null;
        }

        /// <summary>
        /// Resolve a UnityEngine.Object by structured reference ({"instanceId": N} or {"objectPath": "..."})
        /// </summary>
        private static object ResolveUnityObjectByStructuredRef(JObject refObj, Type targetType)
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
                resolvedObj = FindGameObjectByPathAcrossScenes(objPath);
            }

            if (resolvedObj == null)
            {
                McpLogger.LogWarning($"[MCP Unity] Could not resolve scene object reference to type {targetType.Name}");
                return null;
            }

            object casted = CastUnityObject(resolvedObj, targetType);
            if (casted != null)
                return casted;

            McpLogger.LogWarning($"[MCP Unity] Resolved object type {resolvedObj.GetType().Name} is not assignable to {targetType.Name}");
            return null;
        }

        /// <summary>
        /// Resolve a UnityEngine.Object by asset path or GUID string
        /// </summary>
        private static object ResolveUnityObjectByAssetRef(string assetRef, Type targetType)
        {
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

        /// <summary>
        /// Cast a resolved UnityEngine.Object to the requested target type.
        /// Handles GameObject, Component subtypes, and direct assignment.
        /// </summary>
        private static object CastUnityObject(UnityEngine.Object obj, Type targetType)
        {
            // If target is GameObject, return directly or extract from component
            if (targetType == typeof(GameObject))
            {
                if (obj is GameObject go) return go;
                if (obj is Component comp) return comp.gameObject;
                return null;
            }

            // If target is a Component type, try to get it from the resolved object
            if (typeof(Component).IsAssignableFrom(targetType))
            {
                if (targetType.IsAssignableFrom(obj.GetType()))
                    return obj;
                if (obj is GameObject gameObj)
                    return gameObj.GetComponent(targetType);
                if (obj is Component c)
                    return c.gameObject.GetComponent(targetType);
                return null;
            }

            // For other UnityEngine.Object types (e.g. ScriptableObject, Material, Sprite), return if assignable
            if (targetType.IsAssignableFrom(obj.GetType()))
                return obj;

            return null;
        }

        /// <summary>
        /// Find a GameObject by path, searching across all loaded scenes
        /// </summary>
        private static GameObject FindGameObjectByPathAcrossScenes(string objPath)
        {
            GameObject found = GameObject.Find(objPath);
            if (found != null)
                return found;

            for (int i = 0; i < SceneManager.sceneCount; i++)
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
                        return t.gameObject;
                    }
                }
            }

            return null;
        }
    }
}
