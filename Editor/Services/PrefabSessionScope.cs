using McpUnity.Unity;
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

            if (string.IsNullOrEmpty(objectPath))
                return null;

            if (prefabRoot != null)
            {
                gameObject = FindByPath(prefabRoot, objectPath);
                return gameObject != null
                    ? null
                    : CreatePathContextMissError(objectPath, prefabRoot);
            }

            gameObject = FindByPathInScenes(objectPath);
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
                gameObject = FindByPathInScenes(objectName);
                return null;
            }

            gameObject = FindByName(prefabRoot, objectName);
            if (gameObject != null)
                return null;

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

        public static JObject CreatePathContextMissError(string objectPath, GameObject prefabRoot)
        {
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Prefab editing session is scoped to '{PrefabEditingService.AssetPath}' " +
                $"(root '{prefabRoot.name}'). Object path '{objectPath}' does not exist " +
                "inside the Prefab contents.",
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

        private static GameObject FindByPath(GameObject prefabRoot, string path)
        {
            if (prefabRoot == null || string.IsNullOrEmpty(path))
                return null;

            string trimmedPath = path.Trim('/');
            if (string.IsNullOrEmpty(trimmedPath))
                return null;

            string[] parts = trimmedPath.Split('/');
            if (parts.Length == 0 || parts[0] != prefabRoot.name)
                return null;

            GameObject current = prefabRoot;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.transform.Find(parts[i]);
                if (child == null)
                    return null;
                current = child.gameObject;
            }
            return current;
        }

        private static GameObject FindByName(GameObject current, string objectName)
        {
            if (current.name == objectName)
                return current;

            foreach (Transform child in current.transform)
            {
                GameObject found = FindByName(child.gameObject, objectName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static GameObject FindByPathInScenes(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            GameObject found = GameObject.Find(path);
            if (IsLoadedNonPreviewSceneObject(found))
                return found;

            string trimmedPath = path.Trim('/');
            if (string.IsNullOrEmpty(trimmedPath))
                return null;

            string[] parts = trimmedPath.Split('/');
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!IsLoadedNonPreviewScene(scene))
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name != parts[0])
                        continue;

                    GameObject current = root;
                    bool foundFullPath = true;
                    for (int i = 1; i < parts.Length; i++)
                    {
                        Transform child = current.transform.Find(parts[i]);
                        if (child == null)
                        {
                            foundFullPath = false;
                            break;
                        }
                        current = child.gameObject;
                    }

                    if (foundFullPath)
                        return current;
                }
            }

            return null;
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
