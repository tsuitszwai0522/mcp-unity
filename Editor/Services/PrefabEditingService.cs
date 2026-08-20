using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace McpUnity.Services
{
    internal sealed class PrefabEditingCleanupException : InvalidOperationException
    {
        public PrefabEditingSessionStatus SessionStatus { get; }

        public PrefabEditingCleanupException(
            string message,
            PrefabEditingSessionStatus sessionStatus,
            Exception innerException)
            : base(message, innerException)
        {
            SessionStatus = sessionStatus;
        }
    }

    public enum PrefabEditingSessionStatus
    {
        None,
        Active,
        Lost
    }

    /// <summary>
    /// Static service for managing Prefab Edit Mode.
    /// Uses PrefabUtility.LoadPrefabContents() to load a Prefab into an isolated environment
    /// for structural editing, then saves it back to the .prefab asset.
    /// Only one Prefab can be edited at a time.
    /// </summary>
    public static class PrefabEditingService
    {
        private const string SessionAssetPathKey = "McpUnity.PrefabEditingService.AssetPath";
        private const string SessionAssetGuidKey = "McpUnity.PrefabEditingService.AssetGuid";
        private const string SessionRootInstanceIdKey = "McpUnity.PrefabEditingService.RootInstanceId";

        private static GameObject _prefabRoot;
        private static string _assetPath;
        private static string _assetGuid;
        private static bool _sessionLost;
        private static string _lostAssetPath;
        private static GameObject _lostPrefabRoot;
        private static bool _lostPreviewWasUnloaded;
        private static Func<GameObject, string, bool> _savePrefabContents =
            (root, path) =>
            {
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                return success;
            };
        private static Action<GameObject> _unloadPrefabContents = PrefabUtility.UnloadPrefabContents;

        /// <summary>
        /// Whether a Prefab is currently being edited
        /// </summary>
        public static bool IsEditing => Status == PrefabEditingSessionStatus.Active;

        /// <summary>
        /// Current Prefab editing session state. Accessing this property attempts to restore
        /// an active session whose managed state was cleared by a domain reload.
        /// </summary>
        public static PrefabEditingSessionStatus Status
        {
            get
            {
                EnsureRehydrated();
                if (IsManagedRootValid())
                    return PrefabEditingSessionStatus.Active;
                return _sessionLost
                    ? PrefabEditingSessionStatus.Lost
                    : PrefabEditingSessionStatus.None;
            }
        }

        /// <summary>
        /// The root GameObject of the currently loaded Prefab contents
        /// </summary>
        public static GameObject PrefabRoot
        {
            get
            {
                EnsureRehydrated();
                return _prefabRoot;
            }
        }

        /// <summary>
        /// The asset path of the currently loaded Prefab
        /// </summary>
        public static string AssetPath
        {
            get
            {
                EnsureRehydrated();
                return _assetPath;
            }
        }

        /// <summary>
        /// Asset path recorded for a session that could not be restored.
        /// </summary>
        public static string LostAssetPath
        {
            get
            {
                EnsureRehydrated();
                return _lostAssetPath;
            }
        }

        /// <summary>
        /// Load a Prefab's contents into an isolated editing environment
        /// </summary>
        /// <param name="assetPath">Asset path to the .prefab file</param>
        /// <returns>The root GameObject of the loaded Prefab contents</returns>
        public static GameObject Open(string assetPath)
        {
            PrefabEditingSessionStatus status = Status;
            if (status == PrefabEditingSessionStatus.Active)
            {
                throw new InvalidOperationException(
                    $"A Prefab is already being edited: '{_assetPath}'. " +
                    "Call Save() or Discard() before opening another Prefab.");
            }

            if (status == PrefabEditingSessionStatus.Lost)
            {
                throw new InvalidOperationException(
                    $"The Prefab editing session for '{_lostAssetPath}' was lost and may contain " +
                    "unsaved edits. Call Discard() to acknowledge and clear the lost session before " +
                    "opening another Prefab.");
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));
            }

            GameObject loadedRoot = PrefabUtility.LoadPrefabContents(assetPath);
            if (loadedRoot == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not return a Prefab contents root for '{assetPath}'.");
            }

            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            string canonicalAssetPath = string.IsNullOrEmpty(assetGuid)
                ? assetPath
                : AssetDatabase.GUIDToAssetPath(assetGuid);

            _assetPath = string.IsNullOrEmpty(canonicalAssetPath)
                ? assetPath
                : canonicalAssetPath;
            _assetGuid = assetGuid;
            _prefabRoot = loadedRoot;
            _sessionLost = false;
            _lostAssetPath = null;
            _lostPrefabRoot = null;
            _lostPreviewWasUnloaded = false;
            SessionState.SetString(SessionAssetPathKey, _assetPath);
            SessionState.SetString(SessionAssetGuidKey, _assetGuid ?? string.Empty);
            SessionState.SetInt(SessionRootInstanceIdKey, loadedRoot.GetInstanceID());
            return _prefabRoot;
        }

        /// <summary>
        /// Save the current Prefab edits and unload the contents
        /// </summary>
        /// <remarks>
        /// The success=false branch is defensive: observed Unity 2022.3 failures throw while
        /// other probed inputs return true, so only an injected save delegate can cover it.
        /// </remarks>
        public static void Save()
        {
            if (!IsEditing)
            {
                throw new InvalidOperationException("No Prefab is currently being edited.");
            }

            RefreshAssetPathFromGuid();

            bool saveSuccess;
            try
            {
                saveSuccess = _savePrefabContents(_prefabRoot, _assetPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to save Prefab '{_assetPath}'. The editing session and unsaved edits remain open; " +
                    $"fix the problem and retry. {ex.Message}", ex);
            }

            if (!saveSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to save Prefab '{_assetPath}'. The editing session and unsaved edits remain open; " +
                    "fix the problem and retry.");
            }

            string savedAssetPath = _assetPath;
            try
            {
                _unloadPrefabContents(_prefabRoot);
            }
            catch (Exception ex)
            {
                PrefabEditingSessionStatus cleanupStatus = Status;
                string recoveryGuidance;
                if (cleanupStatus == PrefabEditingSessionStatus.Active)
                {
                    recoveryGuidance =
                        "The session is still Active. Call save_prefab_contents again to save " +
                        "any changes made after this cleanup error and retry the unload. Use " +
                        "discard=true only if you intentionally want to abandon those later changes.";
                }
                else if (cleanupStatus == PrefabEditingSessionStatus.Lost)
                {
                    recoveryGuidance =
                        "The session is Lost. Call save_prefab_contents with discard=true to " +
                        "acknowledge the lost session and clear its recovery record before continuing.";
                }
                else
                {
                    recoveryGuidance =
                        "The session is no longer active. Inspect the current session state before " +
                        "attempting another Prefab operation.";
                }

                throw new PrefabEditingCleanupException(
                    $"Prefab '{savedAssetPath}' was saved successfully, but Unity could not unload its " +
                    $"Prefab contents. The session record was preserved. {recoveryGuidance} " +
                    ex.Message,
                    cleanupStatus,
                    ex);
            }
            ClearActiveSession();
        }

        /// <summary>
        /// Discard changes and unload the Prefab contents
        /// </summary>
        public static void Discard()
        {
            DiscardWithCleanupResult();
        }

        internal static bool DiscardWithCleanupResult()
        {
            PrefabEditingSessionStatus status = Status;
            if (status == PrefabEditingSessionStatus.Lost)
            {
                bool previewWasUnloaded = _lostPreviewWasUnloaded;
                // Once cleanup succeeded, the persisted instance ID is stale. Unity can reuse
                // it for an unrelated object, so never resolve it a second time while
                // acknowledging the already-cleaned lost session.
                GameObject livePreviewRoot = previewWasUnloaded
                    ? null
                    : GetLivePreviewRoot(_lostPrefabRoot) ?? ResolvePersistedPreviewRoot();
                if (livePreviewRoot != null)
                {
                    try
                    {
                        _unloadPrefabContents(livePreviewRoot);
                        previewWasUnloaded = true;
                    }
                    catch (Exception ex)
                    {
                        _lostPrefabRoot = livePreviewRoot;
                        throw new InvalidOperationException(
                            "Unity could not unload the live Prefab preview while acknowledging " +
                            "the lost session. The recovery record was preserved; retry " +
                            $"discard=true. {ex.Message}",
                            ex);
                    }
                }

                ClearActiveSession();
                return previewWasUnloaded;
            }

            if (status != PrefabEditingSessionStatus.Active)
            {
                throw new InvalidOperationException("No Prefab is currently being edited.");
            }

            _unloadPrefabContents(_prefabRoot);
            ClearActiveSession();
            return true;
        }

        /// <summary>
        /// Find a GameObject within the loaded Prefab by a root-qualified hierarchy path.
        /// Supports paths like "PrefabRoot/Child/SubChild".
        /// </summary>
        /// <param name="path">Hierarchy path to search for</param>
        /// <returns>The found GameObject, or null if not found</returns>
        public static GameObject FindByPath(string path)
        {
            if (!IsEditing)
                return null;

            var error = PrefabSessionScope.TryResolveGameObject(
                null, path, out GameObject gameObject);
            return error == null ? gameObject : null;
        }

        private static void EnsureRehydrated()
        {
            string storedAssetPath = SessionState.GetString(SessionAssetPathKey, string.Empty);
            string storedAssetGuid = SessionState.GetString(SessionAssetGuidKey, string.Empty);
            int storedRootInstanceId = SessionState.GetInt(SessionRootInstanceIdKey, 0);

            bool hasPersistedSession = !string.IsNullOrEmpty(storedAssetPath)
                || !string.IsNullOrEmpty(storedAssetGuid)
                || storedRootInstanceId != 0;
            if (!hasPersistedSession)
            {
                if (_prefabRoot == null)
                {
                    _assetPath = null;
                    _assetGuid = null;
                    _sessionLost = false;
                    _lostAssetPath = null;
                    _lostPrefabRoot = null;
                    _lostPreviewWasUnloaded = false;
                }
                return;
            }

            // Lost is terminal until the caller explicitly acknowledges it. In particular,
            // preserve whether a mismatched live preview was already unloaded so the discard
            // response can report the cleanup accurately.
            if (_sessionLost)
                return;

            string canonicalAssetPath = ResolveCanonicalAssetPath(storedAssetGuid, storedAssetPath);

            if (IsManagedRootValid()
                && _prefabRoot.GetInstanceID() == storedRootInstanceId
                && RootMatchesPersistedAsset(
                    _prefabRoot, storedAssetGuid, storedAssetPath, canonicalAssetPath))
            {
                _assetPath = canonicalAssetPath;
                _assetGuid = storedAssetGuid;
                _sessionLost = false;
                _lostAssetPath = null;
                _lostPrefabRoot = null;
                _lostPreviewWasUnloaded = false;
                return;
            }

            UnityEngine.Object restoredObject = storedRootInstanceId != 0
                ? EditorUtility.InstanceIDToObject(storedRootInstanceId)
                : null;
            GameObject restoredRoot = restoredObject as GameObject;

            if (restoredObject != null
                && restoredRoot != null
                && restoredRoot.scene.IsValid()
                && RootMatchesPersistedAsset(
                    restoredRoot, storedAssetGuid, storedAssetPath, canonicalAssetPath))
            {
                _prefabRoot = restoredRoot;
                _assetPath = canonicalAssetPath;
                _assetGuid = storedAssetGuid;
                _sessionLost = false;
                _lostAssetPath = null;
                _lostPrefabRoot = null;
                _lostPreviewWasUnloaded = false;
                return;
            }

            CleanupMismatchedPreviewBeforeLost(restoredRoot);
            _prefabRoot = null;
            _assetPath = null;
            _assetGuid = null;
            _sessionLost = true;
            _lostAssetPath = string.IsNullOrEmpty(canonicalAssetPath)
                ? "<unknown Prefab asset>"
                : canonicalAssetPath;
        }

        private static void ClearActiveSession()
        {
            _prefabRoot = null;
            _assetPath = null;
            _assetGuid = null;
            _sessionLost = false;
            _lostAssetPath = null;
            _lostPrefabRoot = null;
            _lostPreviewWasUnloaded = false;
            ClearPersistedSession();
        }

        internal static string GetLostPreviewCleanupDescription()
        {
            EnsureRehydrated();
            if (_lostPreviewWasUnloaded)
            {
                return "A live preview root was unloaded before the session entered the lost " +
                    "state; any unsaved preview edits were discarded.";
            }

            if (GetLivePreviewRoot(_lostPrefabRoot) != null)
            {
                return "A live preview root is still loaded because Unity could not unload it; " +
                    "discard=true will retry cleanup before clearing the recovery record.";
            }

            return "No live preview root remained available to unload.";
        }

        private static bool IsManagedRootValid()
        {
            return _prefabRoot != null && _prefabRoot.scene.IsValid();
        }

        private static void CleanupMismatchedPreviewBeforeLost(GameObject restoredRoot)
        {
            _lostPrefabRoot = GetLivePreviewRoot(restoredRoot)
                ?? GetLivePreviewRoot(_prefabRoot);
            _lostPreviewWasUnloaded = false;
            if (_lostPrefabRoot == null)
                return;

            try
            {
                _unloadPrefabContents(_lostPrefabRoot);
                _lostPrefabRoot = null;
                _lostPreviewWasUnloaded = true;
            }
            catch
            {
                // Preserve the live root and recovery record. Lost-session acknowledgement
                // retries the unload and refuses to clear the record if it still fails.
            }
        }

        private static GameObject ResolvePersistedPreviewRoot()
        {
            int rootInstanceId = SessionState.GetInt(SessionRootInstanceIdKey, 0);
            return rootInstanceId == 0
                ? null
                : GetLivePreviewRoot(EditorUtility.InstanceIDToObject(rootInstanceId) as GameObject);
        }

        private static GameObject GetLivePreviewRoot(GameObject candidate)
        {
            // A corrupted recovery record can point at a normal scene object. Never pass that
            // object to UnloadPrefabContents. A LoadPrefabContents result is a root in a preview
            // scene but is not owned by a user-facing Prefab Stage; those observable invariants
            // exclude children and Stage roots before relying on Unity's cleanup API.
            return candidate != null
                && candidate.scene.IsValid()
                && EditorSceneManager.IsPreviewSceneObject(candidate)
                && candidate.transform.parent == null
                && PrefabStageUtility.GetPrefabStage(candidate) == null
                    ? candidate
                    : null;
        }

        private static string ResolveCanonicalAssetPath(string assetGuid, string fallbackPath)
        {
            if (!string.IsNullOrEmpty(assetGuid))
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (!string.IsNullOrEmpty(guidPath))
                    return guidPath;
            }

            return fallbackPath;
        }

        private static bool RootMatchesPersistedAsset(
            GameObject root,
            string storedAssetGuid,
            string storedAssetPath,
            string canonicalAssetPath)
        {
            if (root == null || !root.scene.IsValid())
                return false;

            if (!string.IsNullOrEmpty(storedAssetGuid))
            {
                string sceneGuid = AssetDatabase.AssetPathToGUID(root.scene.path);
                if (!string.IsNullOrEmpty(sceneGuid))
                {
                    return string.Equals(sceneGuid, storedAssetGuid, StringComparison.Ordinal);
                }

                // Unity may retain the original preview-scene path after the asset moves.
                // The persisted instance ID has already matched at the call site, so the
                // original or canonical path remains a safe identity hint while save uses the
                // GUID path. Depending on reload timing, Unity can expose either path here.
                return string.Equals(root.scene.path, storedAssetPath, StringComparison.Ordinal)
                    || string.Equals(root.scene.path, canonicalAssetPath, StringComparison.Ordinal);
            }

            if (string.IsNullOrEmpty(canonicalAssetPath))
                return false;

            return string.Equals(
                root.scene.path,
                canonicalAssetPath,
                StringComparison.Ordinal);
        }

        private static void RefreshAssetPathFromGuid()
        {
            if (string.IsNullOrEmpty(_assetGuid))
                return;

            string canonicalAssetPath = AssetDatabase.GUIDToAssetPath(_assetGuid);
            if (string.IsNullOrEmpty(canonicalAssetPath))
            {
                throw new InvalidOperationException(
                    $"Failed to save Prefab '{_assetPath}' because its asset GUID '{_assetGuid}' " +
                    "no longer resolves to an asset path. The editing session and unsaved edits " +
                    "remain open; restore the asset and retry or discard the session.");
            }

            // Use the moved asset path for saving, but keep the persisted path as the preview
            // scene's identity hint until unload succeeds and the session record is cleared.
            _assetPath = canonicalAssetPath;
        }

        private static void ClearPersistedSession()
        {
            SessionState.EraseString(SessionAssetPathKey);
            SessionState.EraseString(SessionAssetGuidKey);
            SessionState.EraseInt(SessionRootInstanceIdKey);
        }

    }
}
