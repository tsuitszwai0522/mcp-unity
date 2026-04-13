using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Query the current AddressableAssetSettings state. Returns <c>initialized=false</c>
    /// (not an error) when Addressables has not been set up, so agents can branch.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrGetSettingsTool : McpToolBase
    {
        public AddrGetSettingsTool()
        {
            Name = "addr_get_settings";
            Description = "Query Unity Addressables settings state — initialized flag, default group, active profile, labels, group count";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Addressables is not initialized. Call 'addr_init_settings' to create default settings.",
                    ["initialized"] = false
                };
            }

            var activeProfileId = settings.activeProfileId;
            var profileName = settings.profileSettings.GetProfileName(activeProfileId);

            var profileVariables = new JObject();
            foreach (var variableName in settings.profileSettings.GetVariableNames())
            {
                var value = settings.profileSettings.GetValueByName(activeProfileId, variableName);
                profileVariables[variableName] = value;
            }

            var labels = new JArray();
            foreach (var label in settings.GetLabels())
            {
                labels.Add(label);
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Addressables initialized. Default group: '{settings.DefaultGroup?.Name}', active profile: '{profileName}', groups: {settings.groups.Count}, entries: {AddrHelper.GetTotalEntryCount(settings)}",
                ["initialized"] = true,
                ["defaultGroup"] = settings.DefaultGroup?.Name,
                ["activeProfile"] = profileName,
                ["profileVariables"] = profileVariables,
                ["groupCount"] = settings.groups.Count,
                ["entryCount"] = AddrHelper.GetTotalEntryCount(settings),
                ["labels"] = labels
            };
        }
    }
}
