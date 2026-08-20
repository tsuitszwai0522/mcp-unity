using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Services;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared utility for finding and setting SerializedProperty values.
    /// Consolidates logic previously duplicated across UpdateComponentTool and SerializedFieldTools.
    /// </summary>
    public static class SerializedPropertyHelper
    {
        public sealed class ObjectReferenceWrite
        {
            public UnityEngine.Object PreviousValue { get; }
            public UnityEngine.Object AttemptedValue { get; }
            public bool IsIntentionalClear { get; }

            public ObjectReferenceWrite(
                UnityEngine.Object previousValue,
                UnityEngine.Object attemptedValue,
                bool isIntentionalClear)
            {
                PreviousValue = previousValue;
                AttemptedValue = attemptedValue;
                IsIntentionalClear = isIntentionalClear;
            }
        }

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
            ObjectReferenceWrite ignoredWrite;
            return SetValue(prop, value, warnings, fieldName, out ignoredWrite);
        }

        /// <summary>
        /// Set a SerializedProperty value and report object-reference assignment metadata for read-back verification.
        /// </summary>
        public static bool SetValue(
            SerializedProperty prop,
            JToken value,
            List<string> warnings,
            string fieldName,
            out ObjectReferenceWrite objectReferenceWrite)
        {
            objectReferenceWrite = null;
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
                        if (!TryConvertUnityStructValue(
                            value, typeof(Color), prop.colorValue, warnings, out object colorValue))
                            return false;
                        prop.colorValue = (Color)colorValue;
                        return true;
                    case SerializedPropertyType.Vector2:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector2), prop.vector2Value, warnings, out object vector2Value))
                            return false;
                        prop.vector2Value = (Vector2)vector2Value;
                        return true;
                    case SerializedPropertyType.Vector3:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector3), prop.vector3Value, warnings, out object vector3Value))
                            return false;
                        prop.vector3Value = (Vector3)vector3Value;
                        return true;
                    case SerializedPropertyType.Vector4:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector4), prop.vector4Value, warnings, out object vector4Value))
                            return false;
                        prop.vector4Value = (Vector4)vector4Value;
                        return true;
                    case SerializedPropertyType.Rect:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Rect), prop.rectValue, warnings, out object rectValue))
                            return false;
                        prop.rectValue = (Rect)rectValue;
                        return true;
                    case SerializedPropertyType.Enum:
                        if (value is JObject enumObject)
                        {
                            string[] allowedEnumKeys = { "value", "index", "name" };
                            foreach (JProperty suppliedKey in enumObject.Properties())
                            {
                                if (Array.IndexOf(allowedEnumKeys, suppliedKey.Name) < 0)
                                {
                                    warnings?.Add(
                                        $"Unknown enum key '{suppliedKey.Name}' for '{fieldName}'. " +
                                        $"Valid reader-shape keys: {string.Join(", ", allowedEnumKeys)}");
                                    return false;
                                }
                            }
                            if (!enumObject.TryGetValue("value", out JToken underlyingValue))
                            {
                                warnings?.Add(
                                    $"Reader-shaped enum object for '{fieldName}' must include 'value'");
                                return false;
                            }
                            value = underlyingValue;
                        }

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

                            if (int.TryParse(
                                strValue,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int numericValue))
                            {
                                return TrySetEnumInteger(
                                    prop, numericValue, warnings, fieldName, internalNames);
                            }

                            warnings?.Add(
                                $"Enum value '{strValue}' not found for '{fieldName}'. " +
                                $"Valid names: {string.Join(", ", internalNames)}");
                        }
                        else if (value.Type == JTokenType.Integer)
                        {
                            int requestedValue = value.ToObject<int>();
#pragma warning disable CS0618 // enumNames is obsolete but no non-obsolete API returns internal C# enum names
                            string[] validNames = prop.enumNames;
#pragma warning restore CS0618
                            return TrySetEnumInteger(
                                prop, requestedValue, warnings, fieldName, validNames);
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
                                objectReferenceWrite = new ObjectReferenceWrite(
                                    prop.objectReferenceValue, asset, false);
                                prop.objectReferenceValue = asset;
                                return true;
                            }
                            warnings?.Add($"Asset not found at '{assetRef}' for '{fieldName}'");
                        }
                        // Integer: try as instance ID
                        else if (value.Type == JTokenType.Integer)
                        {
                            int id = value.ToObject<int>();
                            JObject scopeError = PrefabSessionScope.TryResolveObjectByInstanceId(
                                id, out UnityEngine.Object obj);
                            if (scopeError != null)
                            {
                                warnings?.Add(FormatScopeError(scopeError));
                                return false;
                            }
                            if (obj != null)
                            {
                                JObject assignmentError = PrefabSessionScope.ValidateReferenceAssignment(
                                    prop.serializedObject?.targetObject, obj);
                                if (assignmentError != null)
                                {
                                    warnings?.Add(FormatScopeError(assignmentError));
                                    return false;
                                }

                                objectReferenceWrite = new ObjectReferenceWrite(
                                    prop.objectReferenceValue, obj, false);
                                prop.objectReferenceValue = obj;
                                return true;
                            }
                            warnings?.Add($"Object not found with instance ID {id} for '{fieldName}'");
                        }
                        // Structured reference: locator keys plus reader-emitted descriptive keys.
                        else if (value.Type == JTokenType.Object)
                        {
                            JObject refObj = (JObject)value;
                            string[] locatorKeys = { "assetPath", "instanceId", "objectPath" };
                            string[] validKeys = { "instanceId", "assetPath", "objectPath", "name", "type" };
                            var descriptiveKeys = new List<string>();
                            foreach (JProperty suppliedProperty in refObj.Properties())
                            {
                                bool knownKey = false;
                                foreach (string validKey in validKeys)
                                {
                                    if (suppliedProperty.Name == validKey)
                                    {
                                        knownKey = true;
                                        break;
                                    }
                                }
                                if (!knownKey)
                                {
                                    warnings?.Add(
                                        $"Unknown object reference key '{suppliedProperty.Name}' for '{fieldName}'. " +
                                        $"Valid keys: {string.Join(", ", validKeys)}");
                                    return false;
                                }
                                if (suppliedProperty.Name == "name" || suppliedProperty.Name == "type")
                                {
                                    descriptiveKeys.Add(suppliedProperty.Name);
                                }
                            }

                            if (descriptiveKeys.Count > 0)
                            {
                                warnings?.Add(
                                    $"Ignored descriptive keys: {string.Join(", ", descriptiveKeys)} " +
                                    $"for object reference '{fieldName}'");
                            }

                            var locatorFailures = new List<string>();
                            foreach (string locatorKey in locatorKeys)
                            {
                                if (!refObj.TryGetValue(locatorKey, out JToken locatorToken))
                                {
                                    continue;
                                }

                                if (TryResolveObjectLocator(
                                    locatorKey,
                                    locatorToken,
                                    prop.serializedObject?.targetObject,
                                    out UnityEngine.Object resolved,
                                    out string locatorFailure,
                                    out JObject scopeError))
                                {
                                    foreach (string priorFailure in locatorFailures)
                                    {
                                        warnings?.Add(
                                            $"{priorFailure}; resolved successfully via locator " +
                                            $"'{locatorKey}' (value {FormatLocatorValue(locatorToken)}) " +
                                            $"for '{fieldName}'");
                                    }

                                    objectReferenceWrite = new ObjectReferenceWrite(
                                        prop.objectReferenceValue, resolved, false);
                                    prop.objectReferenceValue = resolved;
                                    return true;
                                }

                                if (scopeError != null)
                                {
                                    locatorFailures.Add(
                                        $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) " +
                                        $"was rejected: {FormatScopeError(scopeError)}");
                                    continue;
                                }

                                locatorFailures.Add(locatorFailure);
                            }

                            if (locatorFailures.Count > 0)
                            {
                                warnings?.AddRange(locatorFailures);
                            }
                            else
                            {
                                warnings?.Add(
                                    $"Object reference for '{fieldName}' must provide one of: " +
                                    string.Join(", ", locatorKeys));
                            }
                            return false;
                        }
                        // Null: clear the reference
                        else if (value.Type == JTokenType.Null)
                        {
                            objectReferenceWrite = new ObjectReferenceWrite(
                                prop.objectReferenceValue, null, true);
                            prop.objectReferenceValue = null;
                            return true;
                        }
                        break;
                    case SerializedPropertyType.Bounds:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Bounds), prop.boundsValue, warnings, out object boundsValue))
                            return false;
                        prop.boundsValue = (Bounds)boundsValue;
                        return true;
                    case SerializedPropertyType.Quaternion:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Quaternion), prop.quaternionValue, warnings, out object quaternionValue))
                            return false;
                        prop.quaternionValue = (Quaternion)quaternionValue;
                        return true;
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

        /// <summary>
        /// Verify an applied object-reference write by reading the exact serialized property path
        /// from a fresh SerializedObject. Restores the previous reference when verification fails.
        /// </summary>
        public static bool VerifyObjectReferenceWrite(
            UnityEngine.Object target,
            SerializedObject originalSerializedObject,
            SerializedProperty originalProperty,
            string propertyPath,
            ObjectReferenceWrite objectReferenceWrite,
            out string failureReason)
        {
            failureReason = null;
            if (objectReferenceWrite == null)
            {
                return true;
            }

            var readBackObject = new SerializedObject(target);
            SerializedProperty readBackProperty = readBackObject.FindProperty(propertyPath);
            UnityEngine.Object readBackValue = readBackProperty?.objectReferenceValue;
            bool matches = objectReferenceWrite.IsIntentionalClear
                ? readBackValue == null
                : readBackValue == objectReferenceWrite.AttemptedValue;

            if (matches)
            {
                return true;
            }

            bool originalValueWasPreserved = readBackValue == objectReferenceWrite.PreviousValue;
            if (!originalValueWasPreserved && readBackProperty != null)
            {
                readBackProperty.objectReferenceValue = objectReferenceWrite.PreviousValue;
                readBackObject.ApplyModifiedProperties();
            }
            else if (!originalValueWasPreserved)
            {
                originalProperty.objectReferenceValue = objectReferenceWrite.PreviousValue;
                originalSerializedObject.ApplyModifiedProperties();
            }

            if (objectReferenceWrite.IsIntentionalClear)
            {
                failureReason = $"Object reference field '{propertyPath}' could not be cleared";
            }
            else
            {
                string resolvedType = objectReferenceWrite.AttemptedValue != null
                    ? objectReferenceWrite.AttemptedValue.GetType().Name
                    : "null";
                failureReason = $"Resolved object type {resolvedType} is not assignable to field " +
                    $"'{propertyPath}' (object-reference read-back did not retain the assigned identity)";
            }

            return false;
        }

        private static bool TryConvertUnityStructValue(
            JToken token,
            Type targetType,
            object currentValue,
            List<string> warnings,
            out object convertedValue)
        {
            var failures = new List<string>();
            bool converted = SerializedFieldConverter.TryConvertUnityStruct(
                token, targetType, currentValue, failures, out convertedValue);
            if (!converted)
            {
                warnings?.AddRange(failures);
            }
            return converted;
        }

        /// <summary>
        /// Validates integer writes for managed enum properties. Native bitfields such as
        /// Rigidbody.m_Constraints serialize as Integer and do not enter this enum path.
        /// </summary>
        private static bool TrySetEnumInteger(
            SerializedProperty prop,
            int requestedValue,
            List<string> warnings,
            string fieldName,
            string[] validNames)
        {
            int previousValue = prop.intValue;
            prop.intValue = requestedValue;
            if (prop.enumValueIndex != -1)
            {
                return true;
            }

            if (TryGetSerializedEnumType(prop, out Type enumType)
                && enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0
                && IsValidFlagsCombination(enumType, requestedValue))
            {
                return true;
            }

            prop.intValue = previousValue;
            warnings?.Add(
                $"Enum value '{requestedValue}' is not defined for '{fieldName}'. " +
                $"Valid names: {string.Join(", ", validNames)}. " +
                "Combined numeric values are accepted only for [Flags] enums when every bit is defined.");
            return false;
        }

        private static bool TryGetSerializedEnumType(SerializedProperty prop, out Type enumType)
        {
            enumType = null;
            UnityEngine.Object target = prop.serializedObject?.targetObject;
            if (target == null)
            {
                return false;
            }

            Type currentType = target.GetType();
            string normalizedPath = prop.propertyPath.Replace(".Array.data[", "[");
            string[] pathSegments = normalizedPath.Split('.');
            foreach (string pathSegment in pathSegments)
            {
                int bracketIndex = pathSegment.IndexOf('[', StringComparison.Ordinal);
                string fieldName = bracketIndex >= 0
                    ? pathSegment.Substring(0, bracketIndex)
                    : pathSegment;
                if (!TryGetSerializedMemberType(currentType, fieldName, out Type memberType))
                {
                    return false;
                }

                currentType = memberType;
                if (bracketIndex >= 0)
                {
                    currentType = GetCollectionElementType(currentType);
                    if (currentType == null)
                    {
                        return false;
                    }
                }
            }

            currentType = Nullable.GetUnderlyingType(currentType) ?? currentType;
            if (!currentType.IsEnum)
            {
                return false;
            }

            enumType = currentType;
            return true;
        }

        private static bool TryGetSerializedMemberType(
            Type declaringType,
            string serializedName,
            out Type memberType)
        {
            memberType = null;
            FieldInfo field = GetFieldInHierarchy(declaringType, serializedName);
            if (field == null)
            {
                string mappedFieldName = GetBidirectionalSerializedName(serializedName);
                if (!string.IsNullOrEmpty(mappedFieldName))
                {
                    field = GetFieldInHierarchy(declaringType, mappedFieldName);
                }
            }
            if (field != null)
            {
                memberType = field.FieldType;
                return true;
            }

            PropertyInfo property = GetPublicPropertyInHierarchy(declaringType, serializedName);
            if (property == null)
            {
                string mappedPropertyName = GetPublicPropertyName(serializedName);
                if (!string.IsNullOrEmpty(mappedPropertyName))
                {
                    property = GetPublicPropertyInHierarchy(declaringType, mappedPropertyName);
                }
            }
            if (property == null)
            {
                return false;
            }

            memberType = property.PropertyType;
            return true;
        }

        private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
        {
            for (Type currentType = type;
                 currentType != null && currentType != typeof(object);
                 currentType = currentType.BaseType)
            {
                FieldInfo field = currentType.GetField(
                    fieldName,
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }
            return null;
        }

        private static PropertyInfo GetPublicPropertyInHierarchy(Type type, string propertyName)
        {
            for (Type currentType = type;
                 currentType != null && currentType != typeof(object);
                 currentType = currentType.BaseType)
            {
                PropertyInfo property = currentType.GetProperty(
                    propertyName,
                    BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    return property;
                }
            }
            return null;
        }

        private static string GetBidirectionalSerializedName(string memberName)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                return null;
            }
            if (memberName.StartsWith("m_", StringComparison.Ordinal) && memberName.Length > 2)
            {
                return char.ToLowerInvariant(memberName[2]) + memberName.Substring(3);
            }
            return "m_" + char.ToUpperInvariant(memberName[0]) + memberName.Substring(1);
        }

        private static string GetPublicPropertyName(string serializedName)
        {
            if (serializedName == "m_ObjectHideFlags")
            {
                return "hideFlags";
            }
            return serializedName.StartsWith("m_", StringComparison.Ordinal)
                && serializedName.Length > 2
                ? char.ToLowerInvariant(serializedName[2]) + serializedName.Substring(3)
                : null;
        }

        private static Type GetCollectionElementType(Type collectionType)
        {
            Type elementType = collectionType.GetElementType();
            if (elementType != null)
            {
                return elementType;
            }

            if (collectionType.IsGenericType)
            {
                Type genericDefinition = collectionType.GetGenericTypeDefinition();
                if (genericDefinition == typeof(List<>)
                    || genericDefinition == typeof(IList<>))
                {
                    return collectionType.GetGenericArguments()[0];
                }
            }

            foreach (Type interfaceType in collectionType.GetInterfaces())
            {
                if (interfaceType.IsGenericType
                    && interfaceType.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    return interfaceType.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static bool IsValidFlagsCombination(Type enumType, int requestedValue)
        {
            object candidate = Enum.ToObject(enumType, requestedValue);
            Type underlyingType = Enum.GetUnderlyingType(enumType);
            bool roundTrips;
            if (underlyingType == typeof(byte) || underlyingType == typeof(ushort))
            {
                roundTrips = requestedValue >= 0
                    && Convert.ToUInt64(candidate) == (ulong)requestedValue;
            }
            else if (underlyingType == typeof(uint) || underlyingType == typeof(ulong))
            {
                roundTrips = Convert.ToUInt64(candidate) == unchecked((uint)requestedValue);
            }
            else
            {
                roundTrips = Convert.ToInt64(candidate) == requestedValue;
            }
            if (!roundTrips)
            {
                return false;
            }

            ulong candidateBits = EnumValueToUInt64(candidate, enumType);
            ulong allDefinedBits = 0;
            foreach (object definedValue in Enum.GetValues(enumType))
            {
                allDefinedBits |= EnumValueToUInt64(definedValue, enumType);
            }
            return (candidateBits & ~allDefinedBits) == 0;
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

        private static bool TryResolveObjectLocator(
            string locatorKey,
            JToken locatorToken,
            UnityEngine.Object referenceOwner,
            out UnityEngine.Object resolved,
            out string failure,
            out JObject scopeError)
        {
            resolved = null;
            failure = null;
            scopeError = null;
            try
            {
                switch (locatorKey)
                {
                    case "assetPath":
                        string assetPath = locatorToken?.ToObject<string>();
                        resolved = string.IsNullOrEmpty(assetPath)
                            ? null
                            : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                        break;
                    case "instanceId":
                        scopeError = PrefabSessionScope.TryResolveObjectByInstanceId(
                            locatorToken.ToObject<int>(), out resolved);
                        if (scopeError != null)
                            return false;
                        break;
                    case "objectPath":
                        string objectPath = locatorToken?.ToObject<string>();
                        if (string.IsNullOrEmpty(objectPath))
                        {
                            resolved = null;
                        }
                        else
                        {
                            scopeError = PrefabSessionScope.TryResolveGameObject(
                                null, objectPath, out GameObject gameObject);
                            if (scopeError != null)
                                return false;
                            resolved = gameObject;
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

            if (resolved != null)
            {
                JObject assignmentError = PrefabSessionScope.ValidateReferenceAssignment(
                    referenceOwner, resolved);
                if (assignmentError != null)
                {
                    scopeError = assignmentError;
                    resolved = null;
                    return false;
                }
                return true;
            }

            failure =
                $"Locator '{locatorKey}' (value {FormatLocatorValue(locatorToken)}) failed to resolve";
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
    }
}
