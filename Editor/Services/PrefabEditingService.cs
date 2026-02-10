using System;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Services
{
    /// <summary>
    /// Static service for managing Prefab Edit Mode.
    /// Uses PrefabUtility.LoadPrefabContents() to load a Prefab into an isolated environment
    /// for structural editing, then saves it back to the .prefab asset.
    /// Only one Prefab can be edited at a time.
    /// </summary>
    public static class PrefabEditingService
    {
        private static GameObject _prefabRoot;
        private static string _assetPath;

        /// <summary>
        /// Whether a Prefab is currently being edited
        /// </summary>
        public static bool IsEditing => _prefabRoot != null;

        /// <summary>
        /// The root GameObject of the currently loaded Prefab contents
        /// </summary>
        public static GameObject PrefabRoot => _prefabRoot;

        /// <summary>
        /// The asset path of the currently loaded Prefab
        /// </summary>
        public static string AssetPath => _assetPath;

        /// <summary>
        /// Load a Prefab's contents into an isolated editing environment
        /// </summary>
        /// <param name="assetPath">Asset path to the .prefab file</param>
        /// <returns>The root GameObject of the loaded Prefab contents</returns>
        public static GameObject Open(string assetPath)
        {
            if (IsEditing)
            {
                throw new InvalidOperationException(
                    $"A Prefab is already being edited: '{_assetPath}'. " +
                    "Call Save() or Discard() before opening another Prefab.");
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));
            }

            _assetPath = assetPath;
            _prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            return _prefabRoot;
        }

        /// <summary>
        /// Save the current Prefab edits and unload the contents
        /// </summary>
        public static void Save()
        {
            if (!IsEditing)
            {
                throw new InvalidOperationException("No Prefab is currently being edited.");
            }

            PrefabUtility.SaveAsPrefabAsset(_prefabRoot, _assetPath);
            PrefabUtility.UnloadPrefabContents(_prefabRoot);
            _prefabRoot = null;
            _assetPath = null;
        }

        /// <summary>
        /// Discard changes and unload the Prefab contents
        /// </summary>
        public static void Discard()
        {
            if (!IsEditing)
            {
                throw new InvalidOperationException("No Prefab is currently being edited.");
            }

            PrefabUtility.UnloadPrefabContents(_prefabRoot);
            _prefabRoot = null;
            _assetPath = null;
        }

        /// <summary>
        /// Find a GameObject within the loaded Prefab by a path relative to the Prefab root.
        /// Supports paths like "PrefabRoot/Child/SubChild" or just "Child/SubChild".
        /// </summary>
        /// <param name="path">Hierarchy path to search for</param>
        /// <returns>The found GameObject, or null if not found</returns>
        public static GameObject FindByPath(string path)
        {
            if (!IsEditing || string.IsNullOrEmpty(path))
                return null;

            path = path.TrimStart('/');
            string[] parts = path.Split('/');

            if (parts.Length == 0)
                return null;

            // Check if the first part matches the Prefab root name
            if (parts[0] != _prefabRoot.name)
                return null;

            // If only the root name was provided, return the root
            if (parts.Length == 1)
                return _prefabRoot;

            // Traverse children starting from the root
            Transform current = _prefabRoot.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.Find(parts[i]);
                if (child == null)
                    return null;
                current = child;
            }

            return current.gameObject;
        }
    }
}
