using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Remove an Addressables group. Refuses to delete the default group;
    /// refuses to delete non-empty groups unless <c>force=true</c>.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrRemoveGroupTool : McpToolBase
    {
        public AddrRemoveGroupTool()
        {
            Name = "addr_remove_group";
            Description = "Remove a Unity Addressables group. Refuses to delete default group or non-empty groups unless force=true";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""Group name"" },
                ""force"": { ""type"": ""boolean"", ""description"": ""Force delete even when group has entries (default false)"" }
            },
            ""required"": [""name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string name = parameters["name"]?.ToString();
            bool force = parameters["force"]?.ToObject<bool>() ?? false;

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var group = AddrHelper.ResolveGroup(settings, name, out var resolveError);
            if (group == null) return resolveError;

            if (group == settings.DefaultGroup)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot delete default group '{name}'. Switch default first with 'addr_set_default_group'",
                    "validation_error");
            }

            int entryCount = group.entries.Count;
            if (entryCount > 0 && !force)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Group '{name}' has {entryCount} entries. Pass force=true to delete anyway",
                    "in_use");
            }

            settings.RemoveGroup(group);
            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.GroupRemoved);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Removed group '{name}' ({entryCount} entries)",
                ["deleted"] = true,
                ["name"] = name,
                ["removedEntryCount"] = entryCount
            };
        }
    }
}
