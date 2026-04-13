using System.Collections.Generic;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Partial update on a single entry — change address and/or add/remove labels.
    /// Auto-creates labels that don't exist yet (with a warning).
    /// </summary>
    [McpUnityFirstParty]
    public class AddrSetEntryTool : McpToolBase
    {
        public AddrSetEntryTool()
        {
            Name = "addr_set_entry";
            Description = "Update a single Unity Addressables entry — change address and/or add/remove labels (partial update)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""guid"": { ""type"": ""string"", ""description"": ""Entry guid (or asset_path)"" },
                ""asset_path"": { ""type"": ""string"", ""description"": ""Asset path (or guid)"" },
                ""new_address"": { ""type"": ""string"", ""description"": ""New address (optional)"" },
                ""add_labels"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Labels to add"" },
                ""remove_labels"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""Labels to remove"" }
            }
        }");

        public override JObject Execute(JObject parameters)
        {
            string guid = parameters["guid"]?.ToString();
            string assetPath = parameters["asset_path"]?.ToString();

            if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'guid' or 'asset_path' must be provided",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var entry = AddrHelper.ResolveEntry(settings, guid, assetPath);
            if (entry == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Entry not found (guid='{guid}', asset_path='{assetPath}')",
                    "not_found");
            }

            var warnings = new JArray();

            string newAddress = parameters["new_address"]?.ToString();
            if (newAddress != null && newAddress != entry.address)
            {
                entry.address = newAddress;
            }

            var existingLabels = new HashSet<string>(settings.GetLabels());

            var addArray = parameters["add_labels"] as JArray;
            if (addArray != null)
            {
                foreach (var token in addArray)
                {
                    string label = token?.ToString();
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    if (!AddrHelper.ValidateLabel(label, out _))
                    {
                        warnings.Add($"Skipped invalid label '{label}'");
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

            var removeArray = parameters["remove_labels"] as JArray;
            if (removeArray != null)
            {
                foreach (var token in removeArray)
                {
                    string label = token?.ToString();
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    entry.SetLabel(label, false, false, false);
                }
            }

            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.EntryModified);

            var result = AddrHelper.EntryToJson(entry);
            result["success"] = true;
            result["type"] = "text";
            result["message"] = $"Updated entry '{entry.AssetPath}'";
            if (warnings.Count > 0) result["warnings"] = warnings;
            return result;
        }
    }
}
