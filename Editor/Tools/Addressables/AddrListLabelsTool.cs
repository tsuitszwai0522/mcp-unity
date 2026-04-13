using Newtonsoft.Json.Linq;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// List all labels registered in Addressables settings.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrListLabelsTool : McpToolBase
    {
        public AddrListLabelsTool()
        {
            Name = "addr_list_labels";
            Description = "List all Unity Addressables labels";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var labels = new JArray();
            foreach (var label in settings.GetLabels())
            {
                labels.Add(label);
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {labels.Count} label(s)",
                ["labels"] = labels
            };
        }
    }
}
