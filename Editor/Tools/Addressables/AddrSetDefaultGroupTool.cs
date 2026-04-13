using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Switch the Addressables default group. Asset drag-drops into the Groups window
    /// land in whichever group is default.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrSetDefaultGroupTool : McpToolBase
    {
        public AddrSetDefaultGroupTool()
        {
            Name = "addr_set_default_group";
            Description = "Set the Unity Addressables default group (the one new entries fall into when no group is specified)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""Group name"" }
            },
            ""required"": [""name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string name = parameters["name"]?.ToString();

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var group = AddrHelper.ResolveGroup(settings, name, out var resolveError);
            if (group == null) return resolveError;

            string previousDefault = settings.DefaultGroup?.Name;
            if (previousDefault == group.Name)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Group '{name}' is already the default",
                    ["defaultGroup"] = name,
                    ["previousDefault"] = previousDefault
                };
            }

            settings.DefaultGroup = group;
            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.BatchModification);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Default group changed from '{previousDefault}' to '{name}'",
                ["defaultGroup"] = name,
                ["previousDefault"] = previousDefault
            };
        }
    }
}
