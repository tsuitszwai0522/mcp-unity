using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

[assembly: InternalsVisibleTo("McpUnity.Addressables.Tests")]

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Shared helpers for Addressables MCP tools — settings access, entry resolution, save.
    /// </summary>
    internal static class AddrHelper
    {
        /// <summary>
        /// Get the current AddressableAssetSettings. Returns null and fills <paramref name="error"/>
        /// with a <c>not_initialized</c> response if Addressables has not been set up yet.
        /// </summary>
        public static AddressableAssetSettings TryGetSettings(out JObject error)
        {
            error = null;
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings != null) return settings;

            error = McpUnitySocketHandler.CreateErrorResponse(
                "Addressables is not initialized. Call 'addr_init_settings' first, or use the Addressables Groups window to create default settings.",
                "not_initialized");
            return null;
        }

        /// <summary>
        /// Find a group by name. Returns null and fills error on miss.
        /// </summary>
        public static AddressableAssetGroup ResolveGroup(
            AddressableAssetSettings settings,
            string name,
            out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'name' must be a non-empty string",
                    "validation_error");
                return null;
            }

            var group = settings.FindGroup(name);
            if (group != null) return group;

            var available = string.Join(", ", settings.groups.Where(g => g != null).Select(g => g.Name));
            error = McpUnitySocketHandler.CreateErrorResponse(
                $"Addressables group '{name}' not found. Available: [{available}]",
                "not_found");
            return null;
        }

        /// <summary>
        /// Resolve an entry from either a guid or an asset path.
        /// Returns null if neither identifier resolves (caller decides whether to treat as error).
        /// </summary>
        public static AddressableAssetEntry ResolveEntry(
            AddressableAssetSettings settings,
            string guid,
            string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(guid))
            {
                var entry = settings.FindAssetEntry(guid);
                if (entry != null) return entry;
            }

            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var pathGuid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!string.IsNullOrEmpty(pathGuid))
                {
                    return settings.FindAssetEntry(pathGuid);
                }
            }

            return null;
        }

        /// <summary>
        /// Mark settings dirty and save assets. Call once at the end of a batch.
        /// </summary>
        public static void SaveSettings(
            AddressableAssetSettings settings,
            AddressableAssetSettings.ModificationEvent modificationEvent = AddressableAssetSettings.ModificationEvent.BatchModification)
        {
            if (settings == null) return;
            settings.SetDirty(modificationEvent, null, true, true);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Serialize an entry into the canonical JObject shape used across all addr_ tools.
        /// </summary>
        public static JObject EntryToJson(AddressableAssetEntry entry)
        {
            if (entry == null) return null;
            return new JObject
            {
                ["guid"] = entry.guid,
                ["assetPath"] = entry.AssetPath,
                ["address"] = entry.address,
                ["labels"] = new JArray(entry.labels),
                ["group"] = entry.parentGroup?.Name
            };
        }

        /// <summary>
        /// Validate a label name — non-empty, no whitespace, no square brackets (used by
        /// Addressables internally for label parsing).
        /// </summary>
        public static bool ValidateLabel(string label, out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(label))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    "Label must be a non-empty string",
                    "validation_error");
                return false;
            }
            if (label.Contains(' ') || label.Contains('[') || label.Contains(']'))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Label '{label}' contains invalid characters (no spaces or square brackets allowed)",
                    "validation_error");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Count entries across all groups (used by get_settings summary).
        /// </summary>
        public static int GetTotalEntryCount(AddressableAssetSettings settings)
        {
            int count = 0;
            foreach (var group in settings.groups)
            {
                if (group != null) count += group.entries.Count;
            }
            return count;
        }
    }
}
