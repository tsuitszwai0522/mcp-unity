using System.Collections.Generic;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Services
{
    /// <summary>
    /// Central resolver and context boundary for Prefab contents editing sessions.
    /// </summary>
    public static class PrefabSessionScope
    {
        public const string ContextMissErrorType = "prefab_context_miss_error";
        public const string SessionLostErrorType = "prefab_session_lost_error";
        public const string ObjectPathAmbiguityErrorType = "object_path_ambiguity_error";

        public static bool HasActiveSession =>
            PrefabEditingService.Status == PrefabEditingSessionStatus.Active;

        public static JObject ValidateCanOpenPrefab()
        {
            switch (PrefabEditingService.Status)
            {
                case PrefabEditingSessionStatus.Active:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"A Prefab is already being edited: '{PrefabEditingService.AssetPath}'. " +
                        "Call save_prefab_contents first.",
                        "validation_error");
                case PrefabEditingSessionStatus.Lost:
                    return CreateSessionLostError();
                default:
                    return null;
            }
        }

        public static JObject RequireActiveSession(out GameObject root, out string assetPath)
        {
            root = null;
            assetPath = null;

            switch (PrefabEditingService.Status)
            {
                case PrefabEditingSessionStatus.Active:
                    root = PrefabEditingService.PrefabRoot;
                    assetPath = PrefabEditingService.AssetPath;
                    return null;
                case PrefabEditingSessionStatus.Lost:
                    return CreateSessionLostError();
                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        "No Prefab editing session has been opened. Call open_prefab_contents first.",
                        "validation_error");
            }
        }

        /// <summary>
        /// Returns the active Prefab root, null when no session has ever been opened, or
        /// an error when a persisted session could not be restored.
        /// </summary>
        public static JObject TryGetPrefabRoot(out GameObject root)
        {
            root = null;
            switch (PrefabEditingService.Status)
            {
                case PrefabEditingSessionStatus.Active:
                    root = PrefabEditingService.PrefabRoot;
                    return null;
                case PrefabEditingSessionStatus.Lost:
                    return CreateSessionLostError();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Resolve a GameObject while enforcing the active Prefab contents boundary.
        /// A null result without an error means the requested object does not exist outside
        /// a Prefab session and the caller may apply its existing not-found behaviour.
        /// </summary>
        public static JObject TryResolveGameObject(
            int? instanceId,
            string objectPath,
            out GameObject gameObject)
        {
            gameObject = null;
            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;

            if (instanceId.HasValue)
            {
                gameObject = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                return ValidateOperationTarget(instanceId.Value, gameObject, prefabRoot);
            }

            if (objectPath == null)
                return null;

            if (prefabRoot != null)
            {
                bool resolved = GameObjectPathUtils.TryResolveFromRoot(
                    prefabRoot,
                    objectPath,
                    out gameObject,
                    out IReadOnlyList<GameObjectPathUtils.Candidate> candidates,
                    out string resolutionError,
                    out string notFoundHint);
                if (candidates.Count > 0)
                    return CreateObjectPathAmbiguityError(objectPath, candidates, resolutionError);
                if (resolved)
                    return null;

                return CreatePathContextMissError(
                    objectPath,
                    prefabRoot,
                    resolutionError ?? notFoundHint);
            }

            return TryResolvePathInScenes(objectPath, out gameObject);
        }

        /// <summary>
        /// Resolve a dual-purpose ID/name/path text reference. Explicit canonical grammar is
        /// always treated as a path. A plain token first addresses a root path, then retains the
        /// legacy name lookup only when no root matched.
        /// </summary>
        public static JObject TryResolveGameObjectPathOrName(
            string reference,
            out GameObject gameObject)
        {
            gameObject = null;
            if (reference == null)
                return null;

            if (GameObjectPathUtils.HasExplicitPathSyntax(reference))
                return TryResolveGameObject(null, reference, out gameObject);

            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;

            bool resolved;
            IReadOnlyList<GameObjectPathUtils.Candidate> candidates;
            string resolutionError;
            string notFoundHint;
            if (prefabRoot != null)
            {
                resolved = GameObjectPathUtils.TryResolveFromRoot(
                    prefabRoot,
                    reference,
                    out gameObject,
                    out candidates,
                    out resolutionError,
                    out notFoundHint);
                if (candidates.Count > 0)
                    return CreateObjectPathAmbiguityError(reference, candidates, resolutionError);
                if (resolved)
                    return null;
                if (!string.IsNullOrEmpty(resolutionError)
                    || !string.IsNullOrEmpty(notFoundHint))
                {
                    return CreatePathContextMissError(
                        reference,
                        prefabRoot,
                        resolutionError ?? notFoundHint);
                }

                IReadOnlyList<GameObjectPathUtils.Candidate> nameCandidates =
                    GameObjectPathUtils.FindAllByNameFromRoot(prefabRoot, reference);
                JObject nameError = TrySelectUniqueNameCandidate(
                    reference, nameCandidates, out gameObject);
                if (nameError != null || gameObject != null)
                    return nameError;

                return CreatePathContextMissError(reference, prefabRoot);
            }

            resolved = GameObjectPathUtils.TryResolveInLoadedScenes(
                reference,
                out gameObject,
                out candidates,
                out resolutionError,
                out notFoundHint);
            if (candidates.Count > 0)
                return CreateObjectPathAmbiguityError(reference, candidates, resolutionError);
            if (!string.IsNullOrEmpty(resolutionError)
                || !string.IsNullOrEmpty(notFoundHint))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    resolutionError ?? notFoundHint,
                    "not_found_error");
            }
            if (resolved)
                return null;

            return TrySelectUniqueNameCandidate(
                reference,
                GameObjectPathUtils.FindAllByNameInLoadedScenes(reference),
                out gameObject);
        }

        /// <summary>
        /// Resolve a path for a polling condition. After the active Prefab root prefix matches,
        /// a syntactically valid descendant miss is soft, including canonical indexed segments.
        /// A root-prefix context miss, ambiguity, invalid syntax, or session failure is hard.
        /// </summary>
        public static JObject TryResolveGameObjectForPolling(
            string objectPath,
            out GameObject gameObject)
        {
            gameObject = null;
            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;
            if (objectPath == null)
                return null;

            bool resolved;
            IReadOnlyList<GameObjectPathUtils.Candidate> candidates;
            string resolutionError;
            if (prefabRoot != null)
            {
                string[] segments = GameObjectPathUtils.SplitPath(objectPath);
                bool resolvedRoot = GameObjectPathUtils.TryResolveFromRoot(
                    prefabRoot,
                    segments[0],
                    out _,
                    out IReadOnlyList<GameObjectPathUtils.Candidate> rootCandidates,
                    out string rootResolutionError,
                    out string rootNotFoundHint);
                if (rootCandidates.Count > 0)
                {
                    return CreateObjectPathAmbiguityError(
                        objectPath, rootCandidates, rootResolutionError);
                }
                if (!resolvedRoot)
                {
                    return CreatePathContextMissError(
                        objectPath,
                        prefabRoot,
                        rootResolutionError ?? rootNotFoundHint);
                }

                resolved = GameObjectPathUtils.TryResolveFromRoot(
                    prefabRoot,
                    objectPath,
                    out gameObject,
                    out candidates,
                    out resolutionError,
                    out _);
                if (candidates.Count > 0)
                    return CreateObjectPathAmbiguityError(objectPath, candidates, resolutionError);
                if (!resolved && !string.IsNullOrEmpty(resolutionError))
                {
                    return CreatePathContextMissError(
                        objectPath, prefabRoot, resolutionError);
                }
                return null;
            }

            resolved = GameObjectPathUtils.TryResolveInLoadedScenes(
                objectPath,
                out gameObject,
                out candidates,
                out resolutionError,
                out _);
            if (candidates.Count > 0)
                return CreateObjectPathAmbiguityError(objectPath, candidates, resolutionError);
            if (!resolved && !string.IsNullOrEmpty(resolutionError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    resolutionError,
                    "not_found_error");
            }
            return null;
        }

        public static JObject TryResolveGameObjectByName(
            string objectName,
            out GameObject gameObject)
        {
            gameObject = null;
            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;

            if (string.IsNullOrEmpty(objectName))
                return null;

            if (prefabRoot == null)
            {
                IReadOnlyList<GameObjectPathUtils.Candidate> legacyNameCandidates =
                    GameObjectPathUtils.FindAllByNameInLoadedScenes(objectName);
                if (legacyNameCandidates.Count > 0)
                    gameObject = legacyNameCandidates[0].GameObject;
                return null;
            }

            IReadOnlyList<GameObjectPathUtils.Candidate> prefabNameCandidates =
                GameObjectPathUtils.FindAllByNameFromRoot(prefabRoot, objectName);
            if (prefabNameCandidates.Count > 0)
            {
                gameObject = prefabNameCandidates[0].GameObject;
                return null;
            }

            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}' " +
                $"(root '{prefabRoot.name}'). GameObject name '{objectName}' does not exist " +
                "inside the Prefab contents.",
                ContextMissErrorType);
        }

        /// <summary>
        /// Resolve a serialized-reference value by instance ID. Persistent assets are allowed;
        /// scene objects must belong to the active Prefab preview scene.
        /// </summary>
        public static JObject TryResolveObjectByInstanceId(
            int instanceId,
            out UnityEngine.Object resolvedObject)
        {
            resolvedObject = null;
            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;

            resolvedObject = EditorUtility.InstanceIDToObject(instanceId);
            if (prefabRoot == null || resolvedObject == null)
                return null;

            GameObject sceneObject = GetOwningGameObject(resolvedObject);
            if (sceneObject == null || !sceneObject.scene.IsValid()
                || sceneObject.scene == prefabRoot.scene)
            {
                return null;
            }

            string scenePath = string.IsNullOrEmpty(sceneObject.scene.path)
                ? sceneObject.scene.name
                : sceneObject.scene.path;
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}' " +
                $"(root '{prefabRoot.name}'). Instance ID {instanceId} resolves to " +
                $"GameObject '{sceneObject.name}' in scene '{scenePath}', outside the Prefab contents.",
                ContextMissErrorType);
        }

        /// <summary>
        /// Reject a Prefab-preview object when the serialized write owner is outside that
        /// preview scene. A null owner is treated as unknown and therefore fails closed.
        /// </summary>
        public static JObject ValidateReferenceAssignment(
            UnityEngine.Object referenceOwner,
            UnityEngine.Object referenceValue)
        {
            GameObject referencedGameObject = GetOwningGameObject(referenceValue);
            if (referencedGameObject == null)
                return null;

            JObject sessionError = TryGetPrefabRoot(out GameObject prefabRoot);
            if (sessionError != null)
                return sessionError;
            if (prefabRoot == null || referencedGameObject.scene != prefabRoot.scene)
                return null;

            GameObject ownerGameObject = GetOwningGameObject(referenceOwner);
            if (ownerGameObject != null && ownerGameObject.scene == prefabRoot.scene)
                return null;

            string ownerDescription = referenceOwner == null
                ? "an unknown write target"
                : $"'{referenceOwner.name}' ({referenceOwner.GetType().Name})";
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}'. " +
                $"Cannot assign preview object '{referencedGameObject.name}' to {ownerDescription} " +
                "outside the active Prefab contents; the reference would serialize as a missing " +
                "cross-context reference.",
                ContextMissErrorType);
        }

        public static JObject CreatePathContextMissError(
            string objectPath,
            GameObject prefabRoot,
            string resolutionError = null)
        {
            string detail = string.IsNullOrEmpty(resolutionError)
                ? string.Empty
                : " " + resolutionError;
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}' " +
                $"(root '{prefabRoot.name}'). Object path '{objectPath}' does not exist " +
                $"inside the Prefab contents.{detail}",
                ContextMissErrorType);
        }

        public static JObject CreateSessionLostError()
        {
            string assetPath = PrefabEditingService.LostAssetPath;
            return McpUnitySocketHandler.CreateErrorResponse(
                $"The Prefab editing session for '{assetPath}' was lost because its persisted " +
                "preview root can no longer be resolved or validated. " +
                PrefabEditingService.GetLostPreviewCleanupDescription() + " " +
                "The recovery record may represent unsaved edits that could not be saved. " +
                "Prefab-context operations are blocked to avoid losing or " +
                "misapplying those edits. Call save_prefab_contents with discard=true to acknowledge " +
                "the lost session and clear its record.",
                SessionLostErrorType);
        }

        internal static bool IsLoadedNonPreviewScene(Scene scene)
        {
            return scene.IsValid()
                && scene.isLoaded
                && !EditorSceneManager.IsPreviewScene(scene);
        }

        internal static bool IsLoadedNonPreviewSceneObject(GameObject gameObject)
        {
            return gameObject != null && IsLoadedNonPreviewScene(gameObject.scene);
        }

        internal static bool TryGetLoadedNonPreviewScene(out Scene scene)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (IsLoadedNonPreviewScene(activeScene))
            {
                scene = activeScene;
                return true;
            }

            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene candidate = SceneManager.GetSceneAt(sceneIndex);
                if (IsLoadedNonPreviewScene(candidate))
                {
                    scene = candidate;
                    return true;
                }
            }

            scene = default;
            return false;
        }

        private static JObject TrySelectUniqueNameCandidate(
            string reference,
            IReadOnlyList<GameObjectPathUtils.Candidate> candidates,
            out GameObject gameObject)
        {
            gameObject = null;
            if (candidates.Count == 0)
                return null;
            if (candidates.Count > 1)
            {
                var candidateSummary = new List<string>(candidates.Count);
                foreach (GameObjectPathUtils.Candidate candidate in candidates)
                {
                    candidateSummary.Add(
                        $"'{candidate.Path}' (instanceId={candidate.InstanceId}, " +
                        $"scene='{candidate.SceneName}')");
                }
                string message =
                    $"GameObject name '{reference}' is ambiguous: {candidates.Count} candidates " +
                    $"were found: {string.Join(", ", candidateSummary)}. " +
                    "Use instanceId or a canonical hierarchy path.";
                return CreateObjectPathAmbiguityError(reference, candidates, message);
            }

            gameObject = candidates[0].GameObject;
            return null;
        }

        private static JObject TryResolvePathInScenes(
            string path,
            out GameObject gameObject)
        {
            bool resolved = GameObjectPathUtils.TryResolveInLoadedScenes(
                path,
                out gameObject,
                out IReadOnlyList<GameObjectPathUtils.Candidate> candidates,
                out string resolutionError,
                out string notFoundHint);
            if (candidates.Count > 0)
                return CreateObjectPathAmbiguityError(path, candidates, resolutionError);
            string notFoundMessage = resolutionError ?? notFoundHint;
            if (!resolved && !string.IsNullOrEmpty(notFoundMessage))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    notFoundMessage,
                    "not_found_error");
            }

            return null;
        }

        internal static JObject CreateObjectPathAmbiguityError(
            string objectPath,
            IReadOnlyList<GameObjectPathUtils.Candidate> candidates,
            string message)
        {
            JObject response = McpUnitySocketHandler.CreateErrorResponse(
                message,
                ObjectPathAmbiguityErrorType);
            ((JObject)response["error"])["details"] = new JObject
            {
                ["objectPath"] = objectPath,
                ["candidateCount"] = candidates.Count,
                ["candidates"] = CreateCandidateDetails(candidates)
            };
            return response;
        }

        internal static JObject CreateRootPathNotFoundError(
            string objectPath,
            string literalRootName,
            IReadOnlyList<GameObjectPathUtils.Candidate> nestedNameCandidates)
        {
            JObject response = McpUnitySocketHandler.CreateErrorResponse(
                $"Root-qualified path '{objectPath}' does not exist: no loaded-scene root is " +
                $"named '{literalRootName}'. {nestedNameCandidates.Count} nested GameObject(s) " +
                "have that name; use one of their canonical paths instead of creating a new root.",
                "not_found_error");
            ((JObject)response["error"])["details"] = new JObject
            {
                ["objectPath"] = objectPath,
                ["candidateCount"] = nestedNameCandidates.Count,
                ["candidates"] = CreateCandidateDetails(nestedNameCandidates)
            };
            return response;
        }

        private static JArray CreateCandidateDetails(
            IReadOnlyList<GameObjectPathUtils.Candidate> candidates)
        {
            var candidateDetails = new JArray();
            foreach (GameObjectPathUtils.Candidate candidate in candidates)
            {
                candidateDetails.Add(new JObject
                {
                    ["instanceId"] = candidate.InstanceId,
                    ["path"] = candidate.Path,
                    ["scene"] = candidate.SceneName
                });
            }
            return candidateDetails;
        }

        private static JObject ValidateOperationTarget(
            int instanceId,
            GameObject gameObject,
            GameObject prefabRoot)
        {
            if (gameObject == null)
                return null;

            if (!gameObject.scene.IsValid())
            {
                string message = $"Instance ID {instanceId} resolves to persistent GameObject asset " +
                    $"'{gameObject.name}', which cannot be used as an operation target.";
                return McpUnitySocketHandler.CreateErrorResponse(
                    message,
                    prefabRoot != null ? ContextMissErrorType : "validation_error");
            }

            if (prefabRoot == null || gameObject.scene == prefabRoot.scene)
                return null;

            string scenePath = string.IsNullOrEmpty(gameObject.scene.path)
                ? gameObject.scene.name
                : gameObject.scene.path;
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}' " +
                $"(root '{prefabRoot.name}'). Instance ID {instanceId} resolves to " +
                $"GameObject '{gameObject.name}' in scene '{scenePath}', outside the Prefab contents.",
                ContextMissErrorType);
        }

        private static GameObject GetOwningGameObject(UnityEngine.Object obj)
        {
            if (obj is GameObject gameObject)
                return gameObject;
            if (obj is Component component)
                return component.gameObject;
            return null;
        }
    }
}
