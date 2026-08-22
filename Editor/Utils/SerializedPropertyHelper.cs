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
        internal const int MaxDirectArraySize = 10000;

        public sealed class ObjectReferenceWrite
        {
            public UnityEngine.Object PreviousValue { get; }
            public int PreviousInstanceId { get; }
            public UnityEngine.Object AttemptedValue { get; }
            public bool IsIntentionalClear { get; }

            public ObjectReferenceWrite(
                UnityEngine.Object previousValue,
                UnityEngine.Object attemptedValue,
                bool isIntentionalClear)
                : this(
                    previousValue,
                    previousValue != null ? previousValue.GetInstanceID() : 0,
                    attemptedValue,
                    isIntentionalClear)
            {
            }

            public ObjectReferenceWrite(
                UnityEngine.Object previousValue,
                int previousInstanceId,
                UnityEngine.Object attemptedValue,
                bool isIntentionalClear)
            {
                PreviousValue = previousValue;
                PreviousInstanceId = previousInstanceId;
                AttemptedValue = attemptedValue;
                IsIntentionalClear = isIntentionalClear;
            }
        }

        internal sealed class ObjectReferenceWriteRecord
        {
            public string PropertyPath { get; }
            public ObjectReferenceWrite Write { get; }
            public bool FieldIncludesNonReferenceWrites { get; set; }
            public List<string> UnrestoredArraySizeChanges { get; } = new List<string>();

            public ObjectReferenceWriteRecord(string propertyPath, ObjectReferenceWrite write)
            {
                PropertyPath = propertyPath;
                Write = write;
            }
        }

        private sealed class ObjectReferenceWriteContext
        {
            public List<ObjectReferenceWriteRecord> Writes { get; } =
                new List<ObjectReferenceWriteRecord>();
            public bool IncludesNonReferenceWrites { get; set; }
            public List<string> ArraySizeChanges { get; } = new List<string>();
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
        /// Supports: Generic arrays/objects, ArraySize, Integer, Boolean, Float, String, LayerMask,
        /// Color, Vector2/3/4, Rect,
        /// Enum, ObjectReference (asset path/instanceId/GUID/structured with both assetPath and objectPath),
        /// Bounds, Quaternion, and null clearing for ObjectReference.
        /// </summary>
        public static bool SetValue(SerializedProperty prop, JToken value, List<string> warnings, string fieldName)
        {
            List<ObjectReferenceWriteRecord> ignoredWrites;
            return SetValue(prop, value, warnings, fieldName, out ignoredWrites);
        }

        /// <summary>
        /// Compatibility overload for callers that only write one top-level object reference.
        /// </summary>
        public static bool SetValue(
            SerializedProperty prop,
            JToken value,
            List<string> warnings,
            string fieldName,
            out ObjectReferenceWrite objectReferenceWrite)
        {
            bool success = SetValue(
                prop,
                value,
                warnings,
                fieldName,
                out List<ObjectReferenceWriteRecord> objectReferenceWrites);
            if (objectReferenceWrites.Count > 1)
            {
                warnings?.Add(
                    $"SerializedProperty write collected {objectReferenceWrites.Count} object-reference " +
                    "assignments; this compatibility overload returns only the first assignment");
            }
            objectReferenceWrite = objectReferenceWrites.Count > 0
                ? objectReferenceWrites[0].Write
                : null;
            return success;
        }

        /// <summary>
        /// Set a SerializedProperty value and collect every nested object-reference assignment for
        /// read-back verification after ApplyModifiedProperties.
        /// </summary>
        internal static bool SetValue(
            SerializedProperty prop,
            JToken value,
            List<string> warnings,
            string fieldName,
            out List<ObjectReferenceWriteRecord> objectReferenceWrites)
        {
            var context = new ObjectReferenceWriteContext();
            bool success = SetValueInternal(prop, value, warnings, fieldName, context);
            objectReferenceWrites = context.Writes;
            if (success)
            {
                foreach (ObjectReferenceWriteRecord record in objectReferenceWrites)
                {
                    record.FieldIncludesNonReferenceWrites = context.IncludesNonReferenceWrites;
                    record.UnrestoredArraySizeChanges.AddRange(context.ArraySizeChanges);
                }
            }
            return success;
        }

        private static bool SetValueInternal(
            SerializedProperty prop,
            JToken value,
            List<string> warnings,
            string fieldName,
            ObjectReferenceWriteContext context)
        {
            AddPersistentCallsWarning(prop, warnings);
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Generic:
                        return SetGenericValue(
                            prop, value, warnings, fieldName, context);
                    case SerializedPropertyType.Integer:
                        prop.intValue = value.ToObject<int>();
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.ArraySize:
                        if (value.Type != JTokenType.Integer)
                        {
                            warnings?.Add(
                                $"Array size '{prop.propertyPath}' for '{fieldName}' expects an integer value");
                            return false;
                        }
                        int previousSize = prop.intValue;
                        int requestedSize = value.ToObject<int>();
                        const string arraySizeSuffix = ".Array.size";
                        if (prop.propertyPath.EndsWith(arraySizeSuffix, StringComparison.Ordinal))
                        {
                            string parentPath = prop.propertyPath.Substring(
                                0, prop.propertyPath.Length - arraySizeSuffix.Length);
                            SerializedProperty parentProperty =
                                prop.serializedObject?.FindProperty(parentPath);
                            if (parentProperty != null
                                && parentProperty.propertyType == SerializedPropertyType.String)
                            {
                                warnings?.Add(
                                    $"Array size '{prop.propertyPath}' for '{fieldName}' cannot resize " +
                                    "a string via Array.size");
                                return false;
                            }
                        }
                        if (requestedSize < 0)
                        {
                            warnings?.Add(
                                $"Array size '{prop.propertyPath}' for '{fieldName}' cannot be negative");
                            return false;
                        }
                        if (requestedSize > MaxDirectArraySize)
                        {
                            warnings?.Add(
                                $"Array size '{prop.propertyPath}' for '{fieldName}' exceeds the direct " +
                                $"Array.size limit of {MaxDirectArraySize}");
                            return false;
                        }
                        if (requestedSize > previousSize)
                        {
                            string growthBehavior = previousSize == 0
                                ? "Unity initializes the new elements with default values"
                                : "Unity grow duplicates the last element value";
                            warnings?.Add(
                                $"Growing array size '{prop.propertyPath}' from {previousSize} to " +
                                $"{requestedSize}: {growthBehavior}");
                        }
                        prop.intValue = requestedSize;
                        if (requestedSize != previousSize)
                        {
                            context.ArraySizeChanges.Add(
                                $"'{prop.propertyPath}' from {previousSize} to {requestedSize} elements");
                        }
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = value.ToObject<bool>();
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = value.ToObject<float>();
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value.ToObject<string>();
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.LayerMask:
                        if (value.Type != JTokenType.Integer)
                        {
                            warnings?.Add(
                                $"LayerMask property '{prop.propertyPath}' for '{fieldName}' expects an integer value");
                            return false;
                        }
                        prop.intValue = value.ToObject<int>();
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Color:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Color), prop.colorValue, warnings, out object colorValue))
                            return false;
                        prop.colorValue = (Color)colorValue;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Vector2:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector2), prop.vector2Value, warnings, out object vector2Value))
                            return false;
                        prop.vector2Value = (Vector2)vector2Value;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Vector3:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector3), prop.vector3Value, warnings, out object vector3Value))
                            return false;
                        prop.vector3Value = (Vector3)vector3Value;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Vector4:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Vector4), prop.vector4Value, warnings, out object vector4Value))
                            return false;
                        prop.vector4Value = (Vector4)vector4Value;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Rect:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Rect), prop.rectValue, warnings, out object rectValue))
                            return false;
                        prop.rectValue = (Rect)rectValue;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Enum:
                        JObject enumReaderShape = value as JObject;
                        if (enumReaderShape != null)
                        {
                            string[] allowedEnumKeys = { "value", "index", "name" };
                            foreach (JProperty suppliedKey in enumReaderShape.Properties())
                            {
                                if (Array.IndexOf(allowedEnumKeys, suppliedKey.Name) < 0)
                                {
                                    warnings?.Add(
                                        $"Unknown enum key '{suppliedKey.Name}' for '{fieldName}'. " +
                                        $"Valid reader-shape keys: {string.Join(", ", allowedEnumKeys)}");
                                    return false;
                                }
                            }
                            if (!enumReaderShape.TryGetValue("value", out JToken underlyingValue))
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
                                    AddEnumReaderShapeMismatchWarnings(
                                        prop, enumReaderShape, warnings, fieldName);
                                    context.IncludesNonReferenceWrites = true;
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
                                    AddEnumReaderShapeMismatchWarnings(
                                        prop, enumReaderShape, warnings, fieldName);
                                    context.IncludesNonReferenceWrites = true;
                                    return true;
                                }
                            }

                            if (int.TryParse(
                                strValue,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int numericValue))
                            {
                                bool written = TrySetEnumInteger(
                                    prop, numericValue, warnings, fieldName, internalNames);
                                if (written)
                                {
                                    AddEnumReaderShapeMismatchWarnings(
                                        prop, enumReaderShape, warnings, fieldName);
                                    context.IncludesNonReferenceWrites = true;
                                }
                                return written;
                            }

                            warnings?.Add(
                                $"Enum value '{strValue}' not found for '{fieldName}'. " +
                                $"Valid names: {string.Join(", ", internalNames)}");
                            return false;
                        }
                        else if (value.Type == JTokenType.Integer)
                        {
                            int requestedValue = value.ToObject<int>();
#pragma warning disable CS0618 // enumNames is obsolete but no non-obsolete API returns internal C# enum names
                            string[] validNames = prop.enumNames;
#pragma warning restore CS0618
                            bool written = TrySetEnumInteger(
                                prop, requestedValue, warnings, fieldName, validNames);
                            if (written)
                            {
                                AddEnumReaderShapeMismatchWarnings(
                                    prop, enumReaderShape, warnings, fieldName);
                                context.IncludesNonReferenceWrites = true;
                            }
                            return written;
                        }
