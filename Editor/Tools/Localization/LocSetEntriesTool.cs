using Newtonsoft.Json.Linq;
using McpUnity.Unity;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Batch sets multiple StringTable entries in a single transaction.
    /// </summary>
    public class LocSetEntriesTool : McpToolBase
    {
        public LocSetEntriesTool()
        {
            Name = "loc_set_entries";
            Description = "Batch sets multiple Unity Localization StringTable entries in one operation. Saves once at the end.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""StringTable collection name"" },
                ""locale"": { ""type"": ""string"", ""description"": ""Locale code (default zh-TW)"" },
                ""entries"": {
                    ""type"": ""array"",
                    ""description"": ""Array of {key, value} entries"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""key"": { ""type"": ""string"" },
                            ""value"": { ""type"": ""string"" }
                        },
                        ""required"": [""key"", ""value""]
                    }
                }
            },
            ""required"": [""table_name"", ""entries""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();
            string locale = parameters["locale"]?.ToString();
            var entriesArray = parameters["entries"] as JArray;

            if (entriesArray == null || entriesArray.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'entries' must be a non-empty array",
                    "validation_error");
            }

            var collection = LocTableHelper.ResolveCollection(tableName, out var error);
            if (collection == null) return error;

            var table = LocTableHelper.ResolveTable(collection, locale, out error);
            if (table == null) return error;

            int created = 0;
            int updated = 0;

            for (int i = 0; i < entriesArray.Count; i++)
            {
                var entry = entriesArray[i] as JObject;
                if (entry == null)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"entries[{i}] is not an object",
                        "validation_error");
                }

                string key = entry["key"]?.ToString();
                string value = entry["value"]?.ToString() ?? string.Empty;

                if (!LocTableHelper.ValidateKey(key, out error))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"entries[{i}]: invalid key",
                        "validation_error");
                }

                string action = LocSetEntryTool.SetEntry(collection, table, key, value);
                if (action == "created") created++;
                else updated++;
            }

            LocTableHelper.MarkDirtyAndSave(table);

            int total = created + updated;
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Set {total} entries in '{tableName}' ({table.LocaleIdentifier.Code}): {created} created, {updated} updated",
                ["created"] = created,
                ["updated"] = updated,
                ["total"] = total
            };
        }
    }
}
