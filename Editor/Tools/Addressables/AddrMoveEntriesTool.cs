using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Move entries between groups.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrMoveEntriesTool : McpToolBase
    {
        public AddrMoveEntriesTool()
        {
            Name = "addr_move_entries";
            Description = "Batch-move Unity Addressables entries into a different group";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""target_group"": { ""type"": ""string"", ""description"": ""Destination group name (must exist)"" },
                ""entries"": {
                    ""type"": ""array"",
                    ""description"": ""Entries to move. Each: {guid?} or {asset_path?}"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""guid"": { ""type"": ""string"" },
                            ""asset_path"": { ""type"": ""string"" }
                        }
                    }
                }
            },
            ""required"": [""target_group"", ""entries""]
        }");

        public override JObject Execute(JObject parameters)
        {
            string targetGroupName = parameters["target_group"]?.ToString();
            var entriesArray = parameters["entries"] as JArray;
            if (entriesArray == null || entriesArray.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'entries' must be a non-empty array",
                    "validation_error");
            }

            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var targetGroup = AddrHelper.ResolveGroup(settings, targetGroupName, out var groupError);
            if (targetGroup == null) return groupError;

            int moved = 0, notFound = 0;
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

                settings.MoveEntry(entry, targetGroup, false, false);
                moved++;
            }

            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.EntryMoved);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Moved {moved} entries to '{targetGroupName}'" + (notFound > 0 ? $" ({notFound} not found)" : string.Empty),
                ["moved"] = moved,
                ["targetGroup"] = targetGroupName,
                ["notFound"] = notFound
            };
        }
    }
}
