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
            Description = "Finds ALL GameObjects whose name matches a glob pattern (supports '*' and '?'). Returns an array of matches with hierarchical paths and component data. Use this instead of get_gameobject when there are multiple instances of the same name (e.g. 'CBCardUI(Clone)').";
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
            var truncated = false;

            if (PrefabEditingService.IsEditing && PrefabEditingService.PrefabRoot != null)
            {
                truncated = CollectMatchesRecursive(
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

                    if (matches.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }

                    matches.Add(go);
                }
            }

            var results = new JArray();
            foreach (var go in matches)
            {
                JObject data = GetGameObjectResource.GameObjectToJObject(
                    go, true, maxDepth, 0, includeChildren);
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
                    ? $"Found {results.Count} GameObject(s) matching '{pattern}' (limit {limit} reached — results truncated)"
                    : $"Found {results.Count} GameObject(s) matching '{pattern}'",
                ["pattern"] = pattern,
                ["count"] = results.Count,
                ["truncated"] = truncated,
                ["gameObjects"] = results
            };
        }

        private static bool CollectMatchesRecursive(
            GameObject root,
            Regex regex,
            bool includeInactive,
            int limit,
            List<GameObject> matches)
        {
            if (root == null) return false;
            if (!includeInactive && !root.activeInHierarchy) return false;

            if (regex.IsMatch(root.name))
            {
                if (matches.Count >= limit)
                    return true;
                matches.Add(root);
            }

            foreach (Transform child in root.transform)
            {
                if (CollectMatchesRecursive(child.gameObject, regex, includeInactive, limit, matches))
                    return true;
            }

            return false;
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
