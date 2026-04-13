using Newtonsoft.Json.Linq;
using UnityEditor.Localization;

namespace McpUnity.Tools.Localization
{
    /// <summary>
    /// Lists all StringTable collections in the project with their locales and entry counts.
    /// </summary>
    [McpUnityFirstParty]
    public class LocListTablesTool : McpToolBase
    {
        public LocListTablesTool()
        {
            Name = "loc_list_tables";
            Description = "Lists all Unity Localization StringTable collections with their locales and entry counts";
        }

        public override JObject ParameterSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(),
            ["required"] = new JArray()
        };

        public override JObject Execute(JObject parameters)
        {
            var collections = LocalizationEditorSettings.GetStringTableCollections();
            var tables = new JArray();

            foreach (var collection in collections)
            {
                var locales = new JArray();
                foreach (var code in LocTableHelper.GetLocaleCodes(collection))
                {
                    locales.Add(code);
                }

                tables.Add(new JObject
                {
                    ["name"] = collection.TableCollectionName,
                    ["locales"] = locales,
                    ["entryCount"] = LocTableHelper.GetEntryCount(collection)
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {tables.Count} StringTable collection(s)",
                ["tables"] = tables
            };
        }
    }
}
