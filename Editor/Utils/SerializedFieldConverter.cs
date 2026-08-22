using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using McpUnity.Services;

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
        private sealed class PendingFieldWrite
        {
            public FieldInfo Field { get; }
            public object Value { get; }
            public object PreviousValue { get; }

            public PendingFieldWrite(FieldInfo field, object value, object previousValue)
            {
                Field = field;
                Value = value;
                PreviousValue = previousValue;
            }
        }

        /// <summary>
        /// Convert a JToken to a value of the specified type when there is no serialized
        /// reference owner. Preview-object references fail closed if encountered.
        /// </summary>
        /// <param name="token">The JToken to convert</param>
        /// <param name="targetType">The target type to convert to</param>
        /// <param name="failures">Optional collection that receives conversion failure reasons</param>
        /// <returns>The converted value, or null if conversion fails</returns>
        public static object ConvertJTokenToValueWithoutReferenceOwner(
            JToken token,
            Type targetType,
            List<string> failures = null)
        {
            return ConvertJTokenToValueWithoutReferenceOwner(
                token, targetType, null, failures);
        }

        /// <summary>
        /// Convert a JToken without a serialized reference owner, preserving state omitted
        /// by the caller. Preview-object references fail closed if encountered.
        /// </summary>
        /// <param name="token">The JToken to convert</param>
        /// <param name="targetType">The target type to convert to</param>
        /// <param name="currentValue">The current value used as the seed for partial object writes</param>
        /// <param name="failures">Collection that receives conversion failure reasons</param>
        /// <returns>The converted value, or null if conversion fails</returns>
        public static object ConvertJTokenToValueWithoutReferenceOwner(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> failures)
        {
            return ConvertJTokenToValueWithoutReferenceOwner(
                token, targetType, currentValue, failures, null);
        }

        /// <summary>
        /// Convert a JToken without a serialized reference owner while separately reporting
        /// non-fatal conversion disclosures. Preview-object references fail closed if encountered.
        /// </summary>
        public static object ConvertJTokenToValueWithoutReferenceOwner(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> failures,
            List<string> warnings)
        {
            return ConvertJTokenToValue(
                token, targetType, currentValue, failures, warnings, null);
        }

        /// <summary>
        /// Convert a JToken with the object that will own any resolved serialized reference.
        /// Prefab-preview references fail closed when the owner is unknown or outside the preview.
        /// </summary>
        public static object ConvertJTokenToValue(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> failures,
            List<string> warnings,
            UnityEngine.Object referenceOwner)
        {
            failures ??= new List<string>();

            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    failures.Add($"Cannot assign null to non-nullable type {targetType.Name}");
                }
                return null;
            }

            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // --- Unity struct types ---

            if (IsSupportedUnityStruct(targetType))
            {
                return TryConvertUnityStruct(token, targetType, currentValue, failures, out object structValue)
                    ? structValue
                    : null;
            }

            // --- UnityEngine.Object references ---

            // Scene object reference via instance ID (integer value)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Integer)
            {
                int id = token.ToObject<int>();
                return ResolveUnityObjectByInstanceId(
                    id, targetType, failures, referenceOwner);
            }

            // Unity object reference via structured reference.
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Object)
            {
                return ResolveUnityObjectByStructuredRef(
                    (JObject)token, targetType, failures, warnings, referenceOwner);
            }

            // Asset reference via path or GUID (string value)
            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.String)
            {
                string assetRef = token.ToObject<string>();
                return ResolveUnityObjectByAssetRef(assetRef, targetType, failures);
            }

            // --- Enum ---

            if (targetType.IsEnum)
            {
                return ConvertEnum(token, targetType, failures, warnings);
            }

            // --- Array (T[]) ---

            if (targetType.IsArray && token.Type == JTokenType.Array)
            {
                JArray jArray = (JArray)token;
                Type elementType = targetType.GetElementType();
                Array arr = Array.CreateInstance(elementType, jArray.Count);
                for (int i = 0; i < jArray.Count; i++)
                {
                    int failureCount = failures.Count;
                    object elementSeed = currentValue is Array currentArray && i < currentArray.Length
                        ? currentArray.GetValue(i)
                        : null;
                    if (elementType.IsClass)
                    {
                        elementSeed = CloneClassSeed(elementSeed);
                    }
                    object elementValue = ConvertJTokenToValue(
                        jArray[i], elementType, elementSeed, failures, warnings, referenceOwner);
                    if (failures.Count > failureCount)
                    {
                        PrefixFailures(failures, failureCount, $"Array element {i}");
                        return null;
                    }
                    arr.SetValue(elementValue, i);
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
                    int failureCount = failures.Count;
                    object elementSeed = currentValue is IList currentList && i < currentList.Count
                        ? currentList[i]
                        : null;
                    if (elementType.IsClass)
                    {
                        elementSeed = CloneClassSeed(elementSeed);
                    }
                    object elementValue = ConvertJTokenToValue(
                        jArray[i], elementType, elementSeed, failures, warnings, referenceOwner);
                    if (failures.Count > failureCount)
                    {
                        PrefixFailures(failures, failureCount, $"List element {i}");
                        return null;
                    }
                    list.Add(elementValue);
                }
                return list;
            }

            // --- [Serializable] class/struct: recursive field-level deserialization ---

            if (token.Type == JTokenType.Object
                && !typeof(UnityEngine.Object).IsAssignableFrom(targetType)
                && (targetType.IsClass || (targetType.IsValueType && !targetType.IsPrimitive && !targetType.IsEnum))
                && targetType != typeof(decimal)
                && (targetType.GetCustomAttributes(typeof(SerializableAttribute), true).Length > 0
                    || targetType.IsValueType))
            {
                try
                {
                    JObject obj = (JObject)token;
                    List<FieldInfo> fields = GetSerializableFields(targetType);
                    var fieldsByName = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
                    foreach (FieldInfo field in fields)
                    {
                        // Fields are returned from the most-derived type first. Preserve that
                        // first match when a base type shadows a serialized field name.
                        if (!fieldsByName.ContainsKey(field.Name))
                        {
                            fieldsByName.Add(field.Name, field);
                        }
                    }

                    var matchedProperties = new List<JProperty>();
                    var unknownProperties = new List<JProperty>();
                    foreach (JProperty property in obj.Properties())
                    {
                        if (fieldsByName.ContainsKey(property.Name))
                        {
                            matchedProperties.Add(property);
                        }
                        else
                        {
                            unknownProperties.Add(property);
                        }
                    }

                    // Types such as Vector2Int expose writable properties rather than matching
                    // their serialized backing-field names, so retain the Newtonsoft fallback.
                    if (matchedProperties.Count == 0)
                    {
                        return ConvertWithNewtonsoftFallback(token, targetType, currentValue, failures);
                    }

                    if (unknownProperties.Count > 0)
                    {
                        string validFields = fields.Count > 0
                            ? string.Join(", ", fieldsByName.Keys)
                            : "(none)";
                        foreach (JProperty unknown in unknownProperties)
                        {
                            failures.Add(
                                $"Unknown field '{unknown.Name}' for {targetType.Name}. " +
                                $"Valid serializable fields: {validFields}");
                        }
                        return null;
                    }

                    object instance = currentValue != null && targetType.IsInstanceOfType(currentValue)
                        ? currentValue
                        : (targetType.IsValueType
                            ? Activator.CreateInstance(targetType)
                            : Activator.CreateInstance(targetType, true));
                    var pendingWrites = new List<PendingFieldWrite>();
                    int walkerFailureCount = failures.Count;

                    foreach (JProperty property in matchedProperties)
                    {
                        FieldInfo field = fieldsByName[property.Name];
                        JToken fieldToken = property.Value;

                        if (fieldToken == null || fieldToken.Type == JTokenType.Null)
                        {
                            if (field.FieldType.IsValueType
                                && Nullable.GetUnderlyingType(field.FieldType) == null)
                            {
                                failures.Add(
                                    $"Nested field '{field.Name}': cannot assign null to " +
                                    $"non-nullable type {field.FieldType.Name}");
                                continue;
                            }

                            pendingWrites.Add(new PendingFieldWrite(
                                field, null, field.GetValue(instance)));
                            continue;
                        }

                        int failureCount = failures.Count;
                        object fieldSeed = field.GetValue(instance);
                        object conversionSeed = fieldToken.Type == JTokenType.Object
                            && field.FieldType.IsClass
                            && fieldSeed != null
                            ? CloneClassSeed(fieldSeed)
                            : fieldSeed;
                        object fieldValue = ConvertJTokenToValue(
                            fieldToken,
                            field.FieldType,
                            conversionSeed,
                            failures,
                            warnings,
                            referenceOwner);
                        if (failures.Count > failureCount)
                        {
                            PrefixFailures(failures, failureCount, $"Nested field '{field.Name}'");
                            continue;
                        }
                        pendingWrites.Add(new PendingFieldWrite(field, fieldValue, fieldSeed));
                    }

                    if (failures.Count > walkerFailureCount)
                    {
                        return null;
                    }

                    int appliedCount = 0;
                    try
                    {
                        foreach (PendingFieldWrite pendingWrite in pendingWrites)
                        {
                            pendingWrite.Field.SetValue(instance, pendingWrite.Value);
                            appliedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        for (int i = appliedCount - 1; i >= 0; i--)
                        {
                            PendingFieldWrite appliedWrite = pendingWrites[i];
                            appliedWrite.Field.SetValue(instance, appliedWrite.PreviousValue);
                        }
                        failures.Add(
                            $"Failed to apply converted fields to {targetType.Name}: {ex.Message}");
                        return null;
                    }

                    return instance;
                }
                catch (Exception ex)
                {
                    string reason = $"Failed recursive deserialization for {targetType.Name}: {ex.Message}";
                    McpLogger.LogError($"[MCP Unity] {reason}");
                    failures.Add(reason);
                    return null;
                }
            }

            // --- Fallback: Newtonsoft generic deserialization ---

            return ConvertWithNewtonsoftFallback(token, targetType, currentValue, failures);
        }

        /// <summary>
        /// Convert a supported Unity struct using a current-value seed and strict key validation.
        /// </summary>
        public static bool TryConvertUnityStruct(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> failures,
            out object convertedValue)
        {
            failures ??= new List<string>();
            convertedValue = null;

            string[] validKeys = GetUnityStructKeys(targetType);
            if (validKeys == null)
            {
                failures.Add($"Type {targetType.Name} is not a supported Unity struct");
                return false;
            }

            if (token == null || token.Type != JTokenType.Object)
            {
                failures.Add(
                    $"Expected an object for {targetType.Name}. Valid keys: {string.Join(", ", validKeys)}");
                return false;
            }

            JObject obj = (JObject)token;
            if (!ValidateObjectKeys(obj, targetType.Name, validKeys, failures))
            {
                return false;
            }

            bool hasReadableSeed = currentValue != null && targetType.IsInstanceOfType(currentValue);
            if (!hasReadableSeed)
            {
                var missingKeys = new List<string>();
                foreach (string validKey in validKeys)
                {
                    if (!obj.TryGetValue(validKey, StringComparison.Ordinal, out _))
                    {
                        missingKeys.Add(validKey);
                    }
                }
                if (missingKeys.Count > 0)
                {
                    failures.Add(
                        $"Cannot convert {targetType.Name}: cannot preserve unmentioned components: " +
                        $"no readable current value; provide all of {{{string.Join(", ", validKeys)}}}");
                    return false;
                }
            }

            try
            {
                if (targetType == typeof(Vector2))
                {
                    Vector2 seed = currentValue is Vector2 value ? value : default;
                    bool success = TryReadFloat(
                        obj, "x", seed.x, targetType.Name, failures, out float x);
                    success &= TryReadFloat(
                        obj, "y", seed.y, targetType.Name, failures, out float y);
                    if (!success)
                        return false;
                    convertedValue = new Vector2(x, y);
                    return true;
                }

                if (targetType == typeof(Vector2Int))
                {
                    Vector2Int seed = currentValue is Vector2Int value ? value : default;
                    bool success = TryReadInt(
                        obj, "x", seed.x, targetType.Name, failures, out int x);
                    success &= TryReadInt(
                        obj, "y", seed.y, targetType.Name, failures, out int y);
                    if (!success)
                        return false;
                    convertedValue = new Vector2Int(x, y);
                    return true;
                }

                if (targetType == typeof(Vector3))
                {
                    Vector3 seed = currentValue is Vector3 value ? value : default;
                    bool success = TryReadFloat(
                        obj, "x", seed.x, targetType.Name, failures, out float x);
                    success &= TryReadFloat(
                        obj, "y", seed.y, targetType.Name, failures, out float y);
                    success &= TryReadFloat(
                        obj, "z", seed.z, targetType.Name, failures, out float z);
                    if (!success)
                        return false;
                    convertedValue = new Vector3(x, y, z);
                    return true;
                }

                if (targetType == typeof(Vector3Int))
                {
                    Vector3Int seed = currentValue is Vector3Int value ? value : default;
                    bool success = TryReadInt(
                        obj, "x", seed.x, targetType.Name, failures, out int x);
                    success &= TryReadInt(
                        obj, "y", seed.y, targetType.Name, failures, out int y);
                    success &= TryReadInt(
                        obj, "z", seed.z, targetType.Name, failures, out int z);
                    if (!success)
                        return false;
                    convertedValue = new Vector3Int(x, y, z);
                    return true;
                }

                if (targetType == typeof(Vector4))
                {
                    Vector4 seed = currentValue is Vector4 value ? value : default;
                    bool success = TryReadFloat(
                        obj, "x", seed.x, targetType.Name, failures, out float x);
                    success &= TryReadFloat(
                        obj, "y", seed.y, targetType.Name, failures, out float y);
                    success &= TryReadFloat(
                        obj, "z", seed.z, targetType.Name, failures, out float z);
                    success &= TryReadFloat(
                        obj, "w", seed.w, targetType.Name, failures, out float w);
                    if (!success)
                        return false;
                    convertedValue = new Vector4(x, y, z, w);
                    return true;
                }

                if (targetType == typeof(Quaternion))
                {
                    Quaternion seed = currentValue is Quaternion value ? value : default;
                    bool success = TryReadFloat(
                        obj, "x", seed.x, targetType.Name, failures, out float x);
                    success &= TryReadFloat(
                        obj, "y", seed.y, targetType.Name, failures, out float y);
                    success &= TryReadFloat(
                        obj, "z", seed.z, targetType.Name, failures, out float z);
                    success &= TryReadFloat(
                        obj, "w", seed.w, targetType.Name, failures, out float w);
                    if (!success)
                        return false;
                    convertedValue = new Quaternion(x, y, z, w);
                    return true;
                }

                if (targetType == typeof(Color))
                {
                    Color seed = currentValue is Color value ? value : default;
                    bool success = TryReadFloat(
                        obj, "r", seed.r, targetType.Name, failures, out float r);
                    success &= TryReadFloat(
                        obj, "g", seed.g, targetType.Name, failures, out float g);
                    success &= TryReadFloat(
                        obj, "b", seed.b, targetType.Name, failures, out float b);
                    success &= TryReadFloat(
                        obj, "a", seed.a, targetType.Name, failures, out float a);
                    if (!success)
                        return false;
                    convertedValue = new Color(r, g, b, a);
                    return true;
                }

                if (targetType == typeof(Color32))
                {
                    Color32 seed = currentValue is Color32 value ? value : default;
                    bool success = TryReadByte(
                        obj, "r", seed.r, targetType.Name, failures, out byte r);
                    success &= TryReadByte(
                        obj, "g", seed.g, targetType.Name, failures, out byte g);
                    success &= TryReadByte(
                        obj, "b", seed.b, targetType.Name, failures, out byte b);
                    success &= TryReadByte(
                        obj, "a", seed.a, targetType.Name, failures, out byte a);
                    if (!success)
                        return false;
                    convertedValue = new Color32(r, g, b, a);
                    return true;
                }

                if (targetType == typeof(Rect))
                {
                    Rect seed = currentValue is Rect value ? value : default;
                    bool success = TryReadFloat(
                        obj, "x", seed.x, targetType.Name, failures, out float x);
                    success &= TryReadFloat(
                        obj, "y", seed.y, targetType.Name, failures, out float y);
                    success &= TryReadFloat(
                        obj, "width", seed.width, targetType.Name, failures, out float width);
                    success &= TryReadFloat(
                        obj, "height", seed.height, targetType.Name, failures, out float height);
                    if (!success)
                        return false;
                    convertedValue = new Rect(x, y, width, height);
                    return true;
                }

                if (targetType == typeof(RectInt))
                {
                    RectInt seed = currentValue is RectInt value ? value : default;
                    bool success = TryReadInt(
                        obj, "x", seed.x, targetType.Name, failures, out int x);
                    success &= TryReadInt(
                        obj, "y", seed.y, targetType.Name, failures, out int y);
                    success &= TryReadInt(
                        obj, "width", seed.width, targetType.Name, failures, out int width);
                    success &= TryReadInt(
                        obj, "height", seed.height, targetType.Name, failures, out int height);
                    if (!success)
                        return false;
                    convertedValue = new RectInt(x, y, width, height);
                    return true;
                }

                if (targetType == typeof(Bounds))
                {
                    Bounds seed = currentValue is Bounds value ? value : default;
                    Vector3 center = seed.center;
                    Vector3 size = seed.size;
                    bool success = true;

                    if (obj.TryGetValue("center", out JToken centerToken))
                    {
                        int failureCount = failures.Count;
                        if (!TryConvertUnityStruct(
                            centerToken,
                            typeof(Vector3),
                            hasReadableSeed ? center : null,
                            failures,
                            out object convertedCenter))
                        {
                            PrefixFailures(failures, failureCount, "Bounds.center");
                            success = false;
                        }
                        else
                        {
                            center = (Vector3)convertedCenter;
                        }
                    }

                    if (obj.TryGetValue("size", out JToken sizeToken))
                    {
                        int failureCount = failures.Count;
                        if (!TryConvertUnityStruct(
                            sizeToken,
                            typeof(Vector3),
                            hasReadableSeed ? size : null,
                            failures,
                            out object convertedSize))
                        {
                            PrefixFailures(failures, failureCount, "Bounds.size");
                            success = false;
                        }
                        else
                        {
                            size = (Vector3)convertedSize;
                        }
                    }

                    if (!success)
                    {
                        return false;
                    }

                    convertedValue = new Bounds(center, size);
                    return true;
                }

                if (targetType == typeof(BoundsInt))
                {
                    BoundsInt seed = currentValue is BoundsInt value ? value : default;
                    Vector3Int position = seed.position;
                    Vector3Int size = seed.size;
                    bool success = true;

                    if (obj.TryGetValue("position", out JToken positionToken))
                    {
                        int failureCount = failures.Count;
                        if (!TryConvertUnityStruct(
                            positionToken,
                            typeof(Vector3Int),
                            hasReadableSeed ? position : null,
                            failures,
                            out object convertedPosition))
                        {
                            PrefixFailures(failures, failureCount, "BoundsInt.position");
                            success = false;
                        }
                        else
                        {
                            position = (Vector3Int)convertedPosition;
                        }
                    }

                    if (obj.TryGetValue("size", out JToken sizeToken))
                    {
                        int failureCount = failures.Count;
                        if (!TryConvertUnityStruct(
                            sizeToken,
                            typeof(Vector3Int),
                            hasReadableSeed ? size : null,
                            failures,
                            out object convertedSize))
                        {
                            PrefixFailures(failures, failureCount, "BoundsInt.size");
                            success = false;
                        }
                        else
                        {
                            size = (Vector3Int)convertedSize;
                        }
                    }

                    if (!success)
                    {
                        return false;
                    }

                    convertedValue = new BoundsInt(position, size);
                    return true;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Failed to convert {targetType.Name}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// A converted null is assignable when conversion itself recorded no failure.
        /// </summary>
        public static bool CannotAssignConvertedValue(List<string> failures)
        {
            return failures != null && failures.Count > 0;
        }

        /// <summary>
        /// Resolve a UnityEngine.Object by instance ID
        /// </summary>
        private static object ResolveUnityObjectByInstanceId(
            int id,
            Type targetType,
            List<string> failures,
            UnityEngine.Object referenceOwner)
        {
            JObject scopeError = PrefabSessionScope.TryResolveObjectByInstanceId(
                id, out UnityEngine.Object obj);
            if (scopeError != null)
            {
                failures.Add(FormatScopeError(scopeError));
                return null;
            }

            if (obj != null)
            {
                JObject assignmentError = PrefabSessionScope.ValidateReferenceAssignment(
                    referenceOwner, obj);
                if (assignmentError != null)
                {
                    failures.Add(FormatScopeError(assignmentError));
                    return null;
                }

                object resolved = CastUnityObject(obj, targetType);
                if (resolved != null)
                    return resolved;

                string mismatchReason = $"Resolved object type {obj.GetType().Name} is not assignable to {targetType.Name}";
                McpLogger.LogWarning($"[MCP Unity] {mismatchReason}");
                failures.Add(mismatchReason);
                return null;
            }
            string reason = $"Could not resolve instance ID {id} to type {targetType.Name}";
            McpLogger.LogWarning($"[MCP Unity] {reason}");
            failures.Add(reason);
            return null;
        }

        /// <summary>
        /// Resolve a UnityEngine.Object by structured reference, preferring durable locators.
        /// </summary>
        private static object ResolveUnityObjectByStructuredRef(
            JObject refObj,
            Type targetType,
            List<string> failures,
            List<string> warnings,
            UnityEngine.Object referenceOwner)
        {
            string[] locatorKeys = { "assetPath", "instanceId", "objectPath" };
            string[] validKeys = { "instanceId", "assetPath", "objectPath", "name", "type" };
            if (!ValidateObjectKeys(refObj, $"{targetType.Name} reference", validKeys, failures))
            {
                return null;
            }

            var descriptiveKeys = new List<string>();
            foreach (JProperty suppliedProperty in refObj.Properties())
            {
                if (suppliedProperty.Name == "name" || suppliedProperty.Name == "type")
                {
                    descriptiveKeys.Add(suppliedProperty.Name);
                }
            }
            if (descriptiveKeys.Count > 0)
            {
                warnings?.Add(
                    $"Ignored descriptive keys: {string.Join(", ", descriptiveKeys)} " +
                    $"for object reference '{targetType.Name}'");
            }

            var attemptFailures = new List<string>();
            foreach (string locatorKey in locatorKeys)
            {
                if (!refObj.TryGetValue(locatorKey, out JToken locatorToken))
                {
                    continue;
                }
                if (locatorToken.Type == JTokenType.Null
                    || (locatorToken.Type == JTokenType.String
                        && ((JValue)locatorToken).Value == null))
                {
                    continue;
                }

                if (TryResolveStructuredLocator(
                    locatorKey,
                    locatorToken,
                    targetType,
                    referenceOwner,
                    out object resolved,
                    out string attemptFailure,
                    out JObject scopeError))
                {
                    foreach (string priorFailure in attemptFailures)
                    {
                        warnings?.Add(
                            $"{priorFailure}; resolved successfully via locator '{locatorKey}' " +
                            $"(value {FormatLocatorValue(locatorToken)})");
                    }
                    return resolved;
                }

                if (scopeError != null)
                {
                    attemptFailures.Add(
                        $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) " +
                        $"was rejected: {FormatScopeError(scopeError)}");
                    continue;
                }

                attemptFailures.Add(attemptFailure);
            }

            if (attemptFailures.Count > 0)
            {
                failures.AddRange(attemptFailures);
            }
            else
            {
                failures.Add(
                    $"Object reference for {targetType.Name} must provide one of: " +
                    string.Join(", ", locatorKeys));
            }
            return null;
        }

        private static bool TryResolveStructuredLocator(
            string locatorKey,
            JToken locatorToken,
            Type targetType,
            UnityEngine.Object referenceOwner,
            out object resolved,
            out string failure,
            out JObject scopeError)
        {
            resolved = null;
            failure = null;
            scopeError = null;
            UnityEngine.Object candidate;
            try
            {
                switch (locatorKey)
                {
                    case "assetPath":
                        string assetPath = locatorToken?.ToObject<string>();
                        candidate = string.IsNullOrEmpty(assetPath)
                            ? null
                            : AssetDatabase.LoadAssetAtPath(assetPath, targetType);
                        break;
                    case "instanceId":
                        int instanceId = locatorToken.ToObject<int>();
                        scopeError = PrefabSessionScope.TryResolveObjectByInstanceId(
                            instanceId, out candidate);
                        if (scopeError != null)
                            return false;
                        break;
                    case "objectPath":
                        string objectPath = locatorToken?.ToObject<string>();
                        if (string.IsNullOrEmpty(objectPath))
                        {
                            candidate = null;
                        }
                        else
                        {
                            scopeError = PrefabSessionScope.TryResolveGameObject(
                                null, objectPath, out GameObject gameObject);
                            if (scopeError != null)
                                return false;
                            candidate = gameObject;
                        }
                        break;
                    default:
                        failure = $"Unsupported locator '{locatorKey}'";
                        return false;
                }
            }
            catch (Exception ex)
            {
                failure =
                    $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) " +
                    $"failed to resolve: {ex.Message}";
                return false;
            }

            if (candidate == null)
            {
                failure =
                    $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) " +
                    $"failed to resolve to type {targetType.Name}";
                return false;
            }

            JObject assignmentError = PrefabSessionScope.ValidateReferenceAssignment(
                referenceOwner, candidate);
            if (assignmentError != null)
            {
                scopeError = assignmentError;
                return false;
            }

            resolved = CastUnityObject(candidate, targetType);
            if (resolved != null)
            {
                return true;
            }

            failure =
                $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) resolved " +
                $"object type {candidate.GetType().Name}, which is not assignable to {targetType.Name}";
            return false;
        }

        private static string FormatLocatorValue(JToken token)
        {
            return token == null || token.Type == JTokenType.Null
                ? "null"
                : $"'{token}'";
        }

        private static string FormatScopeError(JObject scopeError)
        {
            string errorType = scopeError?["error"]?["type"]?.ToString();
            string message = scopeError?["error"]?["message"]?.ToString();
            return $"{errorType}: {message}";
        }

        /// <summary>
        /// Resolve a UnityEngine.Object by asset path or GUID string
        /// </summary>
        private static object ResolveUnityObjectByAssetRef(string assetRef, Type targetType, List<string> failures)
        {
            if (string.IsNullOrEmpty(assetRef))
            {
                failures.Add($"Asset reference is empty for type {targetType.Name}");
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

            string reason = $"Could not load asset of type {targetType.Name} from '{assetRef}'";
            McpLogger.LogWarning($"[MCP Unity] {reason}");
            failures.Add(reason);
            return null;
        }

        private static object ConvertWithNewtonsoftFallback(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> failures)
        {
            try
            {
                if (token.Type == JTokenType.Object)
                {
                    JObject obj = (JObject)token;
                    if (!IsDictionaryType(targetType))
                    {
                        var writableMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (FieldInfo field in targetType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (!field.IsInitOnly)
                            {
                                writableMembers.Add(field.Name);
                            }
                        }
                        foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (property.CanWrite && property.GetIndexParameters().Length == 0)
                            {
                                writableMembers.Add(property.Name);
                            }
                        }

                        var unknownKeys = new List<string>();
                        foreach (JProperty property in obj.Properties())
                        {
                            if (!writableMembers.Contains(property.Name))
                            {
                                unknownKeys.Add(property.Name);
                            }
                        }
                        if (unknownKeys.Count > 0)
                        {
                            string validMembers = writableMembers.Count > 0
                                ? string.Join(", ", writableMembers)
                                : "(none)";
                            foreach (string unknownKey in unknownKeys)
                            {
                                failures.Add(
                                    $"Unknown member '{unknownKey}' for {targetType.Name}. " +
                                    $"Valid public writable members: {validMembers}");
                            }
                            return null;
                        }
                    }

                    object boxed = currentValue != null && targetType.IsInstanceOfType(currentValue)
                        ? (targetType.IsClass ? CloneClassSeed(currentValue) : currentValue)
                        : CreateFallbackInstance(targetType);
                    JsonConvert.PopulateObject(
                        token.ToString(Formatting.None),
                        boxed,
                        new JsonSerializerSettings
                        {
                            MissingMemberHandling = MissingMemberHandling.Error,
                            ObjectCreationHandling = ObjectCreationHandling.Replace
                        });
                    return boxed;
                }

                object converted = token.ToObject(targetType);
                if (converted == null)
                {
                    failures.Add($"Failed to convert value to {targetType.Name}: conversion returned null");
                }
                return converted;
            }
            catch (Exception ex)
            {
                string reason = $"Failed to convert value to {targetType.Name}: {ex.Message}.";
                McpLogger.LogError(
                    $"[MCP Unity] Error converting value to type {targetType.Name}: {ex.Message}");
                failures.Add(reason);
                return null;
            }
        }

        private static object ConvertEnum(
            JToken token,
            Type targetType,
            List<string> failures,
            List<string> warnings)
        {
            string validValues = GetValidEnumValues(targetType);
            try
            {
                JObject enumReaderShape = token as JObject;
                if (enumReaderShape != null)
                {
                    string[] allowedEnumKeys = { "value", "index", "name" };
                    foreach (JProperty suppliedKey in enumReaderShape.Properties())
                    {
                        if (Array.IndexOf(allowedEnumKeys, suppliedKey.Name) < 0)
                        {
                            failures.Add(
                                $"Unknown enum key '{suppliedKey.Name}' for '{targetType.Name}'. " +
                                $"Valid reader-shape keys: {string.Join(", ", allowedEnumKeys)}");
                            return null;
                        }
                    }
                    if (!enumReaderShape.TryGetValue("value", out JToken underlyingValue))
                    {
                        failures.Add(
                            $"Reader-shaped enum object for '{targetType.Name}' must include 'value'");
                        return null;
                    }
                    if (underlyingValue.Type == JTokenType.Object)
                    {
                        failures.Add(
                            $"Expected an enum name or integer for {targetType.Name}. " +
                            $"Valid values: {validValues}");
                        return null;
                    }

                    int failureCount = failures.Count;
                    object readerShapeValue = ConvertEnum(
                        underlyingValue, targetType, failures, warnings);
                    if (failures.Count == failureCount)
                    {
                        AddEnumReaderShapeMismatchWarnings(
                            targetType, readerShapeValue, enumReaderShape, warnings);
                    }
                    return readerShapeValue;
                }

                object converted;
                Type underlyingType = Enum.GetUnderlyingType(targetType);
                bool unsignedUnderlying = IsUnsignedIntegerType(underlyingType);
                bool numericInput = false;
                long signedInput = 0;
                ulong unsignedInput = 0;
                if (token.Type == JTokenType.String)
                {
                    string text = token.ToObject<string>();
                    if (unsignedUnderlying
                        && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out unsignedInput))
                    {
                        numericInput = true;
                        converted = Enum.ToObject(targetType, unsignedInput);
                    }
                    else if (!unsignedUnderlying
                        && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out signedInput))
                    {
                        numericInput = true;
                        converted = Enum.ToObject(targetType, signedInput);
                    }
                    else if (IsIntegerText(text))
                    {
                        failures.Add(
                            $"Enum value '{text}' is outside the underlying range for {targetType.Name}. " +
                            $"Valid values: {validValues}");
                        return null;
                    }
                    else if (!Enum.TryParse(targetType, text, true, out converted))
                    {
                        failures.Add(
                            $"Enum value '{text}' is invalid for {targetType.Name}. Valid values: {validValues}");
                        return null;
                    }
                }
                else if (token.Type == JTokenType.Integer)
                {
                    numericInput = true;
                    if (unsignedUnderlying)
                    {
                        unsignedInput = Convert.ToUInt64(((JValue)token).Value, CultureInfo.InvariantCulture);
                        converted = Enum.ToObject(targetType, unsignedInput);
                    }
                    else
                    {
                        signedInput = Convert.ToInt64(((JValue)token).Value, CultureInfo.InvariantCulture);
                        converted = Enum.ToObject(targetType, signedInput);
                    }
                }
                else
                {
                    failures.Add(
                        $"Expected an enum name or integer for {targetType.Name}. Valid values: {validValues}");
                    return null;
                }

                if (numericInput)
                {
                    bool roundTrips = unsignedUnderlying
                        ? Convert.ToUInt64(converted, CultureInfo.InvariantCulture) == unsignedInput
                        : Convert.ToInt64(converted, CultureInfo.InvariantCulture) == signedInput;
                    if (!roundTrips)
                    {
                        failures.Add(
                            $"Enum value '{token}' is outside the underlying range for {targetType.Name}. " +
                            $"Valid values: {validValues}");
                        return null;
                    }
                }

                bool isFlags = targetType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;
                bool isValid;
                if (isFlags)
                {
                    ulong allDefinedBits = 0;
                    foreach (object definedValue in Enum.GetValues(targetType))
                    {
                        allDefinedBits |= EnumValueToUInt64(definedValue, targetType);
                    }
                    ulong candidateBits = EnumValueToUInt64(converted, targetType);
                    isValid = (candidateBits & ~allDefinedBits) == 0;
                }
                else
                {
                    isValid = Enum.IsDefined(targetType, converted);
                }

                if (!isValid)
                {
                    failures.Add(
                        $"Enum value '{token}' is invalid for {targetType.Name}. Valid values: {validValues}");
                    return null;
                }

                return converted;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Enum value '{token}' is invalid for {targetType.Name}: {ex.Message}. " +
                    $"Valid values: {validValues}");
                return null;
            }
        }

        private static void AddEnumReaderShapeMismatchWarnings(
            Type targetType,
            object resolvedValue,
            JObject readerShape,
            List<string> warnings)
        {
            if (readerShape == null || resolvedValue == null || warnings == null)
            {
                return;
            }

            string resolvedName = resolvedValue.ToString();
            if (readerShape.TryGetValue("name", out JToken suppliedNameToken))
            {
                string suppliedName = suppliedNameToken.Type == JTokenType.Null
                    ? null
                    : suppliedNameToken.ToString();
                bool suppliedNameMatches = suppliedName != null
                    && Enum.TryParse(targetType, suppliedName, true, out object suppliedValue)
                    && suppliedValue.Equals(resolvedValue);
                suppliedNameMatches = suppliedNameMatches
                    || InspectorDisplayNameMatchesResolvedValue(
                        targetType, suppliedName, resolvedValue);
                if (!suppliedNameMatches)
                {
                    warnings.Add(
                        $"Reader-shaped enum metadata mismatch for '{targetType.Name}': supplied name " +
                        $"'{suppliedName ?? "null"}', but 'value' resolved to name '{resolvedName}'. Used 'value'.");
                }
            }

            if (readerShape.TryGetValue("index", out JToken suppliedIndexToken))
            {
                string suppliedIndex = suppliedIndexToken.Type == JTokenType.Null
                    ? "null"
                    : suppliedIndexToken.ToString();
                bool hasIntegerIndex = int.TryParse(
                    suppliedIndex,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int suppliedIndexValue);
                int resolvedIndex = GetSerializedEnumIndex(targetType, resolvedValue);
                if (!hasIntegerIndex || suppliedIndexValue != resolvedIndex)
                {
                    warnings.Add(
                        $"Reader-shaped enum metadata mismatch for '{targetType.Name}': supplied index " +
                        $"'{suppliedIndex}', but 'value' resolved to index {resolvedIndex}. Used 'value'.");
                }
            }
        }

        private static bool InspectorDisplayNameMatchesResolvedValue(
            Type enumType,
            string suppliedName,
            object resolvedValue)
        {
            if (string.IsNullOrEmpty(suppliedName))
            {
                return false;
            }

            foreach (FieldInfo member in enumType.GetFields(
                BindingFlags.Public | BindingFlags.Static))
            {
                object memberValue = member.GetValue(null);
                if (!memberValue.Equals(resolvedValue))
                {
                    continue;
                }

                object[] attributes = member.GetCustomAttributes(
                    typeof(InspectorNameAttribute), false);
                if (attributes.Length == 0)
                {
                    continue;
                }

                Type attributeType = attributes[0].GetType();
                FieldInfo displayNameField = attributeType.GetField(
                    "displayName", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo displayNameProperty = attributeType.GetProperty(
                    "displayName", BindingFlags.Public | BindingFlags.Instance);
                string displayName = displayNameField != null
                    ? displayNameField.GetValue(attributes[0]) as string
                    : displayNameProperty?.GetValue(attributes[0], null) as string;
                if (string.Equals(
                    suppliedName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetSerializedEnumIndex(Type enumType, object resolvedValue)
        {
            // Unity SerializedProperty.enumValueIndex follows Enum.GetNames value ordering,
            // verified in Unity staging on 2026-08-22 with an out-of-order enum declaration.
            string resolvedName = resolvedValue.ToString();
            return Array.IndexOf(Enum.GetNames(enumType), resolvedName);
        }

        private static string GetValidEnumValues(Type enumType)
        {
            string[] names = Enum.GetNames(enumType);
            Array values = Enum.GetValues(enumType);
            var entries = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                object value = values.GetValue(i);
                entries.Add($"{names[i]}={FormatEnumValue(value, enumType)}");
            }
            return string.Join(", ", entries);
        }

        private static string FormatEnumValue(object value, Type enumType)
        {
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            if (underlyingType == typeof(ulong))
            {
                return Convert.ToUInt64(value).ToString();
            }
            return Convert.ToInt64(value).ToString();
        }

        private static ulong EnumValueToUInt64(object value, Type enumType)
        {
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            if (underlyingType == typeof(sbyte)) return unchecked((ulong)Convert.ToSByte(value));
            if (underlyingType == typeof(short)) return unchecked((ulong)Convert.ToInt16(value));
            if (underlyingType == typeof(int)) return unchecked((ulong)Convert.ToInt32(value));
            if (underlyingType == typeof(long)) return unchecked((ulong)Convert.ToInt64(value));
            return Convert.ToUInt64(value);
        }

        private static bool IsUnsignedIntegerType(Type type)
        {
            return type == typeof(byte)
                || type == typeof(ushort)
                || type == typeof(uint)
                || type == typeof(ulong);
        }

        private static bool IsIntegerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            int startIndex = trimmed[0] == '+' || trimmed[0] == '-' ? 1 : 0;
            if (startIndex == trimmed.Length)
            {
                return false;
            }
            for (int i = startIndex; i < trimmed.Length; i++)
            {
                if (!char.IsDigit(trimmed[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsDictionaryType(Type targetType)
        {
            if (typeof(IDictionary).IsAssignableFrom(targetType))
            {
                return true;
            }

            if (targetType.IsGenericType
                && targetType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                return true;
            }

            foreach (Type interfaceType in targetType.GetInterfaces())
            {
                if (interfaceType.IsGenericType
                    && interfaceType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    return true;
                }
            }
            return false;
        }

        private static object CreateFallbackInstance(Type targetType)
        {
            if (targetType.IsInterface
                && targetType.IsGenericType
                && targetType.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                Type concreteType = typeof(Dictionary<,>).MakeGenericType(
                    targetType.GetGenericArguments());
                if (targetType.IsAssignableFrom(concreteType))
                {
                    return Activator.CreateInstance(concreteType);
                }
            }

            return targetType.IsValueType
                ? Activator.CreateInstance(targetType)
                : Activator.CreateInstance(targetType, true);
        }

        private static List<FieldInfo> GetSerializableFields(Type targetType)
        {
            var fields = new List<FieldInfo>();
            for (Type currentType = targetType;
                 currentType != null && currentType != typeof(object);
                 currentType = currentType.BaseType)
            {
                foreach (FieldInfo field in currentType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    bool isSerializable = !field.IsNotSerialized
                        && !field.IsInitOnly
                        && (field.IsPublic
                            || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0);
                    if (isSerializable)
                    {
                        fields.Add(field);
                    }
                }
            }
            return fields;
        }

        private static bool IsSupportedUnityStruct(Type targetType)
        {
            return GetUnityStructKeys(targetType) != null;
        }

        private static string[] GetUnityStructKeys(Type targetType)
        {
            if (targetType == typeof(Vector2)) return new[] { "x", "y" };
            if (targetType == typeof(Vector2Int)) return new[] { "x", "y" };
            if (targetType == typeof(Vector3)) return new[] { "x", "y", "z" };
            if (targetType == typeof(Vector3Int)) return new[] { "x", "y", "z" };
            if (targetType == typeof(Vector4) || targetType == typeof(Quaternion))
                return new[] { "x", "y", "z", "w" };
            if (targetType == typeof(Color)) return new[] { "r", "g", "b", "a" };
            if (targetType == typeof(Color32)) return new[] { "r", "g", "b", "a" };
            if (targetType == typeof(Rect)) return new[] { "x", "y", "width", "height" };
            if (targetType == typeof(RectInt)) return new[] { "x", "y", "width", "height" };
            if (targetType == typeof(Bounds)) return new[] { "center", "size" };
            if (targetType == typeof(BoundsInt)) return new[] { "position", "size" };
            return null;
        }

        private static bool ValidateObjectKeys(
            JObject obj,
            string typeName,
            string[] validKeys,
            List<string> failures)
        {
            var validKeySet = new HashSet<string>(validKeys, StringComparer.Ordinal);
            bool valid = true;
            foreach (JProperty property in obj.Properties())
            {
                if (!validKeySet.Contains(property.Name))
                {
                    failures.Add(
                        $"Unknown key '{property.Name}' for {typeName}. " +
                        $"Valid keys: {string.Join(", ", validKeys)}");
                    valid = false;
                }
            }
            return valid;
        }

        private static bool TryReadFloat(
            JObject obj,
            string key,
            float seed,
            string typeName,
            List<string> failures,
            out float value)
        {
            value = seed;
            if (!obj.TryGetValue(key, out JToken token))
            {
                return true;
            }
            if (token == null || token.Type == JTokenType.Null)
            {
                failures.Add($"Key '{key}' on {typeName} cannot be null; expected a number");
                return false;
            }
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                failures.Add($"Key '{key}' on {typeName} must be a JSON number");
                return false;
            }
            try
            {
                value = token.ToObject<float>();
                return true;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Key '{key}' on {typeName} could not be converted to a number: {ex.Message}");
                return false;
            }
        }

        private static bool TryReadInt(
            JObject obj,
            string key,
            int seed,
            string typeName,
            List<string> failures,
            out int value)
        {
            value = seed;
            if (!obj.TryGetValue(key, out JToken token))
            {
                return true;
            }
            if (token == null
                || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                failures.Add($"Key '{key}' on {typeName} must be a JSON integer");
                return false;
            }
            try
            {
                if (token.Type == JTokenType.Integer)
                {
                    value = token.ToObject<int>();
                    return true;
                }

                double numericValue = token.ToObject<double>();
                if (double.IsNaN(numericValue)
                    || double.IsInfinity(numericValue)
                    || numericValue < int.MinValue
                    || numericValue > int.MaxValue
                    || Math.Truncate(numericValue) != numericValue)
                {
                    failures.Add(
                        $"Key '{key}' on {typeName} must be a JSON integer within Int32 range");
                    return false;
                }
                value = Convert.ToInt32(numericValue);
                return true;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Key '{key}' on {typeName} could not be converted to an integer: {ex.Message}");
                return false;
            }
        }

        private static bool TryReadByte(
            JObject obj,
            string key,
            byte seed,
            string typeName,
            List<string> failures,
            out byte value)
        {
            value = seed;
            if (!obj.TryGetValue(key, out JToken token))
            {
                return true;
            }
            if (token == null
                || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                failures.Add($"Key '{key}' on {typeName} must be a JSON integer from 0 to 255");
                return false;
            }
            try
            {
                if (token.Type == JTokenType.Integer)
                {
                    value = token.ToObject<byte>();
                    return true;
                }

                double numericValue = token.ToObject<double>();
                if (double.IsNaN(numericValue)
                    || double.IsInfinity(numericValue)
                    || numericValue < byte.MinValue
                    || numericValue > byte.MaxValue
                    || Math.Truncate(numericValue) != numericValue)
                {
                    failures.Add(
                        $"Key '{key}' on {typeName} must be a JSON integer from 0 to 255");
                    return false;
                }
                value = Convert.ToByte(numericValue);
                return true;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Key '{key}' on {typeName} could not be converted to a byte (0 to 255): {ex.Message}");
                return false;
            }
        }

        private static void PrefixFailures(List<string> failures, int startIndex, string prefix)
        {
            if (failures.Count == startIndex)
            {
                failures.Add($"{prefix}: conversion failed");
                return;
            }
            for (int i = startIndex; i < failures.Count; i++)
            {
                failures[i] = $"{prefix}: {failures[i]}";
            }
        }

        internal static object CloneClassSeed(object seed)
        {
            if (seed == null
                || seed is string
                || seed is UnityEngine.Object
                || seed.GetType().IsValueType)
            {
                return seed;
            }

            if (seed is Array array)
            {
                return array.Clone();
            }

            if (seed is IDictionary dictionary)
            {
                if (!(Activator.CreateInstance(seed.GetType()) is IDictionary dictionaryClone))
                {
                    throw new InvalidOperationException(
                        $"Could not create an isolated dictionary seed for {seed.GetType().Name}");
                }
                foreach (DictionaryEntry entry in dictionary)
                {
                    dictionaryClone.Add(entry.Key, entry.Value);
                }
                return dictionaryClone;
            }

            if (seed is IList list)
            {
                if (!(Activator.CreateInstance(seed.GetType()) is IList listClone))
                {
                    throw new InvalidOperationException(
                        $"Could not create an isolated list seed for {seed.GetType().Name}");
                }
                foreach (object item in list)
                {
                    listClone.Add(item);
                }
                return listClone;
            }

            MethodInfo memberwiseClone = typeof(object).GetMethod(
                "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            return memberwiseClone.Invoke(seed, null);
        }

        /// <summary>
        /// Read a reflection property seed unless the getter may materialize Unity objects.
        /// </summary>
        internal static object GetSafePropertySeed(PropertyInfo property, object target)
        {
            if (property == null || !property.CanRead)
            {
                return null;
            }

            Type propertyType = property.PropertyType;
            if (typeof(UnityEngine.Object).IsAssignableFrom(propertyType)
                || HasUnityObjectCollectionElement(propertyType))
            {
                return null;
            }

            return property.GetValue(target);
        }

        private static bool HasUnityObjectCollectionElement(Type collectionType)
        {
            Type elementType = collectionType.IsArray
                ? collectionType.GetElementType()
                : null;

            if (elementType == null && collectionType.IsGenericType)
            {
                Type genericDefinition = collectionType.GetGenericTypeDefinition();
                if (genericDefinition == typeof(List<>)
                    || genericDefinition == typeof(IList<>))
                {
                    elementType = collectionType.GetGenericArguments()[0];
                }
            }

            if (elementType == null)
            {
                foreach (Type interfaceType in collectionType.GetInterfaces())
                {
                    if (interfaceType.IsGenericType
                        && interfaceType.GetGenericTypeDefinition() == typeof(IList<>))
                    {
                        elementType = interfaceType.GetGenericArguments()[0];
                        break;
                    }
                }
            }

            return elementType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(elementType);
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

    }
}
