using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Remove a label. Refuses if any entry still references it unless <c>force=true</c>.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrRemoveLabelTool : McpToolBase
    {
        public AddrRemoveLabelTool()
        {
            Name = "addr_remove_label";
            Description = "Remove a Unity Addressables label. Refuses when still in use unless force=true (which also strips it from entries)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""label"": { ""type"": ""string"", ""description"": ""Label name"" },
                ""force"": { ""type"": ""boolean"", ""description"": ""Remove even if entries still reference it (default false)"" }
            },
            ""required"": [""label""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string label = parameters["label"]?.ToString();
            if (string.IsNullOrWhiteSpace(label))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'label' must be a non-empty string",
                    "validation_error");
            }

            bool force = parameters["force"]?.ToObject<bool>() ?? false;

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            if (!settings.GetLabels().Contains(label))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Label '{label}' not found",
                    "not_found");
            }

            int affected = 0;
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry.labels.Contains(label))
                    {
                        affected++;
                        if (force)
                        {
                            entry.SetLabel(label, false, false, true);
                        }
                    }
                }
            }

            if (affected > 0 && !force)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Label '{label}' is still used by {affected} entry(ies). Pass force=true to remove anyway",
                    "in_use");
            }

            settings.RemoveLabel(label, true);
            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.LabelRemoved);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Removed label '{label}'" + (affected > 0 ? $" (stripped from {affected} entries)" : string.Empty),
                ["deleted"] = true,
                ["label"] = label,
                ["affectedEntries"] = affected
            };
        }
    }
}
