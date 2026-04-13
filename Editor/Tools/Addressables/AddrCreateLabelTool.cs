using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Register a new label. Idempotent — re-adding an existing label is not an error.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrCreateLabelTool : McpToolBase
    {
        public AddrCreateLabelTool()
        {
            Name = "addr_create_label";
            Description = "Register a new Unity Addressables label (idempotent)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""label"": { ""type"": ""string"", ""description"": ""Label name (no spaces or brackets)"" }
            },
            ""required"": [""label""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string label = parameters["label"]?.ToString();
            if (!AddrHelper.ValidateLabel(label, out var validationError))
            {
                return validationError;
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var existing = settings.GetLabels();
            if (existing.Contains(label))
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Label '{label}' already exists",
                    ["created"] = false,
                    ["label"] = label
                };
            }

            settings.AddLabel(label, true);
            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.LabelAdded);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Created label '{label}'",
                ["created"] = true,
                ["label"] = label
            };
        }
    }
}
