using System.Globalization;
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
    [McpUnityFirstParty]
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

            // Already registered? Match by Identifier.Code (consistent with FindLocale).
            var existing = LocTableHelper.FindLocale(code);
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

            // Soft culture pre-check: warn if .NET doesn't recognise the code, but still
            // create the Locale. Unity Localization accepts identifiers that .NET does not
            // (e.g. "zh-Hant" on some runtimes), so a hard reject would block legal cases.
            // We try GetCultureInfo first and the IETF-tag fallback second; both failing
            // is reported as a warning — never an error.
            JArray warnings = null;
            if (!IsRecognisedCulture(code))
            {
                warnings = new JArray
                {
                    $"Locale code '{code}' is not recognised by .NET CultureInfo on this runtime. Unity will still create it, but verify the code is correct (e.g. 'zh-TW', 'en', 'ja')."
                };
            }

            var identifier = new LocaleIdentifier(code);

            // Create the Locale via the runtime factory (handles m_Identifier + CultureInfo correctly).
            var locale = Locale.CreateLocale(identifier);
            if (locale == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Locale.CreateLocale returned null for code '{code}' — invalid identifier",
                    "invalid_locale_code");
            }

            string dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.TrimEnd('/');
            if (!LocTableHelper.ValidateAssetPath(dir, out var pathError)) return pathError;
            LocTableHelper.EnsureFolderExists(dir);

            string assetPath = $"{dir}/Locale_{code}.asset";
            AssetDatabase.CreateAsset(locale, assetPath);
            LocalizationEditorSettings.AddLocale(locale, createUndo: false);
            AssetDatabase.SaveAssets();

            var result = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Registered locale '{code}' at '{assetPath}'",
                ["action"] = "created",
                ["code"] = code,
                ["path"] = assetPath
            };
            if (warnings != null) result["warnings"] = warnings;
            return result;
        }

        private static bool IsRecognisedCulture(string code)
        {
            try
            {
                CultureInfo.GetCultureInfo(code);
                return true;
            }
            catch (CultureNotFoundException) { }

            try
            {
                CultureInfo.GetCultureInfoByIetfLanguageTag(code);
                return true;
            }
            catch (System.ArgumentException) { }

            return false;
        }
    }
}
