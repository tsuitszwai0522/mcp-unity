using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Unregisters a Locale from the project's Localization settings and deletes the
    /// underlying asset. Symmetric counterpart to <see cref="LocAddLocaleTool"/>.
    /// </summary>
    [McpUnityFirstParty]
    public class LocRemoveLocaleTool : McpToolBase
    {
        public LocRemoveLocaleTool()
        {
            Name = "loc_remove_locale";
            Description = "Unregisters a Locale (by code, e.g. 'zh-TW') from Unity Localization and deletes its asset. Symmetric to loc_add_locale.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""code"": { ""type"": ""string"", ""description"": ""Locale identifier code to remove (e.g. 'zh-TW', 'en')"" },
                ""delete_asset"": { ""type"": ""boolean"", ""description"": ""Also delete the .asset file from disk (default true)"" }
            },
            ""required"": [""code""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string code = parameters["code"]?.ToString();
            bool deleteAsset = parameters["delete_asset"]?.Value<bool>() ?? true;

            if (string.IsNullOrWhiteSpace(code))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'code' must be provided",
                    "validation_error");
            }

            var locale = LocTableHelper.FindLocale(code);
            if (locale == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Locale '{code}' was not registered",
                    ["action"] = "not_registered",
                    ["code"] = code
                };
            }

            string assetPath = AssetDatabase.GetAssetPath(locale);

            // Unregister from LocalizationEditorSettings first (raises RaiseLocaleRemoved
            // and removes from Addressables groups). Then optionally delete the asset.
            LocalizationEditorSettings.RemoveLocale(locale, createUndo: false);

            if (deleteAsset && !string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Removed locale '{code}'" + (deleteAsset ? $" and deleted asset '{assetPath}'" : ""),
                ["action"] = "removed",
                ["code"] = code,
                ["path"] = assetPath
            };
        }
    }
}
