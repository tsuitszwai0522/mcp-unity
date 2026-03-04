using System;
using UnityEngine;
using UnityEditor; // Required for Undo operations
using McpUnity.Services;

namespace McpUnity.Utils
{
    public static class GameObjectHierarchyCreator
    {
        public static GameObject FindOrCreateHierarchicalGameObject(string path)
        {
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
            GameObject currentParent = null;
            GameObject foundOrCreatedObject = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string name = parts[i];
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException($"Invalid path: empty segment at part {i + 1} in path '{path}'. Ensure segments are not empty.");
                }

                Transform childTransform;
                if (currentParent == null)
                {
                    GameObject rootObj = null;

                    // When editing a prefab, prioritize the prefab editing context
                    if (PrefabEditingService.IsEditing)
                    {
                        if (PrefabEditingService.PrefabRoot.name == name)
                        {
                            rootObj = PrefabEditingService.PrefabRoot;
                        }
                        else
                        {
                            // Check direct children of prefab root
                            Transform childOfRoot = PrefabEditingService.PrefabRoot.transform.Find(name);
                            if (childOfRoot != null)
                                rootObj = childOfRoot.gameObject;
                        }
                    }

                    // Fallback to scene search only if not found in prefab editing context
                    if (rootObj == null)
                        rootObj = GameObject.Find(name);

                    childTransform = rootObj?.transform;
                }
                else
                {
                    childTransform = currentParent.transform.Find(name);
                }

                if (childTransform == null)
                {
                    GameObject newObj = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(newObj, $"Create {name}");
                    if (currentParent != null)
                    {
                        newObj.transform.SetParent(currentParent.transform, false);

                        // Auto-add RectTransform for objects created under a Canvas hierarchy
                        if (currentParent.GetComponentInParent<Canvas>() != null
                            && newObj.GetComponent<RectTransform>() == null)
                        {
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

            return foundOrCreatedObject;
        }
    }
}
