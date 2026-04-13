using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

[assembly: InternalsVisibleTo("McpUnity.Localization.Tests")]

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

        /// <summary>
        /// Find a registered project Locale by its code (e.g. "zh-TW"). Returns null if not registered.
        /// Use this instead of struct equality on LocaleIdentifier to avoid edge cases like zh-Hant-TW.
        /// </summary>
        public static Locale FindLocale(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return LocalizationEditorSettings.GetLocales()
                .FirstOrDefault(l => l.Identifier.Code == code);
        }

        /// <summary>
        /// Reject paths that escape the Unity Assets folder. Empty/null is treated as
        /// "use default" by the caller and is allowed.
        /// </summary>
        public static bool ValidateAssetPath(string dir, out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(dir)) return true;

            if (dir != "Assets" && !dir.StartsWith("Assets/"))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Directory '{dir}' must be inside the Assets folder (start with 'Assets/')",
                    "validation_error");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Ensure an asset folder exists, creating intermediate folders via AssetDatabase
        /// (which writes proper .meta files atomically — unlike Directory.CreateDirectory).
        /// Caller must have already passed the path through ValidateAssetPath.
        /// </summary>
        public static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split('/');
            string currentPath = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string parentPath = currentPath;
                currentPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(parentPath, parts[i]);
                }
            }
        }
    }
}
