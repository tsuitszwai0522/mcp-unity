using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Batch-remove entries identified by either guid or asset_path.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrRemoveEntriesTool : McpToolBase
    {
        public AddrRemoveEntriesTool()
        {
            Name = "addr_remove_entries";
            Description = "Batch-remove Unity Addressables entries (identified by guid or asset_path)";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""entries"": {
                    ""type"": ""array"",
                    ""description"": ""Entries to remove. Each: {guid?} or {asset_path?}"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""guid"": { ""type"": ""string"" },
                            ""asset_path"": { ""type"": ""string"" }
                        }
                    }
                }
            },
            ""required"": [""entries""]
        }");

        public override JObject Execute(JObject parameters)
        {
            var entriesArray = parameters["entries"] as JArray;
            if (entriesArray == null || entriesArray.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'entries' must be a non-empty array",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            int removed = 0, notFound = 0;
            foreach (var item in entriesArray)
            {
                string guid = item["guid"]?.ToString();
                string assetPath = item["asset_path"]?.ToString();
                var entry = AddrHelper.ResolveEntry(settings, guid, assetPath);
                if (entry == null)
                {
                    notFound++;
                    continue;
                }

                settings.RemoveAssetEntry(entry.guid, false);
                removed++;
            }

            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.EntryRemoved);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Removed {removed} entries" + (notFound > 0 ? $" ({notFound} not found)" : string.Empty),
                ["removed"] = removed,
                ["notFound"] = notFound
            };
        }
    }
}
