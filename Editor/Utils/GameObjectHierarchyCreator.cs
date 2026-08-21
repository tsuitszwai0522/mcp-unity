using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor; // Required for Undo operations
using McpUnity.Services;
using Newtonsoft.Json.Linq;

namespace McpUnity.Utils
{
    public static class GameObjectHierarchyCreator
    {
        public static JObject TryFindOrCreateHierarchicalGameObject(
            string path,
            out GameObject foundOrCreatedObject)
        {
            foundOrCreatedObject = null;
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("GameObject path cannot be null or empty.", nameof(path));
            }

            string[] parts = GameObjectPathUtils.SplitPath(path);
            JObject scopeError = PrefabSessionScope.TryGetPrefabRoot(out GameObject prefabRoot);
            if (scopeError != null)
                return scopeError;

            var createdObjects = new List<GameObject>();
            bool completed = false;
            try
            {
                GameObject currentParent = prefabRoot;
                int startIndex = 0;
                if (prefabRoot != null)
                {
                    bool resolvedRoot = GameObjectPathUtils.TryResolveFromRoot(
                        prefabRoot,
                        parts[0],
                        out GameObject resolvedPrefabRoot,
                        out IReadOnlyList<GameObjectPathUtils.Candidate> rootCandidates,
                        out string rootResolutionError,
                        out string rootNotFoundHint);
                    if (rootCandidates.Count > 0)
                    {
                        return PrefabSessionScope.CreateObjectPathAmbiguityError(
                            path, rootCandidates, rootResolutionError);
                    }
                    if (!resolvedRoot)
                    {
                        return PrefabSessionScope.CreatePathContextMissError(
                            path,
                            prefabRoot,
                            rootResolutionError ?? rootNotFoundHint);
                    }

                    foundOrCreatedObject = resolvedPrefabRoot;
                    startIndex = 1;
                }

                for (int i = startIndex; i < parts.Length; i++)
                {
                    string name = parts[i];
                    if (!GameObjectPathUtils.TryDecodeSegment(
                            name,
                            out string literalName,
                            out _,
                            out string decodeError))
                    {
                        return CreatePathResolutionError(path, prefabRoot, decodeError);
                    }

                    GameObject resolvedObject;
                    IReadOnlyList<GameObjectPathUtils.Candidate> candidates;
                    string resolutionError;
                    string notFoundHint;
                    bool resolved;
                    if (currentParent == null)
                    {
                        resolved = GameObjectPathUtils.TryResolveInLoadedScenes(
                            name,
                            out resolvedObject,
                            out candidates,
                            out resolutionError,
                            out notFoundHint);
                    }
                    else
                    {
                        resolved = GameObjectPathUtils.TryResolveDirectChild(
                            currentParent,
                            name,
                            out resolvedObject,
                            out candidates,
                            out resolutionError,
                            out notFoundHint);
                    }

                    if (candidates.Count > 0)
                    {
                        return PrefabSessionScope.CreateObjectPathAmbiguityError(
                            path, candidates, resolutionError);
                    }
                    string notFoundMessage = resolutionError ?? notFoundHint;
                    if (!resolved && !string.IsNullOrEmpty(notFoundMessage))
                    {
                        return CreatePathResolutionError(
                            path, prefabRoot, notFoundMessage);
                    }

                    if (!resolved && currentParent == null && i == 0)
                    {
                        IReadOnlyList<GameObjectPathUtils.Candidate> nestedNameCandidates =
                            GameObjectPathUtils.FindNestedByNameInLoadedScenes(literalName);
                        if (nestedNameCandidates.Count > 0)
                        {
                            return PrefabSessionScope.CreateRootPathNotFoundError(
                                path, literalName, nestedNameCandidates);
                        }
                    }

                    if (!resolved)
                    {
                        GameObject newObj = new GameObject(literalName);
                        createdObjects.Add(newObj);
                        if (currentParent != null)
                        {
                            newObj.transform.SetParent(currentParent.transform, false);

                            // Auto-add RectTransform for objects created under a Canvas hierarchy.
                            // Undo registration is deferred until the whole path succeeds so a
                            // later resolution failure can roll back without leaving Undo state.
                            if (currentParent.GetComponentInParent<Canvas>() != null
                                && newObj.GetComponent<RectTransform>() == null)
                            {
                                newObj.AddComponent<RectTransform>();
                            }
                        }
                        foundOrCreatedObject = newObj;
                        currentParent = newObj;
                    }
                    else
                    {
                        foundOrCreatedObject = resolvedObject;
                        currentParent = foundOrCreatedObject;
                    }
                }

                if (foundOrCreatedObject == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to find or create GameObject for path '{path}'. " +
                        "This indicates an unexpected state.");
                }

                if (!PrefabSessionScope.HasActiveSession)
                {
                    foreach (GameObject createdObject in createdObjects)
                    {
                        if (createdObject != null)
                        {
                            Undo.RegisterCreatedObjectUndo(
                                createdObject, $"Create {createdObject.name}");
                        }
                    }
                }

                completed = true;
                return null;
            }
            finally
            {
                if (!completed)
                {
                    for (int i = createdObjects.Count - 1; i >= 0; i--)
                    {
                        GameObject createdObject = createdObjects[i];
                        if (createdObject != null)
                            UnityEngine.Object.DestroyImmediate(createdObject);
                    }
                    foundOrCreatedObject = null;
                }
            }
        }

        private static JObject CreatePathResolutionError(
            string path,
            GameObject prefabRoot,
            string resolutionError)
        {
            if (prefabRoot != null)
            {
                return PrefabSessionScope.CreatePathContextMissError(
                    path, prefabRoot, resolutionError);
            }

            return McpUnity.Unity.McpUnitySocketHandler.CreateErrorResponse(
                resolutionError,
                "not_found_error");
        }
    }
}
