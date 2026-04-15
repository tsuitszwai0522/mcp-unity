using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Read the current BundledAssetGroupSchema values for a group — read-only
    /// companion to addr_set_group_schema. Handy for verification and dry-run diffs.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrGetGroupSchemaTool : McpToolBase
    {
        public AddrGetGroupSchemaTool()
        {
            Name = "addr_get_group_schema";
            Description = "Read the current BundledAssetGroupSchema values for an Addressables group";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""group"": { ""type"": ""string"", ""description"": ""Group name (must exist)"" }
            },
            ""required"": [""group""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string groupName = parameters["group"]?.ToString();
            var settings = AddrHelper.TryGetSettings(out var settingsError);
            if (settings == null) return settingsError;

            var group = AddrHelper.ResolveGroup(settings, groupName, out var groupError);
            if (group == null) return groupError;

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Group '{groupName}' does not have a BundledAssetGroupSchema attached",
                    "schema_not_found");
            }

            var values = new JObject
            {
                ["compression"] = schema.Compression.ToString(),
                ["include_in_build"] = schema.IncludeInBuild,
                ["packed_mode"] = schema.BundleMode.ToString(),
                ["bundle_naming"] = schema.BundleNaming.ToString(),
                ["use_asset_bundle_cache"] = schema.UseAssetBundleCache,
                ["use_unitywebrequest_for_local_bundles"] = schema.UseUnityWebRequestForLocalBundles,
                ["retry_count"] = schema.RetryCount,
                ["timeout"] = schema.Timeout,
                ["build_path"] = schema.BuildPath.GetName(settings),
                ["load_path"] = schema.LoadPath.GetName(settings),
                ["build_path_value"] = schema.BuildPath.GetValue(settings),
                ["load_path_value"] = schema.LoadPath.GetValue(settings)
            };

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"BundledAssetGroupSchema for '{group.Name}'",
                ["group"] = group.Name,
                ["values"] = values
            };
        }
    }
}
