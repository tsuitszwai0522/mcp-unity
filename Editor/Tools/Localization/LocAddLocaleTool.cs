using System.IO;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Registers a Locale with the project's Localization settings. Creates the Locale asset
    /// if it does not already exist. Intended for fresh-project bootstrap — explicit, not automatic.
    /// </summary>
    public class LocAddLocaleTool : McpToolBase
    {
        private const string DefaultDirectory = "Assets/Localization/Locales";

        public LocAddLocaleTool()
        {
            Name = "loc_add_locale";
            Description = "Registers a Locale (by code, e.g. 'zh-TW') with Unity Localization. Creates the Locale asset if missing. Use this to bootstrap a fresh project before loc_create_table.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""code"": { ""type"": ""string"", ""description"": ""Locale identifier code (e.g. 'zh-TW', 'en', 'ja')"" },
                ""directory"": { ""type"": ""string"", ""description"": ""Asset directory for the Locale asset (default Assets/Localization/Locales)"" }
            },
            ""required"": [""code""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string code = parameters["code"]?.ToString();
            string directory = parameters["directory"]?.ToString();

            if (string.IsNullOrWhiteSpace(code))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'code' must be provided",
                    "validation_error");
            }

            var identifier = new LocaleIdentifier(code);

            // Already registered?
            var existing = LocalizationEditorSettings.GetLocales()
                .FirstOrDefault(l => l.Identifier == identifier);
            if (existing != null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Locale '{code}' already registered",
                    ["action"] = "already_exists",
                    ["code"] = code,
                    ["path"] = AssetDatabase.GetAssetPath(existing)
                };
            }

            // Create the Locale via the runtime factory (handles m_Identifier + CultureInfo correctly).
            var locale = Locale.CreateLocale(identifier);
            if (locale == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Locale.CreateLocale returned null for code '{code}' — invalid identifier",
                    "invalid_locale_code");
            }

            string dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            string assetPath = $"{dir}/Locale_{code}.asset";
            AssetDatabase.CreateAsset(locale, assetPath);
            LocalizationEditorSettings.AddLocale(locale, createUndo: false);
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Registered locale '{code}' at '{assetPath}'",
                ["action"] = "created",
                ["code"] = code,
                ["path"] = assetPath
            };
        }
    }
}
