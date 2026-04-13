using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Deletes an entire StringTableCollection (the collection asset, its SharedTableData,
    /// and every per-locale StringTable). Symmetric counterpart to <see cref="LocCreateTableTool"/>.
    /// </summary>
    [McpUnityFirstParty]
    public class LocDeleteTableTool : McpToolBase
    {
        public LocDeleteTableTool()
        {
            Name = "loc_delete_table";
            Description = "Deletes a Unity Localization StringTable collection (removes the collection asset, its SharedTableData, and all per-locale StringTables). Symmetric to loc_create_table.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""table_name"": { ""type"": ""string"", ""description"": ""StringTable collection name to delete"" }
            },
            ""required"": [""table_name""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string tableName = parameters["table_name"]?.ToString();

            var collection = LocTableHelper.ResolveCollection(tableName, out var error);
            if (collection == null) return error;

            // Capture details before deletion for the response.
            int entryCount = LocTableHelper.GetEntryCount(collection);
            var localeCodes = LocTableHelper.GetLocaleCodes(collection);
            string collectionPath = AssetDatabase.GetAssetPath(collection);

            LocTableHelper.DeleteStringTableCollection(collection);
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Deleted StringTable '{tableName}' ({entryCount} entries, locales [{string.Join(", ", localeCodes)}])",
                ["deleted"] = true,
                ["name"] = tableName,
                ["path"] = collectionPath,
                ["entryCount"] = entryCount,
                ["locales"] = new JArray(localeCodes)
            };
        }
    }
}
