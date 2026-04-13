using Newtonsoft.Json.Linq;
using UnityEditor;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Deletes an entry key from a StringTable collection (removes from SharedData and all locale tables).
    /// </summary>
    public class LocDeleteEntryTool : McpToolBase
    {
        public LocDeleteEntryTool()
        {
            Name = "loc_delete_entry";
            Description = "Deletes a Unity Localization entry key from a StringTable collection (affects all locales)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""StringTable collection name"" },
                ""key"": { ""type"": ""string"", ""description"": ""Entry key to delete"" }
            },
            ""required"": [""table_name"", ""key""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();
            string key = parameters["key"]?.ToString();

            if (!LocTableHelper.ValidateKey(key, out var error)) return error;

            var collection = LocTableHelper.ResolveCollection(tableName, out error);
            if (collection == null) return error;

            var sharedEntry = collection.SharedData.GetEntry(key);
            if (sharedEntry == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Key '{key}' did not exist in '{tableName}'",
                    ["deleted"] = false,
                    ["key"] = key
                };
            }

            // Use the collection-level RemoveEntry API — this atomically removes the
            // SharedData key AND every per-locale StringTable entry referencing it,
            // and raises LocalizationEditorSettings.EditorEvents.RaiseTableEntryRemoved.
            // (Available since Unity Localization 1.x. Doing this manually risks orphan
            // StringTableEntry rows surviving in per-locale .asset files.)
            collection.RemoveEntry(key);

            EditorUtility.SetDirty(collection.SharedData);
            foreach (var t in collection.StringTables)
            {
                if (t != null) EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Deleted '{key}' from '{tableName}'",
                ["deleted"] = true,
                ["key"] = key
            };
        }
    }
}
