using System.Collections.Generic;
using McpUnity.Resources;
using McpUnity.Services;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace McpUnity.Tools
{
    /// <summary>
    /// Finds all GameObjects with a component assignable to the requested component type.
    /// GetComponent(Type) includes components whose concrete type derives from the resolved type.
    /// </summary>
    public class GetGameObjectsByComponentTool : McpToolBase
    {
        internal const int DefaultLimit = 100;
        internal const int MaxLimit = 1000;

        public GetGameObjectsByComponentTool()
        {
            Name = "get_gameobjects_by_component";
            Description = "Finds ALL GameObjects with a component assignable to a resolved component type, so querying a base type such as 'Collider' includes derived components such as BoxCollider. The type may be a short name, full name, or assembly-qualified name. Returns canonical hierarchy paths and component data. 'compact' defaults to true unless componentFilter is provided; set compact=false for full component property dumps, or use componentFilter to keep detailed dumps only for selected component types.";
        }

        public override JObject Execute(JObject parameters)
        {
            string componentTypeName = parameters?["componentType"]?.ToObject<string>();
            if (string.IsNullOrEmpty(componentTypeName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Missing required parameter: componentType",
                    "validation_error"
                );
            }

            bool includeInactive = parameters?["includeInactive"]?.ToObject<bool?>() ?? true;
            int maxDepth = parameters?["maxDepth"]?.ToObject<int?>() ?? 0;
            bool includeChildren = parameters?["includeChildren"]?.ToObject<bool?>() ?? false;
            int limit = parameters?["limit"]?.ToObject<int?>() ?? DefaultLimit;

            HashSet<string> componentFilter = null;
            if (parameters?["componentFilter"] is JArray componentFilterArray
                && componentFilterArray.Count > 0)
            {
                componentFilter = new HashSet<string>();
                foreach (var item in componentFilterArray)
                    componentFilter.Add(item.ToString());
            }

            // Component queries default to compact output unless a componentFilter implicitly
            // requests filtered detail. An explicit compact=true still wins and ignores the filter.
            bool compact = parameters?["compact"]?.ToObject<bool?>() ?? (componentFilter == null);
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

            System.Type resolvedType = ComponentTypeResolver.FindComponentType(
                componentTypeName,
                targetGameObject: null,
                out string warning,
                out string ambiguityError);
            if (!string.IsNullOrEmpty(ambiguityError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    ambiguityError,
                    "component_ambiguity_error");
            }

            if (resolvedType == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Component type '{componentTypeName}' not found in Unity",
                    "component_error"
                );
            }

            var matches = new List<GameObject>();
            var total = 0;
            JObject scopeError = PrefabSessionScope.TryGetPrefabRoot(out GameObject prefabRoot);
            if (scopeError != null) return scopeError;

            if (prefabRoot != null)
            {
                total = CollectMatchesRecursive(
                    prefabRoot, resolvedType, includeInactive, limit, matches);
            }
            else
            {
                var inactiveMode = includeInactive
                    ? UnityEngine.FindObjectsInactive.Include
                    : UnityEngine.FindObjectsInactive.Exclude;
                var all = Object.FindObjectsByType<GameObject>(inactiveMode, FindObjectsSortMode.None);
                foreach (var go in all)
                {
                    if (!PrefabSessionScope.IsLoadedNonPreviewSceneObject(go)
                        || go.GetComponent(resolvedType) == null)
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
                    data["path"] = GameObjectPathUtils.GetCanonicalPath(go);
                    results.Add(data);
                }
            }

            var response = new JObject
            {
                ["success"] = true,
                ["message"] = truncated
                    ? $"Found {results.Count} of {total} GameObject(s) with component '{resolvedType.FullName}' (limit {limit} reached — results truncated)"
                    : $"Found {results.Count} GameObject(s) with component '{resolvedType.FullName}'",
                ["componentType"] = resolvedType.FullName,
                ["count"] = results.Count,
                ["total"] = total,
                ["truncated"] = truncated,
                ["gameObjects"] = results
            };
            if (warning != null)
                response["warnings"] = new JArray { warning };
            return response;
        }

        private static int CollectMatchesRecursive(
            GameObject root,
            System.Type resolvedType,
            bool includeInactive,
            int limit,
            List<GameObject> matches)
        {
            if (root == null) return 0;
            if (!includeInactive && !root.activeInHierarchy) return 0;

            var total = 0;

            if (root.GetComponent(resolvedType) != null)
            {
                total++;
                if (matches.Count < limit)
                    matches.Add(root);
            }

            foreach (Transform child in root.transform)
                total += CollectMatchesRecursive(
                    child.gameObject, resolvedType, includeInactive, limit, matches);

            return total;
        }
    }
}
