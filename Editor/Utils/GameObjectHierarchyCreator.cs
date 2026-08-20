using System;
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

            path = path.Trim('/');
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("GameObject path cannot consist only of slashes.", nameof(path));
            }

            string[] parts = path.Split('/');
            JObject scopeError = PrefabSessionScope.TryGetPrefabRoot(out GameObject prefabRoot);
            if (scopeError != null)
                return scopeError;

            GameObject currentParent = prefabRoot;
            int startIndex = 0;
            if (prefabRoot != null)
            {
                if (parts[0] != prefabRoot.name)
                    return PrefabSessionScope.CreatePathContextMissError(path, prefabRoot);

                foundOrCreatedObject = prefabRoot;
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Length; i++)
            {
                string name = parts[i];
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException($"Invalid path: empty segment at part {i + 1} in path '{path}'. Ensure segments are not empty.");
                }

                Transform childTransform;
                if (currentParent == null)
                {
                    JObject rootError = PrefabSessionScope.TryResolveGameObject(
                        null, name, out GameObject rootObj);
                    if (rootError != null)
                        return rootError;
                    childTransform = rootObj?.transform;
                }
                else
                {
                    childTransform = currentParent.transform.Find(name);
                }

                if (childTransform == null)
                {
                    GameObject newObj = new GameObject(name);
                    if (!PrefabSessionScope.HasActiveSession)
                        Undo.RegisterCreatedObjectUndo(newObj, $"Create {name}");
                    if (currentParent != null)
                    {
                        newObj.transform.SetParent(currentParent.transform, false);

                        // Auto-add RectTransform for objects created under a Canvas hierarchy
                        if (currentParent.GetComponentInParent<Canvas>() != null
                            && newObj.GetComponent<RectTransform>() == null)
                        {
                            if (PrefabSessionScope.HasActiveSession)
                                newObj.AddComponent<RectTransform>();
                            else
                                Undo.AddComponent<RectTransform>(newObj);
                        }
                    }
                    foundOrCreatedObject = newObj;
                    currentParent = newObj;
                }
                else
                {
                    foundOrCreatedObject = childTransform.gameObject;
                    currentParent = foundOrCreatedObject;
                }
            }

            if (foundOrCreatedObject == null)
            {
                throw new InvalidOperationException($"Failed to find or create GameObject for path '{path}'. This indicates an unexpected state.");
            }

            return null;
        }
    }
}
