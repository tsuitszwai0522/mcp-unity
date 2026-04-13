using System.Collections.Generic;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Creates a new StringTable Collection with the specified locales.
    /// </summary>
    [McpUnityFirstParty]
    public class LocCreateTableTool : McpToolBase
    {
        private const string DefaultDirectory = "Assets/Localization/Tables";

        public LocCreateTableTool()
        {
            Name = "loc_create_table";
            Description = "Creates a new Unity Localization StringTable collection with the specified locales";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""New StringTable collection name"" },
                ""locales"": {
                    ""type"": ""array"",
                    ""description"": ""Locale codes to include (default [\""zh-TW\""])"",
                    ""items"": { ""type"": ""string"" }
                },
                ""directory"": { ""type"": ""string"", ""description"": ""Asset directory to save into (default Assets/Localization/Tables)"" }
            },
            ""required"": [""table_name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();
            string directory = parameters["directory"]?.ToString();
            var localesArray = parameters["locales"] as JArray;

            if (string.IsNullOrWhiteSpace(tableName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'table_name' must be provided",
                    "validation_error");
            }

            // Reject duplicate
            var existing = LocalizationEditorSettings.GetStringTableCollections()
                .FirstOrDefault(c => c.TableCollectionName == tableName);
            if (existing != null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"StringTable '{tableName}' already exists",
                    "duplicate_table");
            }

            // Resolve target locales
            var requestedCodes = localesArray != null && localesArray.Count > 0
                ? localesArray.Select(t => t.ToString()).ToList()
                : new List<string> { LocTableHelper.DefaultLocale };

            var resolvedLocales = new List<Locale>();
            var warnings = new JArray();

            foreach (var code in requestedCodes)
            {
                var locale = LocTableHelper.FindLocale(code);
                if (locale != null)
                {
                    resolvedLocales.Add(locale);
                }
                else
                {
                    warnings.Add($"Locale '{code}' is not configured in Localization Settings; skipped");
                }
            }

            if (resolvedLocales.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"No valid locales resolved. Requested: [{string.Join(", ", requestedCodes)}]. Configure them in Window > Asset Management > Localization Tables first.",
                    "no_valid_locales");
            }

            // Ensure directory
            string dir = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.TrimEnd('/');
            if (!LocTableHelper.ValidateAssetPath(dir, out var pathError)) return pathError;
            LocTableHelper.EnsureFolderExists(dir);

            var collection = LocalizationEditorSettings.CreateStringTableCollection(tableName, dir, resolvedLocales);
            if (collection == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create StringTable '{tableName}'",
                    "create_failed");
            }

            string assetPath = AssetDatabase.GetAssetPath(collection.SharedData);

            var result = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Created StringTable '{tableName}' with locales [{string.Join(", ", resolvedLocales.Select(l => l.Identifier.Code))}]",
                ["created"] = true,
                ["name"] = tableName,
                ["path"] = assetPath
            };

            if (warnings.Count > 0)
            {
                result["warnings"] = warnings;
            }

            return result;
        }
    }
}
