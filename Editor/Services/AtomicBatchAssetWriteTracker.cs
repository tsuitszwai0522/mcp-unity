using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace McpUnity.Services
{
    /// <summary>
    /// Records asset paths that Unity reports as saved or postprocessed during an atomic batch.
    /// </summary>
    [InitializeOnLoad]
    internal static class AtomicBatchAssetWriteTracker
    {
        private static readonly Dictionary<int, HashSet<string>> ActiveCollections =
            new Dictionary<int, HashSet<string>>();
        private static bool _isCollecting;
        private static int _nextCollectionId;

        static AtomicBatchAssetWriteTracker()
        {
            ResetAll();
            AssemblyReloadEvents.beforeAssemblyReload -= ResetAll;
            AssemblyReloadEvents.beforeAssemblyReload += ResetAll;
        }

        internal static int Begin()
        {
            int collectionId = ++_nextCollectionId;
            ActiveCollections.Add(
                collectionId,
                new HashSet<string>(StringComparer.Ordinal));
            _isCollecting = true;
            return collectionId;
        }

        internal static string[] End(int collectionId)
        {
            if (!ActiveCollections.TryGetValue(collectionId, out HashSet<string> paths))
                return Array.Empty<string>();

            ActiveCollections.Remove(collectionId);
            _isCollecting = ActiveCollections.Count > 0;
            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        internal static void ResetAll()
        {
            ActiveCollections.Clear();
            _isCollecting = false;
            _nextCollectionId = 0;
        }

        internal static void RecordSavedPaths(string[] paths)
        {
            if (!_isCollecting)
                return;

            Record(paths);
        }

        internal static void RecordPostprocessedPaths(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!_isCollecting)
                return;

            Record(importedAssets);
            Record(deletedAssets);
            Record(movedAssets);
            Record(movedFromAssetPaths);
        }

        private static void Record(string[] paths)
        {
            if (paths == null)
                return;

            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (HashSet<string> collection in ActiveCollections.Values)
                        collection.Add(path);
                }
            }
        }
    }

    public sealed class AtomicBatchAssetModificationProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            AtomicBatchAssetWriteTracker.RecordSavedPaths(paths);
            return paths;
        }
    }

    public sealed class AtomicBatchAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            AtomicBatchAssetWriteTracker.RecordPostprocessedPaths(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths);
        }
    }
}
