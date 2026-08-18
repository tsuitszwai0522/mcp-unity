using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using McpUnity.Resources;
using McpUnity.Services;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace McpUnity.Tools
{
    /// <summary>
    /// Finds all GameObjects whose name matches a glob pattern (supports * and ?).
    /// Returns an array of matches, each with hierarchical path + full GameObject data.
    /// Complements get_gameobject (which only returns the first match).
    /// </summary>
    public class GetGameObjectsByNameTool : McpToolBase
    {
        internal const int DefaultLimit = 100;
        internal const int MaxLimit = 1000;

        public GetGameObjectsByNameTool()
        {
            Name = "get_gameobjects_by_name";
            Description = "Finds ALL GameObjects whose name matches a glob pattern (supports '*' and '?'). Returns an array of matches with hierarchical paths and component data. Use this instead of get_gameobject when there are multiple instances of the same name (e.g. 'CBCardUI(Clone)'). Use 'compact' to drop component property dumps (type+enabled only), or 'componentFilter' to keep dumps only for specific component types (e.g. ['RectTransform']) — both cut output size dramatically.";
        }

        public override JObject Execute(JObject parameters)
        {
            string pattern = parameters?["name"]?.ToObject<string>();
            if (string.IsNullOrEmpty(pattern))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Missing required parameter: name",
                    "validation_error"
                );
            }

            bool includeInactive = parameters?["includeInactive"]?.ToObject<bool?>() ?? true;
            int maxDepth = parameters?["maxDepth"]?.ToObject<int?>() ?? 0;
            bool includeChildren = parameters?["includeChildren"]?.ToObject<bool?>() ?? false;
            int limit = parameters?["limit"]?.ToObject<int?>() ?? DefaultLimit;

            // Output-size controls (default off = current verbose behavior):
            //  - compact: drop every component's property dump, keeping only { type, enabled }.
            //  - componentFilter: keep property dumps only for the listed component types (e.g. ["RectTransform"]),
            //    everything else collapses to { type, enabled }. Ignored when compact is true.
            bool compact = parameters?["compact"]?.ToObject<bool?>() ?? false;
            HashSet<string> componentFilter = null;
            if (parameters?["componentFilter"] is JArray componentFilterArray && componentFilterArray.Count > 0)
            {
                componentFilter = new HashSet<string>();
                foreach (var item in componentFilterArray)
                    componentFilter.Add(item.ToString());
            }
            bool includeDetailed = !compact;

            if (maxDepth < -1)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'maxDepth' must be -1 or greater",
                    "validation_error"
                );
            }

            if (limit < 1 || limit > MaxLimit)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter 'limit' must be between 1 and {MaxLimit}",
                    "validation_error"
                );
            }

            Regex regex;
            try
            {
                regex = new Regex("^" + GlobToRegex(pattern) + "$");
            }
            catch (System.Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid glob pattern '{pattern}': {ex.Message}",
                    "validation_error"
                );
            }

            var matches = new List<GameObject>();
            var total = 0;

            if (PrefabEditingService.IsEditing && PrefabEditingService.PrefabRoot != null)
            {
                total = CollectMatchesRecursive(
                    PrefabEditingService.PrefabRoot, regex, includeInactive, limit, matches);
            }
            else
            {
                var inactiveMode = includeInactive
                    ? UnityEngine.FindObjectsInactive.Include
                    : UnityEngine.FindObjectsInactive.Exclude;
                var all = Object.FindObjectsByType<GameObject>(inactiveMode, FindObjectsSortMode.None);
                foreach (var go in all)
                {
                    if (!regex.IsMatch(go.name))
                        continue;

                    total++;
                    if (matches.Count < limit)
                        matches.Add(go);
                }
            }

            var truncated = total > matches.Count;

            var results = new JArray();
            foreach (var go in matches)
            {
                JObject data = GetGameObjectResource.GameObjectToJObject(
                    go, includeDetailed, maxDepth, 0, includeChildren, componentFilter);
                if (data != null)
                {
                    data["path"] = GetHierarchicalPath(go);
                    results.Add(data);
                }
            }

            return new JObject
            {
                ["success"] = true,
                ["message"] = truncated
                    ? $"Found {results.Count} of {total} GameObject(s) matching '{pattern}' (limit {limit} reached — results truncated)"
                    : $"Found {results.Count} GameObject(s) matching '{pattern}'",
                ["pattern"] = pattern,
                ["count"] = results.Count,
                ["total"] = total,
                ["truncated"] = truncated,
                ["gameObjects"] = results
            };
        }

        private static int CollectMatchesRecursive(
            GameObject root,
            Regex regex,
            bool includeInactive,
            int limit,
            List<GameObject> matches)
        {
            if (root == null) return 0;
            if (!includeInactive && !root.activeInHierarchy) return 0;

            var total = 0;

            if (regex.IsMatch(root.name))
            {
                total++;
                if (matches.Count < limit)
                    matches.Add(root);
            }

            foreach (Transform child in root.transform)
                total += CollectMatchesRecursive(
                    child.gameObject, regex, includeInactive, limit, matches);

            return total;
        }

        private static string GetHierarchicalPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, '/');
                sb.Insert(0, t.name);
                t = t.parent;
            }
            return sb.ToString();
        }

        private static string GlobToRegex(string glob)
        {
            var sb = new StringBuilder(glob.Length * 2);
            foreach (var c in glob)
            {
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '?': sb.Append('.'); break;
                    case '.': case '(': case ')': case '[': case ']':
                    case '{': case '}': case '+': case '^': case '$':
                    case '|': case '\\':
                        sb.Append('\\').Append(c); break;
                    default:
                        sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