#pragma warning disable CS0618 // enumNames is obsolete but no non-obsolete API returns internal C# enum names
                        string[] expectedEnumNames = prop.enumNames;
#pragma warning restore CS0618
                        warnings?.Add(
                            $"Expected an enum name or integer for '{fieldName}'. " +
                            $"Valid names: {string.Join(", ", expectedEnumNames)}");
                        return false;
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
                                return TryAssignObjectReference(
                                    prop, asset, false, warnings, fieldName, context);
                            }
                            warnings?.Add($"Asset not found at '{assetRef}' for '{fieldName}'");
                            return false;
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

                                return TryAssignObjectReference(
                                    prop, obj, false, warnings, fieldName, context);
                            }
                            warnings?.Add($"Object not found with instance ID {id} for '{fieldName}'");
                            return false;
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
                                if (locatorToken.Type == JTokenType.Null
                                    || (locatorToken.Type == JTokenType.String
                                        && ((JValue)locatorToken).Value == null))
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

                                    return TryAssignObjectReference(
                                        prop, resolved, false, warnings, fieldName, context);
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
                            return TryAssignObjectReference(
                                prop, null, true, warnings, fieldName, context);
                        }
                        warnings?.Add(
                            $"Object reference property '{prop.propertyPath}' for '{fieldName}' expects " +
                            "an asset path, instance ID, structured locator object, or null");
                        return false;
                    case SerializedPropertyType.Bounds:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Bounds), prop.boundsValue, warnings, out object boundsValue))
                            return false;
                        prop.boundsValue = (Bounds)boundsValue;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.Quaternion:
                        if (!TryConvertUnityStructValue(
                            value, typeof(Quaternion), prop.quaternionValue, warnings, out object quaternionValue))
                            return false;
                        prop.quaternionValue = (Quaternion)quaternionValue;
                        context.IncludesNonReferenceWrites = true;
                        return true;
                    case SerializedPropertyType.ManagedReference:
                        warnings?.Add(
                            $"Managed reference property '{prop.propertyPath}' for '{fieldName}' is not " +
                            "supported for direct SerializedProperty writes");
                        return false;
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

        private static bool SetGenericValue(
            SerializedProperty prop,
            JToken value,
            List<string> warnings,
            string fieldName,
            ObjectReferenceWriteContext context)
        {
            if (prop.isFixedBuffer)
            {
                warnings?.Add(
                    $"Fixed buffer property '{prop.propertyPath}' for '{fieldName}' is not supported");
                return false;
            }

            if (prop.isArray)
            {
                if (value.Type != JTokenType.Array)
                {
                    warnings?.Add(
                        $"Array property '{prop.propertyPath}' for '{fieldName}' expects a JArray value");
                    return false;
                }

                JArray arrayValue = (JArray)value;
                int previousSize = prop.arraySize;
                if (arrayValue.Count < previousSize)
                {
                    warnings?.Add(
                        $"Shrinking array '{prop.propertyPath}' from {previousSize} to " +
                        $"{arrayValue.Count} elements; the removed elements are discarded");
                }
                prop.arraySize = arrayValue.Count;
                if (arrayValue.Count != previousSize)
                {
                    context.ArraySizeChanges.Add(
                        $"'{prop.propertyPath}' from {previousSize} to {arrayValue.Count} elements");
                }
                for (int i = previousSize; i < arrayValue.Count; i++)
                {
                    SerializedProperty grownElement = prop.GetArrayElementAtIndex(i);
                    if (!ClearToDefault(grownElement, out bool clearedNonReferenceValue))
                    {
                        warnings?.Add(
                            $"Could not clear grown array slot '{grownElement.propertyPath}' to type " +
                            $"defaults because property type '{grownElement.propertyType}' is unsupported; " +
                            "the slot retains Unity's duplicated value");
                    }
                    else if (clearedNonReferenceValue)
                    {
                        context.IncludesNonReferenceWrites = true;
                    }
                }
                for (int i = 0; i < arrayValue.Count; i++)
                {
                    SerializedProperty element = prop.GetArrayElementAtIndex(i);
                    if (!SetValueInternal(
                        element,
                        arrayValue[i],
                        warnings,
                        element.propertyPath,
                        context))
                    {
                        return false;
                    }
                }
                return true;
            }

            if (value.Type != JTokenType.Object)
            {
                warnings?.Add(
                    $"Generic property '{prop.propertyPath}' for '{fieldName}' expects a JObject value");
                return false;
            }

            List<string> childNames = GetDirectChildNames(prop);
            JObject objectValue = (JObject)value;
            foreach (JProperty suppliedChild in objectValue.Properties())
            {
                if (!childNames.Contains(suppliedChild.Name))
                {
                    warnings?.Add(
                        $"Unknown child key '{suppliedChild.Name}' for '{prop.propertyPath}'. " +
                        $"Valid child names: {FormatChildNames(childNames)}");
                    return false;
                }

                SerializedProperty child = prop.FindPropertyRelative(suppliedChild.Name);
                if (child == null)
                {
                    warnings?.Add(
                        $"Unknown child key '{suppliedChild.Name}' for '{prop.propertyPath}'. " +
                        $"Valid child names: {FormatChildNames(childNames)}");
                    return false;
                }

                if (!SetValueInternal(
                    child,
                    suppliedChild.Value,
                    warnings,
                    child.propertyPath,
                    context))
                {
                    return false;
                }
            }
            return true;
        }

        private static List<string> GetDirectChildNames(SerializedProperty prop)
        {
            var childNames = new List<string>();
            SerializedProperty iterator = prop.Copy();
            SerializedProperty end = prop.GetEndProperty(true);
            bool enterChildren = true;
            while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                childNames.Add(iterator.name);
            }
            return childNames;
        }

        private static string FormatChildNames(List<string> childNames)
        {
            return childNames.Count == 0 ? "(none)" : string.Join(", ", childNames);
        }

        private static bool TryAssignObjectReference(
            SerializedProperty prop,
            UnityEngine.Object resolved,
            bool isIntentionalClear,
            List<string> warnings,
            string fieldName,
            ObjectReferenceWriteContext context)
        {
            if (resolved != null
                && !IsResolvedObjectReferenceTypeCompatible(prop, resolved, warnings, fieldName))
            {
                return false;
            }

            context.Writes.Add(new ObjectReferenceWriteRecord(
                prop.propertyPath,
                new ObjectReferenceWrite(
                    prop.objectReferenceValue,
                    prop.objectReferenceInstanceIDValue,
                    resolved,
                    isIntentionalClear)));
            prop.objectReferenceValue = resolved;
            return true;
        }

        private static bool IsResolvedObjectReferenceTypeCompatible(
            SerializedProperty prop,
            UnityEngine.Object resolved,
            List<string> warnings,
            string fieldName)
        {
            const string typePrefix = "PPtr<$";
            string serializedType = prop.type;
            if (string.IsNullOrEmpty(serializedType)
                || !serializedType.StartsWith(typePrefix, StringComparison.Ordinal)
                || !serializedType.EndsWith(">", StringComparison.Ordinal))
            {
                return true;
            }

            string expectedTypeName = serializedType.Substring(
                typePrefix.Length,
                serializedType.Length - typePrefix.Length - 1);
            if (string.IsNullOrEmpty(expectedTypeName))
            {
                return true;
            }

            for (Type resolvedType = resolved.GetType();
                resolvedType != null;
                resolvedType = resolvedType.BaseType)
            {
                if (string.Equals(resolvedType.Name, expectedTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            warnings?.Add(
                $"Resolved object type {resolved.GetType().Name} is not assignable to object-reference " +
                $"field '{prop.propertyPath}' for '{fieldName}' (expected {expectedTypeName})");
            return false;
        }

        private static bool ClearToDefault(
            SerializedProperty prop,
            out bool clearedNonReferenceValue)
        {
            clearedNonReferenceValue = false;
            if (!CanClearToDefault(prop))
            {
                return false;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (prop.isArray)
                    {
                        clearedNonReferenceValue = prop.arraySize > 0;
                        prop.arraySize = 0;
                        return true;
                    }
                    foreach (SerializedProperty child in GetDirectChildren(prop))
                    {
                        ClearToDefault(child, out bool childClearedNonReferenceValue);
                        clearedNonReferenceValue |= childClearedNonReferenceValue;
                    }
                    return true;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    prop.intValue = 0;
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = false;
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Float:
                    prop.floatValue = 0f;
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = string.Empty;
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = null;
                    return true;
                case SerializedPropertyType.Enum:
                    prop.intValue = 0;
                    if (prop.enumValueIndex == -1)
                    {
                        prop.enumValueIndex = 0;
                    }
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Color:
                    prop.colorValue = default(Color);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = default(Vector2);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = default(Vector3);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Vector4:
                    prop.vector4Value = default(Vector4);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Vector2Int:
                    prop.vector2IntValue = default(Vector2Int);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Vector3Int:
                    prop.vector3IntValue = default(Vector3Int);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Rect:
                    prop.rectValue = default(Rect);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.RectInt:
                    prop.rectIntValue = default(RectInt);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Bounds:
                    prop.boundsValue = default(Bounds);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.BoundsInt:
                    prop.boundsIntValue = default(BoundsInt);
                    clearedNonReferenceValue = true;
                    return true;
                case SerializedPropertyType.Quaternion:
                    // Deliberately differs from C# default(Quaternion): (0, 0, 0, 0) is not a
                    // valid rotation, while identity is Unity's semantic "no rotation" default.
                    prop.quaternionValue = Quaternion.identity;
                    clearedNonReferenceValue = true;
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanClearToDefault(SerializedProperty prop)
        {
            if (prop.propertyType == SerializedPropertyType.Generic)
            {
                if (prop.isFixedBuffer)
                {
                    return false;
                }
                if (prop.isArray)
                {
                    return true;
                }
                foreach (SerializedProperty child in GetDirectChildren(prop))
                {
                    if (!CanClearToDefault(child))
                    {
                        return false;
                    }
                }
                return true;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Float:
                case SerializedPropertyType.String:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.Quaternion:
                    return true;
                default:
                    return false;
            }
        }

        private static IEnumerable<SerializedProperty> GetDirectChildren(SerializedProperty prop)
        {
            SerializedProperty iterator = prop.Copy();
            SerializedProperty end = prop.GetEndProperty(true);
            bool enterChildren = true;
            while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                yield return iterator.Copy();
            }
        }

        private static void AddPersistentCallsWarning(
            SerializedProperty prop,
            List<string> warnings)
        {
            if (warnings == null
                || prop == null
                || !ContainsPropertyPathSegment(prop.propertyPath, "m_PersistentCalls"))
            {
                return;
            }

            const string warningPrefix = "Direct m_PersistentCalls writes bypass UnityEvent mode derivation validation";
            foreach (string existingWarning in warnings)
            {
                if (existingWarning.StartsWith(warningPrefix, StringComparison.Ordinal))
                {
                    return;
                }
            }

            warnings.Add(
                $"{warningPrefix} (target path '{prop.propertyPath}'); prefer wire_unity_event");
        }

        private static bool ContainsPropertyPathSegment(string propertyPath, string segment)
        {
            int searchIndex = 0;
            while (searchIndex < propertyPath.Length)
            {
                int matchIndex = propertyPath.IndexOf(segment, searchIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    return false;
                }

                bool beginsAtBoundary = matchIndex == 0 || propertyPath[matchIndex - 1] == '.';
                int endIndex = matchIndex + segment.Length;
                bool endsAtBoundary = endIndex == propertyPath.Length || propertyPath[endIndex] == '.';
                if (beginsAtBoundary && endsAtBoundary)
                {
                    return true;
                }
                searchIndex = matchIndex + 1;
            }
            return false;
        }

        /// <summary>
        /// Verify every object-reference write for one applied field. If any path fails read-back,
        /// restores every safe collected object reference in one no-undo rollback apply, skips
        /// missing-reference previous values, and verifies restored identities from a fresh read.
        /// </summary>
        internal static bool VerifyObjectReferenceWrites(
            UnityEngine.Object target,
            List<ObjectReferenceWriteRecord> objectReferenceWrites,
            out string failureReason)
        {
            failureReason = null;
            if (objectReferenceWrites == null || objectReferenceWrites.Count == 0)
            {
                return true;
            }

            var readBackObject = new SerializedObject(target);
            var verificationFailures = new List<string>();
            var alreadyRestoredPaths = new HashSet<string>(StringComparer.Ordinal);
            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectReferenceWriteRecord record in objectReferenceWrites)
            {
                allPaths.Add(record.PropertyPath);
                SerializedProperty readBackProperty = readBackObject.FindProperty(record.PropertyPath);
                if (readBackProperty == null)
                {
                    verificationFailures.Add(
                        $"Object reference field '{record.PropertyPath}' could not be verified: " +
                        "property path no longer resolves");
                    continue;
                }

                UnityEngine.Object readBackValue = readBackProperty.objectReferenceValue;
                int readBackInstanceId = readBackProperty.objectReferenceInstanceIDValue;
                bool matches = record.Write.IsIntentionalClear
                    ? readBackValue == null && readBackInstanceId == 0
                    : readBackValue == record.Write.AttemptedValue;
                if (matches)
                {
                    continue;
                }

                if (MatchesPreviousObjectReference(
                    readBackValue, readBackInstanceId, record.Write))
                {
                    alreadyRestoredPaths.Add(record.PropertyPath);
                }
                verificationFailures.Add(
                    GetObjectReferenceVerificationFailure(record.PropertyPath, record.Write));
            }

            if (verificationFailures.Count == 0)
            {
                return true;
            }

            var rollbackObject = new SerializedObject(target);
            var rollbackFailures = new List<string>();
            var rollbackCandidates = new HashSet<string>(StringComparer.Ordinal);
            var restoredPaths = new HashSet<string>(alreadyRestoredPaths, StringComparer.Ordinal);
            var skippedMissingReferencePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectReferenceWriteRecord record in objectReferenceWrites)
            {
                if (alreadyRestoredPaths.Contains(record.PropertyPath))
                {
                    continue;
                }
                SerializedProperty rollbackProperty = rollbackObject.FindProperty(record.PropertyPath);
                if (rollbackProperty == null)
                {
                    rollbackFailures.Add(
                        $"'{record.PropertyPath}': property path no longer resolves during rollback");
                    continue;
                }
                if (record.Write.PreviousValue == null && record.Write.PreviousInstanceId != 0)
                {
                    skippedMissingReferencePaths.Add(record.PropertyPath);
                    continue;
                }
                try
                {
                    rollbackProperty.objectReferenceValue = record.Write.PreviousValue;
                    rollbackCandidates.Add(record.PropertyPath);
                }
                catch (Exception ex)
                {
                    rollbackFailures.Add(
                        $"'{record.PropertyPath}': could not stage the previous reference ({ex.Message})");
                }
            }

            if (rollbackCandidates.Count > 0)
            {
                try
                {
                    rollbackObject.ApplyModifiedPropertiesWithoutUndo();
                }
                catch (Exception ex)
                {
                    foreach (string path in rollbackCandidates)
                    {
                        rollbackFailures.Add(
                            $"'{path}': rollback apply failed ({ex.Message})");
                    }
                    rollbackCandidates.Clear();
                }
            }

            if (rollbackCandidates.Count > 0)
            {
                var rollbackReadBackObject = new SerializedObject(target);
                foreach (ObjectReferenceWriteRecord record in objectReferenceWrites)
                {
                    if (!rollbackCandidates.Contains(record.PropertyPath))
                    {
                        continue;
                    }

                    SerializedProperty restoredProperty =
                        rollbackReadBackObject.FindProperty(record.PropertyPath);
                    if (restoredProperty == null)
                    {
                        rollbackFailures.Add(
                            $"'{record.PropertyPath}': property path no longer resolves after rollback");
                        continue;
                    }
                    if (!MatchesPreviousObjectReference(
                        restoredProperty.objectReferenceValue,
                        restoredProperty.objectReferenceInstanceIDValue,
                        record.Write))
                    {
                        rollbackFailures.Add(
                            $"'{record.PropertyPath}': rollback read-back did not retain the previous identity");
                        continue;
                    }
                    restoredPaths.Add(record.PropertyPath);
                }
            }

            bool includesNonReferenceWrites = false;
            var arraySizeChanges = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectReferenceWriteRecord record in objectReferenceWrites)
            {
                includesNonReferenceWrites |= record.FieldIncludesNonReferenceWrites;
                foreach (string arraySizeChange in record.UnrestoredArraySizeChanges)
                {
                    arraySizeChanges.Add(arraySizeChange);
                }
            }

            var disclosures = new List<string>();
            if (restoredPaths.SetEquals(allPaths)
                && skippedMissingReferencePaths.Count == 0
                && rollbackFailures.Count == 0)
            {
                disclosures.Add(
                    "all collected object-reference writes were restored");
            }
            disclosures.Add(
                $"object-reference writes restored: [{FormatPaths(restoredPaths)}]");
            disclosures.Add(
                "skipped (missing-reference previous): [" +
                FormatSkippedMissingReferencePaths(skippedMissingReferencePaths) + "]");
            disclosures.Add(
                $"rollback failures: [{FormatList(rollbackFailures)}]");
            if (arraySizeChanges.Count > 0)
            {
                disclosures.Add(
                    $"array size changes were not rolled back: [{FormatList(arraySizeChanges)}]");
            }
            if (includesNonReferenceWrites)
            {
                disclosures.Add(
                    "Non-reference children of this field may already have been applied");
            }

            failureReason = string.Join("; ", verificationFailures.ToArray()) + "; " +
                string.Join("; ", disclosures.ToArray()) + ".";
            return false;
        }

        private static bool MatchesPreviousObjectReference(
            UnityEngine.Object value,
            int instanceId,
            ObjectReferenceWrite write)
        {
            return write.PreviousValue != null
                ? value == write.PreviousValue
                : value == null && instanceId == write.PreviousInstanceId;
        }

        private static string FormatPaths(IEnumerable<string> paths)
        {
            var formatted = new List<string>();
            foreach (string path in paths)
            {
                formatted.Add($"'{path}'");
            }
            formatted.Sort(StringComparer.Ordinal);
            return formatted.Count == 0 ? "none" : string.Join(", ", formatted);
        }

        private static string FormatList(IEnumerable<string> values)
        {
            var formatted = new List<string>(values);
            formatted.Sort(StringComparer.Ordinal);
            return formatted.Count == 0 ? "none" : string.Join(", ", formatted);
        }

        private static string FormatSkippedMissingReferencePaths(IEnumerable<string> paths)
        {
            var formatted = new List<string>();
            foreach (string path in paths)
            {
                formatted.Add(
                    $"'{path}' — its previous value was a missing reference; the newly written value " +
                    "was retained to avoid destroying the missing-reference GUID");
            }
            formatted.Sort(StringComparer.Ordinal);
            return formatted.Count == 0 ? "none" : string.Join(", ", formatted);
        }

        private static string GetObjectReferenceVerificationFailure(
            string propertyPath,
            ObjectReferenceWrite objectReferenceWrite)
        {
            if (objectReferenceWrite.IsIntentionalClear)
            {
                return $"Object reference field '{propertyPath}' could not be cleared";
            }

            string resolvedType = objectReferenceWrite.AttemptedValue != null
                ? objectReferenceWrite.AttemptedValue.GetType().Name
                : "null";
            return $"Resolved object type {resolvedType} is not assignable to field " +
                $"'{propertyPath}' (object-reference read-back did not retain the assigned identity)";
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

        private static void AddEnumReaderShapeMismatchWarnings(
            SerializedProperty prop,
            JObject readerShape,
            List<string> warnings,
            string fieldName)
        {
            if (readerShape == null || warnings == null)
            {
                return;
            }

#pragma warning disable CS0618 // enumNames is obsolete but no non-obsolete API returns internal C# enum names
            string[] enumNames = prop.enumNames;
#pragma warning restore CS0618
            bool hasEnumType = TryGetSerializedEnumType(prop, out Type enumType);
            string resolvedName = hasEnumType
                ? Enum.ToObject(enumType, prop.intValue).ToString()
                : enumNames != null
                    && prop.enumValueIndex >= 0
                    && prop.enumValueIndex < enumNames.Length
                        ? enumNames[prop.enumValueIndex]
                        : prop.intValue.ToString(CultureInfo.InvariantCulture);

            if (readerShape.TryGetValue("name", out JToken suppliedNameToken))
            {
                string suppliedName = suppliedNameToken.Type == JTokenType.Null
                    ? null
                    : suppliedNameToken.ToString();
                bool suppliedNameMatches = false;

                if (suppliedName != null
                    && hasEnumType
                    && Enum.TryParse(enumType, suppliedName, true, out object suppliedEnumValue))
                {
                    suppliedNameMatches = suppliedEnumValue.Equals(
                        Enum.ToObject(enumType, prop.intValue));
                }

                if (suppliedName != null)
                {
                    string[] displayNames = prop.enumDisplayNames;
                    for (int i = 0; displayNames != null && i < displayNames.Length; i++)
                    {
                        if (i == prop.enumValueIndex
                            && string.Equals(
                                displayNames[i], suppliedName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            suppliedNameMatches = true;
                            break;
                        }
                    }

                    for (int i = 0; enumNames != null && i < enumNames.Length; i++)
                    {
                        if (i == prop.enumValueIndex
                            && string.Equals(
                                enumNames[i], suppliedName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            suppliedNameMatches = true;
                            break;
                        }
                    }

                    suppliedNameMatches = suppliedNameMatches
                        || string.Equals(
                            suppliedName, resolvedName, StringComparison.OrdinalIgnoreCase);
                }

                if (!suppliedNameMatches)
                {
                    warnings.Add(
                        $"Reader-shaped enum metadata mismatch for '{fieldName}': supplied name " +
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
                if (!hasIntegerIndex || suppliedIndexValue != prop.enumValueIndex)
                {
                    warnings.Add(
                        $"Reader-shaped enum metadata mismatch for '{fieldName}': supplied index " +
                        $"'{suppliedIndex}', but 'value' resolved to index {prop.enumValueIndex}. Used 'value'.");
                }
            }
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
