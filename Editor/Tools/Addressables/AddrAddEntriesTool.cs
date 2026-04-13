using System.Collections.Generic;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Batch add assets to a group. Accepts per-asset optional address + labels.
    /// Labels that don't exist yet are auto-created with a warning.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrAddEntriesTool : McpToolBase
    {
        public AddrAddEntriesTool()
        {
            Name = "addr_add_entries";
            Description = "Batch-add Unity Addressables entries to a group with optional address/labels per asset";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""group"": { ""type"": ""string"", ""description"": ""Target group name (must exist)"" },
                ""assets"": {
                    ""type"": ""array"",
                    ""description"": ""Assets to add. Each: {asset_path, address?, labels?}"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""asset_path"": { ""type"": ""string"" },
                            ""address"": { ""type"": ""string"" },
                            ""labels"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
                        },
                        ""required"": [""asset_path""]
                    }
                },
                ""fail_on_missing_asset"": {
                    ""type"": ""boolean"",
                    ""description"": ""When true (default) the whole call fails with not_found if any asset_path does not resolve. Set false for best-effort batches that skip missing assets with a warning.""
                }
            },
            ""required"": [""group"", ""assets""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string groupName = parameters["group"]?.ToString();
            var assetsArray = parameters["assets"] as JArray;
            if (assetsArray == null || assetsArray.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'assets' must be a non-empty array",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var group = AddrHelper.ResolveGroup(settings, groupName, out var groupError);
            if (group == null) return groupError;

            // Default strict: any unresolved asset_path aborts the batch. Agents
            // that want best-effort behaviour opt in with fail_on_missing_asset=false.
            bool failOnMissingAsset = parameters["fail_on_missing_asset"]?.ToObject<bool>() ?? true;

            var existingLabels = new HashSet<string>(settings.GetLabels());
            var warnings = new JArray();
            var addedEntries = new JArray();
            var missingAssets = new JArray();
            int added = 0, skipped = 0;

            foreach (var item in assetsArray)
            {
                string assetPath = item["asset_path"]?.ToString();
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    if (failOnMissingAsset)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            "Each entry in 'assets' must have a non-empty 'asset_path'",
                            "validation_error");
                    }
                    warnings.Add("Skipped entry with empty asset_path");
                    skipped++;
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    if (failOnMissingAsset)
                    {
                        return McpUnitySocketHandler.CreateErrorResponse(
                            $"Asset '{assetPath}' not found. Pass fail_on_missing_asset=false for best-effort batches.",
                            "not_found");
                    }
                    warnings.Add($"Asset '{assetPath}' not found, skipped");
                    missingAssets.Add(assetPath);
                    skipped++;
                    continue;
                }

                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null)
                {
                    warnings.Add($"Failed to create entry for '{assetPath}'");
                    skipped++;
                    continue;
                }

                string address = item["address"]?.ToString();
                if (!string.IsNullOrWhiteSpace(address))
                {
                    entry.address = address;
                }

                var labelsArray = item["labels"] as JArray;
                if (labelsArray != null)
                {
                    foreach (var labelToken in labelsArray)
                    {
                        string label = labelToken?.ToString();
                        if (string.IsNullOrWhiteSpace(label)) continue;
                        if (!AddrHelper.ValidateLabel(label, out _))
                        {
                            warnings.Add($"Skipped invalid label '{label}' on '{assetPath}'");
                            continue;
                        }
                        if (!existingLabels.Contains(label))
                        {
                            settings.AddLabel(label, false);
                            existingLabels.Add(label);
                            warnings.Add($"Label '{label}' was created automatically");
                        }
                        entry.SetLabel(label, true, false, false);
                    }
                }

                added++;
                addedEntries.Add(AddrHelper.EntryToJson(entry));
            }

            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.EntryMoved);

            var result = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Added {added} entries to group '{groupName}'" + (skipped > 0 ? $" ({skipped} skipped)" : string.Empty),
                ["added"] = added,
                ["skipped"] = skipped,
                ["entries"] = addedEntries
            };
            if (warnings.Count > 0) result["warnings"] = warnings;
            if (missingAssets.Count > 0) result["missingAssets"] = missingAssets;
            return result;
        }
    }
}
