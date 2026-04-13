using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// List Addressables entries with optional filters. All filters are optional;
    /// <c>limit</c> guards against returning the entire project.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrListEntriesTool : McpToolBase
    {
        private const int DefaultLimit = 200;

        public AddrListEntriesTool()
        {
            Name = "addr_list_entries";
            Description = "List Unity Addressables entries with optional group/label/address/path filters";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""group"": { ""type"": ""string"", ""description"": ""Only list entries in this group"" },
                ""label_filter"": { ""type"": ""string"", ""description"": ""Only list entries containing this label"" },
                ""address_pattern"": { ""type"": ""string"", ""description"": ""Glob-style pattern on entry address (supports *)"" },
                ""asset_path_prefix"": { ""type"": ""string"", ""description"": ""Only list entries whose assetPath starts with this prefix"" },
                ""limit"": { ""type"": ""integer"", ""description"": ""Max entries to return (default 200)"" }
            }
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            string groupFilter = parameters["group"]?.ToString();
            string labelFilter = parameters["label_filter"]?.ToString();
            string addressPattern = parameters["address_pattern"]?.ToString();
            string pathPrefix = parameters["asset_path_prefix"]?.ToString();
            int limit = parameters["limit"]?.ToObject<int?>() ?? DefaultLimit;
            if (limit <= 0) limit = DefaultLimit;

            Regex addressRegex = null;
            if (!string.IsNullOrWhiteSpace(addressPattern))
            {
                var escaped = "^" + Regex.Escape(addressPattern).Replace("\\*", ".*") + "$";
                addressRegex = new Regex(escaped, RegexOptions.IgnoreCase);
            }

            var matched = new List<AddressableAssetEntry>();
            int total = 0;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                if (!string.IsNullOrWhiteSpace(groupFilter) && group.Name != groupFilter) continue;

                foreach (var entry in group.entries)
                {
                    if (!string.IsNullOrWhiteSpace(labelFilter) && !entry.labels.Contains(labelFilter)) continue;
                    if (!string.IsNullOrWhiteSpace(pathPrefix) && (entry.AssetPath == null || !entry.AssetPath.StartsWith(pathPrefix))) continue;
                    if (addressRegex != null && !addressRegex.IsMatch(entry.address ?? string.Empty)) continue;

                    total++;
                    if (matched.Count < limit)
                    {
                        matched.Add(entry);
                    }
                }
            }

            var entriesJson = new JArray();
            foreach (var entry in matched)
            {
                entriesJson.Add(AddrHelper.EntryToJson(entry));
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Returned {matched.Count} of {total} matched entries",
                ["total"] = total,
                ["truncated"] = total > matched.Count,
                ["entries"] = entriesJson
            };
        }
    }
}
