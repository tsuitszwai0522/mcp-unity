using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Look up the Addressables entry for a given asset path. Returns
    /// <c>found=false</c> when the asset exists but isn't addressable.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrFindAssetTool : McpToolBase
    {
        public AddrFindAssetTool()
        {
            Name = "addr_find_asset";
            Description = "Look up a Unity Addressables entry by asset path — returns group, address, labels";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""asset_path"": { ""type"": ""string"", ""description"": ""Asset path (e.g. Assets/Prefabs/Foo.prefab)"" }
            },
            ""required"": [""asset_path""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string assetPath = parameters["asset_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'asset_path' must be a non-empty string",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Asset '{assetPath}' not found",
                    "not_found");
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Asset '{assetPath}' is not addressable",
                    ["found"] = false,
                    ["assetPath"] = assetPath,
                    ["guid"] = guid
                };
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found entry for '{assetPath}' in group '{entry.parentGroup?.Name}'",
                ["found"] = true,
                ["entry"] = AddrHelper.EntryToJson(entry)
            };
        }
    }
}
