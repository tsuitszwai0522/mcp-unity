using System.Collections.Generic;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Shared helpers for Unity Localization MCP tools — collection lookup, locale resolution, dirty/save.
    /// </summary>
    internal static class LocTableHelper
    {
        public const string DefaultLocale = "zh-TW";

        /// <summary>
        /// Find a StringTableCollection by name. Returns null and fills <paramref name="error"/> if missing.
        /// </summary>
        public static StringTableCollection ResolveCollection(string name, out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'table_name' must be provided",
                    "validation_error");
                return null;
            }

            var collections = LocalizationEditorSettings.GetStringTableCollections();
            var match = collections.FirstOrDefault(c => c.TableCollectionName == name);
            if (match != null) return match;

            var available = string.Join(", ", collections.Select(c => c.TableCollectionName));
            error = McpUnitySocketHandler.CreateErrorResponse(
                $"StringTable '{name}' not found. Available: [{available}]",
                "table_not_found");
            return null;
        }

        /// <summary>
        /// Resolve a StringTable for the given collection + locale code (defaults to zh-TW).
        /// Returns null and fills <paramref name="error"/> if locale missing.
        /// </summary>
        public static StringTable ResolveTable(
            StringTableCollection collection,
            string localeCode,
            out JObject error)
        {
            error = null;
            var code = string.IsNullOrWhiteSpace(localeCode) ? DefaultLocale : localeCode;
            var identifier = new LocaleIdentifier(code);

            var table = collection.GetTable(identifier) as StringTable;
            if (table != null) return table;

            var availableLocales = collection.StringTables
                .Where(t => t != null)
                .Select(t => t.LocaleIdentifier.Code);
            error = McpUnitySocketHandler.CreateErrorResponse(
                $"Locale '{code}' not configured for table '{collection.TableCollectionName}'. Available: [{string.Join(", ", availableLocales)}]",
                "locale_not_found");
            return null;
        }

        /// <summary>
        /// Validate a key — non-empty, no leading/trailing whitespace.
        /// </summary>
        public static bool ValidateKey(string key, out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'key' must be a non-empty string",
                    "validation_error");
                return false;
            }
            if (key != key.Trim())
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Key '{key}' has leading/trailing whitespace",
                    "validation_error");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Mark table + shared data dirty and save assets. Call once after a batch, not per entry.
        /// </summary>
        public static void MarkDirtyAndSave(StringTable table)
        {
            if (table == null) return;
            EditorUtility.SetDirty(table);
            if (table.SharedData != null)
            {
                EditorUtility.SetDirty(table.SharedData);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Get the count of unique keys in a collection (lives in SharedTableData).
        /// </summary>
        public static int GetEntryCount(StringTableCollection collection)
        {
            if (collection?.SharedData == null) return 0;
            return collection.SharedData.Entries.Count;
        }

        /// <summary>
        /// Get all locale codes configured for a collection.
        /// </summary>
        public static List<string> GetLocaleCodes(StringTableCollection collection)
        {
            return collection.StringTables
                .Where(t => t != null)
                .Select(t => t.LocaleIdentifier.Code)
                .ToList();
        }
    }
}
