using Newtonsoft.Json.Linq;
using UnityEngine.Localization.Tables;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Adds or updates a single StringTable entry. Creates the key in SharedData if missing.
    /// </summary>
    [McpUnityFirstParty]
    public class LocSetEntryTool : McpToolBase
    {
        public LocSetEntryTool()
        {
            Name = "loc_set_entry";
            Description = "Sets a Unity Localization StringTable entry value. Creates the key if it does not exist. Supports TMP RichText. For batches of >5 entries, prefer loc_set_entries (single SaveAssets at the end).";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""StringTable collection name"" },
                ""locale"": { ""type"": ""string"", ""description"": ""Locale code (default zh-TW)"" },
                ""key"": { ""type"": ""string"", ""description"": ""Entry key"" },
                ""value"": { ""type"": ""string"", ""description"": ""Entry value (supports TMP RichText)"" }
            },
            ""required"": [""table_name"", ""key"", ""value""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();
            string locale = parameters["locale"]?.ToString();
            string key = parameters["key"]?.ToString();
            string value = parameters["value"]?.ToString() ?? string.Empty;

            if (!LocTableHelper.ValidateKey(key, out var error)) return error;

            var collection = LocTableHelper.ResolveCollection(tableName, out error);
            if (collection == null) return error;

            var table = LocTableHelper.ResolveTable(collection, locale, out error);
            if (table == null) return error;

            string action = SetEntry(collection, table, key, value);
            LocTableHelper.MarkDirtyAndSave(table);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"{action} '{key}' in '{tableName}' ({table.LocaleIdentifier.Code})",
                ["action"] = action,
                ["key"] = key,
                ["value"] = value
            };
        }

        /// <summary>
        /// Set an entry on the table; returns "created" or "updated". Does NOT save.
        /// </summary>
        internal static string SetEntry(
            UnityEditor.Localization.StringTableCollection collection,
            StringTable table,
            string key,
            string value)
        {
            var sharedEntry = collection.SharedData.GetEntry(key);
            string action;
            if (sharedEntry == null)
            {
                sharedEntry = collection.SharedData.AddKey(key);
                action = "created";
            }
            else
            {
                action = table.GetEntry(sharedEntry.Id) == null ? "created" : "updated";
            }

            table.AddEntry(sharedEntry.Id, value);
            return action;
        }
    }
}
