using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// List all Addressables groups with their entry counts and attached schemas.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrListGroupsTool : McpToolBase
    {
        public AddrListGroupsTool()
        {
            Name = "addr_list_groups";
            Description = "List all Unity Addressables groups with entry counts and schemas";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var defaultGroupName = settings.DefaultGroup?.Name;
            var groups = new JArray();
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                var schemas = new JArray();
                foreach (var schema in group.Schemas)
                {
                    if (schema != null) schemas.Add(schema.GetType().Name);
                }

                groups.Add(new JObject
                {
                    ["name"] = group.Name,
                    ["isDefault"] = group.Name == defaultGroupName,
                    ["entryCount"] = group.entries.Count,
                    ["readOnly"] = group.ReadOnly,
                    ["schemas"] = schemas
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {groups.Count} Addressables group(s)",
                ["groups"] = groups
            };
        }
    }
}
