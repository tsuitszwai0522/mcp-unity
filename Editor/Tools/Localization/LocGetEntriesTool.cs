using Newtonsoft.Json.Linq;
using UnityEngine.Localization.Tables;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Reads entries from a StringTable, optionally filtered by key prefix.
    /// </summary>
    [McpUnityFirstParty]
    public class LocGetEntriesTool : McpToolBase
    {
        public LocGetEntriesTool()
        {
            Name = "loc_get_entries";
            Description = "Reads key/value entries from a Unity Localization StringTable, with optional key-prefix filter";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""StringTable collection name"" },
                ""locale"": { ""type"": ""string"", ""description"": ""Locale code (default zh-TW)"" },
                ""filter"": { ""type"": ""string"", ""description"": ""Optional key-prefix filter"" }
            },
            ""required"": [""table_name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();
            string locale = parameters["locale"]?.ToString();
            string filter = parameters["filter"]?.ToString();

            var collection = LocTableHelper.ResolveCollection(tableName, out var error);
            if (collection == null) return error;

            var table = LocTableHelper.ResolveTable(collection, locale, out error);
            if (table == null) return error;

            var entries = new JArray();
            var sharedData = collection.SharedData;

            foreach (var sharedEntry in sharedData.Entries)
            {
                if (!string.IsNullOrEmpty(filter) && !sharedEntry.Key.StartsWith(filter))
                    continue;

                StringTableEntry entry = table.GetEntry(sharedEntry.Id);
                entries.Add(new JObject
                {
                    ["key"] = sharedEntry.Key,
                    ["value"] = entry?.Value ?? string.Empty
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Read {entries.Count} entries from '{tableName}' ({table.LocaleIdentifier.Code})",
                ["table"] = tableName,
                ["locale"] = table.LocaleIdentifier.Code,
                ["entries"] = entries
            };
        }
    }
}
