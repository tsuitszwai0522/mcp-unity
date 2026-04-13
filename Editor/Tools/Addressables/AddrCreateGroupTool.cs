using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Create a new Addressables group with BundledAssetGroupSchema + ContentUpdateGroupSchema.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrCreateGroupTool : McpToolBase
    {
        public AddrCreateGroupTool()
        {
            Name = "addr_create_group";
            Description = "Create a new Unity Addressables group with default Bundled + ContentUpdate schemas";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""Group name (must be unique)"" },
                ""set_as_default"": { ""type"": ""boolean"", ""description"": ""Set as default group (default false)"" },
                ""packed_mode"": { ""type"": ""string"", ""description"": ""PackTogether | PackSeparately | PackTogetherByLabel (default PackTogether)"" },
                ""include_in_build"": { ""type"": ""boolean"", ""description"": ""Whether to include this group in the build (default true)"" }
            },
            ""required"": [""name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string name = parameters["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'name' must be a non-empty string",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            if (settings.FindGroup(name) != null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Addressables group '{name}' already exists",
                    "duplicate");
            }

            bool setAsDefault = parameters["set_as_default"]?.ToObject<bool>() ?? false;
            bool includeInBuild = parameters["include_in_build"]?.ToObject<bool>() ?? true;
            string packedModeStr = parameters["packed_mode"]?.ToString();

            BundledAssetGroupSchema.BundlePackingMode packedMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            if (!string.IsNullOrWhiteSpace(packedModeStr)
                && !System.Enum.TryParse(packedModeStr, true, out packedMode))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid packed_mode '{packedModeStr}'. Expected: PackTogether | PackSeparately | PackTogetherByLabel",
                    "validation_error");
            }

            var group = settings.CreateGroup(
                name,
                setAsDefault,
                false,
                true,
                null,
                typeof(BundledAssetGroupSchema),
                typeof(ContentUpdateGroupSchema));

            if (group == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create group '{name}'",
                    "create_failed");
            }

            var bundled = group.GetSchema<BundledAssetGroupSchema>();
            if (bundled != null)
            {
                bundled.BundleMode = packedMode;
                bundled.IncludeInBuild = includeInBuild;
            }

            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.GroupAdded);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Created Addressables group '{name}'" + (setAsDefault ? " (set as default)" : string.Empty),
                ["created"] = true,
                ["name"] = group.Name,
                ["isDefault"] = group.Name == settings.DefaultGroup?.Name
            };
        }
    }
}
