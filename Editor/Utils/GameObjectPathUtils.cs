using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared canonical hierarchy-path generation and ambiguity-aware resolution.
    /// </summary>
    public static class GameObjectPathUtils
    {
        public sealed class Candidate
        {
            internal Candidate(GameObject gameObject)
            {
                GameObject = gameObject;
                InstanceId = gameObject.GetInstanceID();
                Path = GetCanonicalPath(gameObject);
                SceneName = string.IsNullOrEmpty(gameObject.scene.name)
                    ? "<unnamed>"
                    : gameObject.scene.name;
            }

            public int InstanceId { get; }
            public string Path { get; }
            public string SceneName { get; }
            internal GameObject GameObject { get; }
        }

        /// <summary>
        /// Build a canonical path without a leading slash. A segment receives a same-name
        /// index only when another sibling (or loaded-scene root) has the same literal name.
        /// </summary>
        public static string GetCanonicalPath(GameObject gameObject)
        {
            return gameObject == null ? null : GetCanonicalPath(gameObject.transform);
        }

        public static string GetCanonicalPath(Transform transform)
        {
            if (transform == null)
                return null;

            var hierarchy = new List<Transform>();
            for (Transform current = transform; current != null; current = current.parent)
                hierarchy.Add(current);
            hierarchy.Reverse();

            var path = new StringBuilder();
            for (int i = 0; i < hierarchy.Count; i++)
            {
                if (i > 0)
                    path.Append('/');

                Transform current = hierarchy[i];
                int sameNameCount;
                int sameNameIndex;
                if (current.parent == null)
                {
                    GetRootNamePosition(
                        current.gameObject, out sameNameCount, out sameNameIndex);
                }
                else
                {
                    GetChildNamePosition(
                        current.parent, current, out sameNameCount, out sameNameIndex);
                }

                path.Append(EncodeSegment(
                    current.name,
                    sameNameCount > 1 ? (int?)sameNameIndex : null));
            }

            return path.ToString();
        }

        /// <summary>
        /// Resolve a path inside one Prefab preview root. A false result with candidates means
        /// ambiguity; a false result without candidates means not found or invalid syntax.
        /// </summary>
        public static bool TryResolveFromRoot(
            GameObject root,
            string objectPath,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage)
        {
            return TryResolveFromRoot(
                root,
                objectPath,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out _);
        }

        internal static bool TryResolveFromRoot(
            GameObject root,
            string objectPath,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage,
            out string notFoundHint)
        {
            var roots = new List<GameObject>();
            if (root != null)
                roots.Add(root);
            return TryResolve(
                roots,
                objectPath,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out notFoundHint);
        }

        /// <summary>
        /// Resolve a path across every loaded non-preview scene. Root-name ambiguity is checked
        /// across scene boundaries using scene order followed by root sibling order.
        /// </summary>
        public static bool TryResolveInLoadedScenes(
            string objectPath,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage)
        {
            return TryResolveInLoadedScenes(
                objectPath,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out _);
        }

        internal static bool TryResolveInLoadedScenes(
            string objectPath,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage,
            out string notFoundHint)
        {
            List<GameObject> roots = GetLoadedNonPreviewSceneRoots();

            return TryResolve(
                roots,
                objectPath,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out notFoundHint);
        }

        internal static bool TryResolveDirectChild(
            GameObject parent,
            string encodedSegment,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage)
        {
            return TryResolveDirectChild(
                parent,
                encodedSegment,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out _);
        }

        internal static bool TryResolveDirectChild(
            GameObject parent,
            string encodedSegment,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage,
            out string notFoundHint)
        {
            var children = new List<GameObject>();
            if (parent != null)
            {
                for (int childIndex = 0; childIndex < parent.transform.childCount; childIndex++)
                    children.Add(parent.transform.GetChild(childIndex).gameObject);
            }

            string location = parent == null
                ? "at an unavailable hierarchy level"
                : $"under '{GetCanonicalPath(parent)}'";
            return TrySelectCandidate(
                children,
                encodedSegment,
                location,
                out gameObject,
                out ambiguityCandidates,
                out errorMessage,
                out notFoundHint);
        }

        internal static IReadOnlyList<Candidate> FindAllByNameFromRoot(
            GameObject root,
            string literalName)
        {
            var matches = new List<GameObject>();
            CollectNameMatches(root, literalName, true, matches);
            return ToCandidates(matches);
        }

        internal static IReadOnlyList<Candidate> FindAllByNameInLoadedScenes(
            string literalName)
        {
            var matches = new List<GameObject>();
            foreach (GameObject root in GetLoadedNonPreviewSceneRoots())
                CollectNameMatches(root, literalName, true, matches);
            return ToCandidates(matches);
        }

        internal static IReadOnlyList<Candidate> FindNestedByNameInLoadedScenes(
            string literalName)
        {
            var matches = new List<GameObject>();
            foreach (GameObject root in GetLoadedNonPreviewSceneRoots())
                CollectNameMatches(root, literalName, false, matches);
            return ToCandidates(matches);
        }

        internal static string EncodeSegment(string literalName, int? sameNameIndex)
        {
            string escaped = (literalName ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("/", "\\/");
            int trailingIndexStart = FindTrailingNumericBracketStart(escaped);
            if (trailingIndexStart >= 0)
            {
                escaped = escaped.Substring(0, trailingIndexStart)
                    + "\\["
                    + escaped.Substring(trailingIndexStart + 1);
            }

            return sameNameIndex.HasValue
                ? escaped + "[" + sameNameIndex.Value + "]"
                : escaped;
        }

        internal static bool HasExplicitPathSyntax(string value)
        {
            if (value == null)
                return false;
            if (value.Length == 0 || value[0] == '/')
                return true;

            string[] segments = SplitPath(value);
            if (segments.Length > 1)
                return true;

            foreach (string segment in segments)
            {
                if (TryDecodeSegment(segment, out _, out int? sameNameIndex, out _)
                    && sameNameIndex.HasValue)
                {
                    return true;
                }

                for (int i = 0; i + 1 < segment.Length; i++)
                {
                    if (segment[i] != '\\')
                        continue;

                    char escaped = segment[i + 1];
                    if (escaped == '\\' || escaped == '[' || escaped == '/')
                        return true;
                    i++;
                }
            }

            return false;
        }

        internal static string[] SplitPath(string objectPath)
        {
            if (objectPath == null)
                return Array.Empty<string>();

            int startIndex = objectPath.Length > 0 && objectPath[0] == '/' ? 1 : 0;
            var segments = new List<string>();
            var segment = new StringBuilder();
            for (int i = startIndex; i < objectPath.Length; i++)
            {
                char current = objectPath[i];
                if (current == '/'
                    && CountPrecedingBackslashes(objectPath, i) % 2 == 0)
                {
                    segments.Add(segment.ToString());
                    segment.Clear();
                    continue;
                }

                segment.Append(current);
            }
            segments.Add(segment.ToString());
            return segments.ToArray();
        }

        private static bool TryResolve(
            List<GameObject> roots,
            string objectPath,
            out GameObject gameObject,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage,
            out string notFoundHint)
        {
            gameObject = null;
            ambiguityCandidates = Array.Empty<Candidate>();
            errorMessage = null;
            notFoundHint = null;

            if (objectPath == null)
                return false;

            string[] segments = SplitPath(objectPath);
            if (!TrySelectCandidate(
                    roots,
                    segments[0],
                    "among loaded hierarchy roots",
                    out gameObject,
                    out ambiguityCandidates,
                    out errorMessage,
                    out notFoundHint))
            {
                return false;
            }

            for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                var children = new List<GameObject>();
                Transform parent = gameObject.transform;
                for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
                    children.Add(parent.GetChild(childIndex).gameObject);

                if (!TrySelectCandidate(
                        children,
                        segments[segmentIndex],
                        $"under '{GetCanonicalPath(gameObject)}'",
                        out gameObject,
                        out ambiguityCandidates,
                        out errorMessage,
                        out notFoundHint))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TrySelectCandidate(
            List<GameObject> availableObjects,
            string encodedSegment,
            string locationDescription,
            out GameObject selected,
            out IReadOnlyList<Candidate> ambiguityCandidates,
            out string errorMessage,
            out string notFoundHint)
        {
            selected = null;
            ambiguityCandidates = Array.Empty<Candidate>();
            errorMessage = null;
            notFoundHint = null;

            if (!TryDecodeSegment(
                    encodedSegment, out string literalName, out int? sameNameIndex, out errorMessage))
            {
                return false;
            }

            var sameNameObjects = new List<GameObject>();
            foreach (GameObject availableObject in availableObjects)
            {
                if (availableObject != null && availableObject.name == literalName)
                    sameNameObjects.Add(availableObject);
            }

            if (sameNameIndex.HasValue)
            {
                if (sameNameIndex.Value < sameNameObjects.Count)
                {
                    selected = sameNameObjects[sameNameIndex.Value];
                    return true;
                }

                string literalSegmentName = UnescapeSegment(encodedSegment);
                bool hasLiteralName = false;
                foreach (GameObject availableObject in availableObjects)
                {
                    if (availableObject != null && availableObject.name == literalSegmentName)
                    {
                        hasLiteralName = true;
                        break;
                    }
                }

                notFoundHint =
                    $"Path segment '{encodedSegment}' is interpreted as same-name index " +
                    $"{sameNameIndex.Value} for GameObjects named '{literalName}', but that " +
                    $"candidate does not exist {locationDescription}.";
                if (hasLiteralName)
                {
                    notFoundHint +=
                        $" A GameObject is literally named '{literalSegmentName}'; address it as " +
                        $"'{EncodeSegment(literalSegmentName, null)}' by escaping the literal " +
                        "trailing numeric bracket.";
                }
                return false;
            }

            if (sameNameObjects.Count == 0)
                return false;
            if (sameNameObjects.Count == 1)
            {
                selected = sameNameObjects[0];
                return true;
            }

            var candidates = new List<Candidate>(sameNameObjects.Count);
            foreach (GameObject candidate in sameNameObjects)
                candidates.Add(new Candidate(candidate));
            ambiguityCandidates = candidates;

            var summary = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0)
                    summary.Append(", ");
                Candidate candidate = candidates[i];
                summary.Append('\'').Append(candidate.Path).Append("' (instanceId=")
                    .Append(candidate.InstanceId).Append(", scene='")
                    .Append(candidate.SceneName).Append("')");
            }

            errorMessage =
                $"Object path is ambiguous at segment '{encodedSegment}': " +
                $"{candidates.Count} candidates {locationDescription}: {summary}. " +
                "Use instanceId or one of the canonical indexed paths.";
            return false;
        }

        internal static bool TryDecodeSegment(
            string encodedSegment,
            out string literalName,
            out int? sameNameIndex,
            out string errorMessage)
        {
            literalName = null;
            sameNameIndex = null;
            errorMessage = null;

            int trailingIndexStart = FindTrailingNumericBracketStart(encodedSegment);
            if (trailingIndexStart >= 0
                && CountPrecedingBackslashes(encodedSegment, trailingIndexStart) % 2 == 0)
            {
                string indexText = encodedSegment.Substring(
                    trailingIndexStart + 1,
                    encodedSegment.Length - trailingIndexStart - 2);
                if (!int.TryParse(indexText, out int parsedIndex))
                {
                    errorMessage =
                        $"Path segment '{encodedSegment}' contains a same-name index that is " +
                        "too large to resolve.";
                    return false;
                }

                sameNameIndex = parsedIndex;
                literalName = UnescapeSegment(encodedSegment.Substring(0, trailingIndexStart));
                return true;
            }

            literalName = UnescapeSegment(encodedSegment);
            return true;
        }

        private static string UnescapeSegment(string encodedSegment)
        {
            var unescaped = new StringBuilder(encodedSegment.Length);
            for (int i = 0; i < encodedSegment.Length; i++)
            {
                char current = encodedSegment[i];
                if (current == '\\' && i + 1 < encodedSegment.Length)
                {
                    char next = encodedSegment[i + 1];
                    if (next == '\\' || next == '[' || next == '/')
                    {
                        unescaped.Append(next);
                        i++;
                        continue;
                    }
                }
                unescaped.Append(current);
            }
            return unescaped.ToString();
        }

        private static int FindTrailingNumericBracketStart(string value)
        {
            if (string.IsNullOrEmpty(value) || value[value.Length - 1] != ']')
                return -1;

            int openBracket = value.LastIndexOf('[');
            if (openBracket < 0 || openBracket == value.Length - 2)
                return -1;

            for (int i = openBracket + 1; i < value.Length - 1; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                    return -1;
            }
            return openBracket;
        }

        private static int CountPrecedingBackslashes(string value, int position)
        {
            int count = 0;
            for (int i = position - 1; i >= 0 && value[i] == '\\'; i--)
                count++;
            return count;
        }

        /// <summary>
        /// Upper bound for name-scan candidate collection. Callers use these lists to detect
        /// ambiguity (count > 1) and to render candidate lists in error payloads; both purposes
        /// are served by a bounded prefix, while an unbounded scan would walk entire content
        /// scenes on the main thread for common names (review finding, audit #3).
        /// </summary>
        internal const int MaxNameScanMatches = 20;

        private static void CollectNameMatches(
            GameObject current,
            string literalName,
            bool includeCurrent,
            List<GameObject> matches)
        {
            if (current == null || matches.Count >= MaxNameScanMatches)
                return;

            if (includeCurrent && current.name == literalName)
                matches.Add(current);

            foreach (Transform child in current.transform)
            {
                if (matches.Count >= MaxNameScanMatches)
                    return;
                CollectNameMatches(child.gameObject, literalName, true, matches);
            }
        }

        private static IReadOnlyList<Candidate> ToCandidates(List<GameObject> gameObjects)
        {
            var candidates = new List<Candidate>(gameObjects.Count);
            foreach (GameObject gameObject in gameObjects)
                candidates.Add(new Candidate(gameObject));
            return candidates;
        }

        private static void GetChildNamePosition(
            Transform parent,
            Transform child,
            out int sameNameCount,
            out int sameNameIndex)
        {
            sameNameCount = 0;
            sameNameIndex = -1;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform sibling = parent.GetChild(i);
                if (sibling.name != child.name)
                    continue;

                if (sibling == child)
                    sameNameIndex = sameNameCount;
                sameNameCount++;
            }
        }

        private static void GetRootNamePosition(
            GameObject root,
            out int sameNameCount,
            out int sameNameIndex)
        {
            sameNameCount = 0;
            sameNameIndex = -1;
            foreach (GameObject candidate in GetComparableRoots(root))
            {
                if (candidate == null || candidate.name != root.name)
                    continue;

                if (candidate == root)
                    sameNameIndex = sameNameCount;
                sameNameCount++;
            }

            if (sameNameIndex < 0)
            {
                sameNameCount = 1;
                sameNameIndex = 0;
            }
        }

        private static List<GameObject> GetComparableRoots(GameObject root)
        {
            var roots = new List<GameObject>();
            Scene rootScene = root.scene;
            if (!rootScene.IsValid())
            {
                roots.Add(root);
                return roots;
            }

            if (IsLoadedNonPreviewScene(rootScene))
            {
                return GetLoadedNonPreviewSceneRoots();
            }

            // A Prefab session exposes exactly one addressable preview root. Other roots in the
            // preview scene are outside that session's resolver source and must not affect its path.
            roots.Add(root);
            return roots;
        }

        private static List<GameObject> GetLoadedNonPreviewSceneRoots()
        {
            var roots = new List<GameObject>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (IsLoadedNonPreviewScene(scene))
                    roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }

        private static bool IsLoadedNonPreviewScene(Scene scene)
        {
            return scene.IsValid()
                && scene.isLoaded
                && !EditorSceneManager.IsPreviewScene(scene);
        }
    }
}
