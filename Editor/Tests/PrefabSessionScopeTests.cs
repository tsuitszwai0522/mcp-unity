using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    public class PrefabSessionReferenceHolder : ScriptableObject
    {
        public GameObject target;
    }

    public class PrefabSessionReferenceBehaviour : MonoBehaviour
    {
        public GameObject target;
        public GameObject TargetProperty { get; set; }
    }

    public class PrefabSessionScopeTests
    {
        private const string TestDirectory = "Assets/McpUnityPrefabSessionScopeTests";
        private const string PrefabRootName = "S6ScopedRoot";
        private const string TestPrefabPath = TestDirectory + "/" + PrefabRootName + ".prefab";
        private const string MovedPrefabPath = TestDirectory + "/S6MovedScopedRoot.prefab";
        private const string AdditiveTestScenePath = TestDirectory + "/S6AdditiveScopeTest.unity";
        private const string SessionAssetPathKey = "McpUnity.PrefabEditingService.AssetPath";
        private const string SessionAssetGuidKey = "McpUnity.PrefabEditingService.AssetGuid";
        private const string SessionRootInstanceIdKey = "McpUnity.PrefabEditingService.RootInstanceId";
        private static readonly System.Func<GameObject, string, bool> OriginalSavePrefabContents =
            (System.Func<GameObject, string, bool>)GetServiceField("_savePrefabContents");
        private static readonly System.Action<GameObject> OriginalUnloadPrefabContents =
            (System.Action<GameObject>)GetServiceField("_unloadPrefabContents");
        private static readonly System.Action<UnityEngine.Object> OriginalPingObject =
            (System.Action<UnityEngine.Object>)GetAddAssetToolField("_pingObject");

        private GameObject _sceneObject;
        private Scene _additiveTestScene;
        private bool _ownsFixtureState;
        private readonly HashSet<int> _ownedPreviewSceneHandles = new HashSet<int>();
        private System.Action _discardSession;

        private sealed class OrphanPreviewSceneSnapshot
        {
            public Scene Scene;
            public string ScenePath;
            public readonly List<string> CameraNames = new List<string>();
            public readonly List<Camera> Cameras = new List<Camera>();
        }

        [SetUp]
        public void SetUp()
        {
            _ownsFixtureState = false;
            PrefabEditingSessionStatus existingStatus = PrefabEditingService.Status;
            if (existingStatus != PrefabEditingSessionStatus.None)
            {
                Assert.Ignore(
                    $"PrefabSessionScopeTests will not erase an existing {existingStatus} Prefab " +
                    $"session for '{PrefabEditingService.AssetPath ?? PrefabEditingService.LostAssetPath}'. " +
                    "Save/discard that session before running this fixture.");
            }

            _discardSession = PrefabEditingService.Discard;
            _ownedPreviewSceneHandles.Clear();
            _ownsFixtureState = true;
            ResetSessionState();
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "McpUnityPrefabSessionScopeTests");

            GameObject sourceRoot = new GameObject(PrefabRootName);
            try
            {
                GameObject inside = new GameObject("Inside");
                inside.transform.SetParent(sourceRoot.transform, false);
                PrefabUtility.SaveAsPrefabAsset(sourceRoot, TestPrefabPath, out bool success);
                Assert.IsTrue(success, "Test Prefab setup must succeed");
            }
            finally
            {
                Object.DestroyImmediate(sourceRoot);
            }

            _sceneObject = new GameObject("S6SceneOnly");
        }

        [TearDown]
        public void TearDown()
        {
            if (!_ownsFixtureState)
                return;

            System.Exception cleanupFailure = null;
            string activeSessionCameraLeak = null;
            string orphanLeakFailure = null;
            try
            {
                TryCleanup(RestoreUnloadPrefabContents, ref cleanupFailure);
                TryCleanup(RestoreSavePrefabContents, ref cleanupFailure);
                TryCleanup(RestoreAddAssetPingObject, ref cleanupFailure);
                RememberActivePrefabSessionScene();
                activeSessionCameraLeak = FindActiveSessionGameCameraLeak();
                PrefabEditingSessionStatus status = PrefabEditingService.Status;
                if (status != PrefabEditingSessionStatus.None)
                    TryCleanup(_discardSession, ref cleanupFailure);
                if (_additiveTestScene.IsValid() && _additiveTestScene.isLoaded)
                {
                    TryCleanup(
                        () => EditorSceneManager.CloseScene(_additiveTestScene, true),
                        ref cleanupFailure);
                }
                _additiveTestScene = default;
            }
            finally
            {
                if (_sceneObject != null)
                {
                    TryCleanup(
                        () => Object.DestroyImmediate(_sceneObject),
                        ref cleanupFailure);
                }

                try
                {
                    orphanLeakFailure = CloseNewOrphanPreviewScenesWithGameCameras();
                }
                catch (System.Exception ex)
                {
                    if (cleanupFailure == null)
                        cleanupFailure = ex;
                }

                // Never erase the recovery record if cleanup itself failed. The next test run
                // will skip with an actionable message instead of destroying a live session.
                if (PrefabEditingService.Status == PrefabEditingSessionStatus.None)
                {
                    TryCleanup(ResetSessionState, ref cleanupFailure);
                    TryCleanup(
                        () => AssetDatabase.DeleteAsset(AdditiveTestScenePath),
                        ref cleanupFailure);
                    if (AssetDatabase.IsValidFolder(TestDirectory))
                    {
                        TryCleanup(
                            () => AssetDatabase.DeleteAsset(TestDirectory),
                            ref cleanupFailure);
                    }
                    TryCleanup(() => AssetDatabase.Refresh(), ref cleanupFailure);
                }

                _ownsFixtureState = false;
                _discardSession = null;
                _ownedPreviewSceneHandles.Clear();
            }

            string leakFailure = CombineFailures(
                activeSessionCameraLeak, orphanLeakFailure);
            if (cleanupFailure != null)
            {
                if (!string.IsNullOrEmpty(leakFailure))
                    cleanupFailure.Data["PrefabSessionScopeTests.CameraLeak"] = leakFailure;
                throw cleanupFailure;
            }
            if (!string.IsNullOrEmpty(leakFailure))
                Assert.Fail(leakFailure);
        }

        [Test]
        public void ObjectPathMissDuringSession_ReturnsContextErrorWithoutTouchingScene()
        {
            PrefabEditingService.Open(TestPrefabPath);
            var tool = new UpdateGameObjectTool();

            JObject result = tool.Execute(new JObject
            {
                ["objectPath"] = _sceneObject.name,
                ["gameObjectData"] = new JObject { ["name"] = "S6UnexpectedRename" }
            });

            Assert.AreEqual(
                "prefab_context_miss_error",
                result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(TestPrefabPath));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(PrefabRootName));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(_sceneObject.name));
            Assert.AreEqual("S6SceneOnly", _sceneObject.name);
            Assert.IsNull(GameObject.Find("S6UnexpectedRename"));
        }

        [Test]
        public void SceneInstanceIdResolution_AllowsNoSessionButRejectsCrossContext()
        {
            var tool = new UpdateGameObjectTool();

            JObject withoutSession = tool.Execute(new JObject
            {
                ["instanceId"] = _sceneObject.GetInstanceID(),
                ["gameObjectData"] = new JObject { ["activeSelf"] = false }
            });

            Assert.IsTrue(withoutSession["success"]?.ToObject<bool>() ?? false);
            Assert.IsFalse(_sceneObject.activeSelf);
            _sceneObject.SetActive(true);
            PrefabEditingService.Open(TestPrefabPath);

            JObject duringSession = tool.Execute(new JObject
            {
                ["instanceId"] = _sceneObject.GetInstanceID(),
                ["gameObjectData"] = new JObject { ["name"] = "S6UnexpectedRename" }
            });

            Assert.AreEqual(
                "prefab_context_miss_error",
                duringSession["error"]?["type"]?.ToString());
            Assert.AreEqual("S6SceneOnly", _sceneObject.name);
        }

        [Test]
        public void ManagedStateLoss_RehydratesExistingPrefabContentsAndUnsavedObjects()
        {
            GameObject originalRoot = PrefabEditingService.Open(TestPrefabPath);
            GameObject marker = new GameObject("UnsavedReloadMarker");
            marker.transform.SetParent(originalRoot.transform, false);
            int originalRootId = originalRoot.GetInstanceID();

            SimulateManagedDomainReload();

            Assert.IsTrue(PrefabEditingService.IsEditing);
            Assert.AreEqual(originalRootId, PrefabEditingService.PrefabRoot.GetInstanceID());
            Assert.IsNotNull(PrefabEditingService.PrefabRoot.transform.Find("UnsavedReloadMarker"));
            Assert.AreEqual(TestPrefabPath, PrefabEditingService.AssetPath);
        }

        [Test]
        public void InjectedDelegateDefaults_AreCapturedFromUnityEditorApis()
        {
            MethodInfo expectedUnload = typeof(PrefabUtility).GetMethod(
                nameof(PrefabUtility.UnloadPrefabContents),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject) },
                null);
            MethodInfo expectedPing = typeof(EditorGUIUtility).GetMethod(
                nameof(EditorGUIUtility.PingObject),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(UnityEngine.Object) },
                null);

            Assert.AreEqual(expectedUnload, OriginalUnloadPrefabContents.Method);
            Assert.AreEqual(expectedPing, OriginalPingObject.Method);
        }

        [Test]
        public void InvalidPersistedSession_RemainsRecordedUntilDiscardAcknowledgesLostState()
        {
            SessionState.SetString(SessionAssetPathKey, TestPrefabPath);
            SessionState.SetInt(SessionRootInstanceIdKey, _sceneObject.GetInstanceID());
            SimulateManagedDomainReload();

            Assert.IsFalse(PrefabEditingService.IsEditing);
            JObject result = new SavePrefabContentsTool().Execute(new JObject());

            Assert.AreEqual(
                "prefab_session_lost_error",
                result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("unsaved edits"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(TestPrefabPath));
            Assert.AreEqual(TestPrefabPath, SessionState.GetString(SessionAssetPathKey, string.Empty));
            Assert.AreEqual(_sceneObject.GetInstanceID(), SessionState.GetInt(SessionRootInstanceIdKey, 0));

            SimulateManagedDomainReload();
            Assert.AreEqual(PrefabEditingSessionStatus.Lost, PrefabEditingService.Status);

            JObject acknowledged = new SavePrefabContentsTool().Execute(
                new JObject { ["discard"] = true });
            Assert.IsTrue(acknowledged["success"]?.ToObject<bool>() ?? false);
            Assert.IsTrue(acknowledged["lostSessionAcknowledged"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(PrefabEditingSessionStatus.None, PrefabEditingService.Status);
            Assert.AreEqual(string.Empty, SessionState.GetString(SessionAssetPathKey, string.Empty));
            Assert.AreEqual(0, SessionState.GetInt(SessionRootInstanceIdKey, 0));
        }

        [Test]
        public void LostSessionCleanup_RetriesFailureButNeverResolvesStaleIdAfterSuccess()
        {
            int previewCountBeforeOpen = EditorSceneManager.previewSceneCount;
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            Assert.AreEqual(previewCountBeforeOpen + 1, EditorSceneManager.previewSceneCount);
            int unloadAttempts = 0;
            SetServiceField(
                "_unloadPrefabContents",
                (System.Action<GameObject>)(previewRoot =>
                {
                    unloadAttempts++;
                    if (unloadAttempts == 1)
                    {
                        throw new System.InvalidOperationException(
                            "synthetic first cleanup failure");
                    }
                    PrefabUtility.UnloadPrefabContents(previewRoot);
                }));
            SessionState.SetString(
                SessionAssetPathKey,
                TestDirectory + "/MismatchedRecoveryRecord.prefab");
            SessionState.SetString(SessionAssetGuidKey, string.Empty);
            SimulateManagedDomainReload();

            JObject acknowledged;
            try
            {
                acknowledged = new SavePrefabContentsTool().Execute(
                    new JObject { ["discard"] = true });
            }
            finally
            {
                RestoreUnloadPrefabContents();
            }

            Assert.IsTrue(acknowledged["success"]?.ToObject<bool>() ?? false);
            Assert.IsTrue(acknowledged["lostSessionAcknowledged"]?.ToObject<bool>() ?? false);
            Assert.That(acknowledged["message"]?.ToString(), Does.Contain("was unloaded"));
            Assert.AreEqual(2, unloadAttempts);
            Assert.AreEqual(previewCountBeforeOpen, EditorSceneManager.previewSceneCount);
            Assert.IsTrue(root == null);
            Assert.AreEqual(PrefabEditingSessionStatus.None, PrefabEditingService.Status);

            Scene unrelatedPreviewScene = default;
            GameObject unrelatedPreviewRoot = null;
            int successfulCleanupAttempts = 0;
            try
            {
                GameObject secondRoot = PrefabEditingService.Open(TestPrefabPath);
                SetServiceField(
                    "_unloadPrefabContents",
                    (System.Action<GameObject>)(previewRoot =>
                    {
                        successfulCleanupAttempts++;
                        PrefabUtility.UnloadPrefabContents(previewRoot);
                    }));
                SessionState.SetString(
                    SessionAssetPathKey,
                    TestDirectory + "/SecondMismatchedRecoveryRecord.prefab");
                SessionState.SetString(SessionAssetGuidKey, string.Empty);
                SimulateManagedDomainReload();

                Assert.AreEqual(PrefabEditingSessionStatus.Lost, PrefabEditingService.Status);
                Assert.AreEqual(1, successfulCleanupAttempts);
                Assert.IsTrue(secondRoot == null);

                // Model Unity reusing the unloaded root's stale ID for another preview root.
                // Acknowledgement must trust the recorded successful cleanup and never resolve
                // the persisted ID again.
                unrelatedPreviewScene = NewOwnedPreviewScene();
                unrelatedPreviewRoot = new GameObject("S6UnrelatedReusedPreviewId");
                SceneManager.MoveGameObjectToScene(unrelatedPreviewRoot, unrelatedPreviewScene);
                SessionState.SetInt(
                    SessionRootInstanceIdKey,
                    unrelatedPreviewRoot.GetInstanceID());

                PrefabEditingService.Discard();

                Assert.AreEqual(1, successfulCleanupAttempts);
                Assert.IsTrue(unrelatedPreviewRoot != null);
                Assert.IsTrue(unrelatedPreviewRoot.scene.IsValid());
                Assert.AreEqual(PrefabEditingSessionStatus.None, PrefabEditingService.Status);
            }
            finally
            {
                RestoreUnloadPrefabContents();
                if (unrelatedPreviewScene.IsValid())
                    CloseOwnedPreviewScene(unrelatedPreviewScene);
            }
        }

        /// <summary>
        /// Characterization test that pins observed Unity 2022.3 behavior: a missing target
        /// directory, cyclic nesting, and an invalid extension throw ArgumentException instead
        /// of returning success=false. If Unity changes to return success=false, this test must
        /// fail so a genuine non-injected out-success test can replace this characterization.
        /// </summary>
        [Test]
        public void SaveAsPrefabAssetFailureInputs_Unity2022_3ThrowRatherThanReturnFalse_Characterization()
        {
            const string missingDirectoryPath =
                TestDirectory + "/MissingSaveDirectory/ShouldNotSave.prefab";
            const string invalidExtensionPath = TestDirectory + "/ShouldNotSave.txt";
            var returnedFalseCases = new List<string>();
            GameObject standaloneRoot = new GameObject("S6SaveFailureCharacterizationRoot");
            GameObject cyclicRoot = null;
            bool previousIgnore = LogAssert.ignoreFailingMessages;

            try
            {
                Assert.IsFalse(
                    AssetDatabase.IsValidFolder(TestDirectory + "/MissingSaveDirectory"),
                    "The missing-directory characterization requires a directory that does not exist");

                cyclicRoot = PrefabUtility.LoadPrefabContents(TestPrefabPath);
                Assert.IsNotNull(cyclicRoot);
                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
                GameObject cyclicInstance = PrefabUtility.InstantiatePrefab(
                    prefabAsset,
                    cyclicRoot.scene) as GameObject;
                Assert.IsNotNull(cyclicInstance);
                cyclicInstance.name = "S6CyclicNestedInstance";
                cyclicInstance.transform.SetParent(cyclicRoot.transform, false);

                LogAssert.ignoreFailingMessages = true;
                System.Exception missingDirectoryException = CaptureSaveAsPrefabAssetResult(
                    standaloneRoot,
                    missingDirectoryPath,
                    "missing target directory",
                    returnedFalseCases);
                System.Exception selfCycleException = CaptureSaveAsPrefabAssetResult(
                    cyclicRoot,
                    TestPrefabPath,
                    "self-cycle nesting",
                    returnedFalseCases);
                System.Exception invalidExtensionException = CaptureSaveAsPrefabAssetResult(
                    standaloneRoot,
                    invalidExtensionPath,
                    "invalid .txt extension",
                    returnedFalseCases);

                Assert.That(
                    returnedFalseCases,
                    Is.Empty,
                    "No characterized Unity 2022.3 failure input may return success=false");
                Assert.That(
                    missingDirectoryException,
                    Is.TypeOf<System.ArgumentException>(),
                    "Unity 2022.3 should throw ArgumentException for a missing target directory");
                Assert.That(
                    selfCycleException,
                    Is.TypeOf<System.ArgumentException>(),
                    "Unity 2022.3 should throw ArgumentException for self-cycle nesting");
                Assert.That(
                    invalidExtensionException,
                    Is.TypeOf<System.ArgumentException>(),
                    "Unity 2022.3 should throw ArgumentException for an invalid .txt extension");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
                if (cyclicRoot != null)
                    PrefabUtility.UnloadPrefabContents(cyclicRoot);
                if (standaloneRoot != null)
                    Object.DestroyImmediate(standaloneRoot);
            }
        }

        [Test]
        public void InjectedSaveFalse_ReturnsErrorAndPreservesActiveSessionWithUnsavedEdits()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject marker = new GameObject("UnsavedAfterSaveFalse");
            marker.transform.SetParent(root.transform, false);
            string prefabGuid = AssetDatabase.AssetPathToGUID(TestPrefabPath);
            int rootInstanceId = root.GetInstanceID();
            SetServiceField(
                "_savePrefabContents",
                (System.Func<GameObject, string, bool>)((rootToSave, assetPath) => false));

            JObject result = new SavePrefabContentsTool().Execute(new JObject());

            Assert.IsNull(result["success"]);
            Assert.AreEqual("internal_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("remain open"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("retry"));
            Assert.IsTrue(PrefabEditingService.IsEditing);
            Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            Assert.AreSame(root, PrefabEditingService.PrefabRoot);
            Transform retainedMarker = PrefabEditingService.PrefabRoot.transform.Find(marker.name);
            Assert.IsNotNull(retainedMarker);
            Assert.AreSame(marker, retainedMarker.gameObject);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
            Assert.IsNull(prefabAsset.transform.Find(marker.name));
            Assert.AreEqual(TestPrefabPath, SessionState.GetString(SessionAssetPathKey, string.Empty));
            Assert.AreEqual(prefabGuid, SessionState.GetString(SessionAssetGuidKey, string.Empty));
            Assert.AreEqual(rootInstanceId, SessionState.GetInt(SessionRootInstanceIdKey, 0));
        }

        [Test]
        public void SaveErrors_DistinguishNeverOpenedFromLostSession()
        {
            var tool = new SavePrefabContentsTool();
            JObject neverOpened = tool.Execute(new JObject());

            SessionState.SetString(SessionAssetPathKey, TestPrefabPath);
            SessionState.SetInt(SessionRootInstanceIdKey, _sceneObject.GetInstanceID());
            SimulateManagedDomainReload();
            JObject lost = tool.Execute(new JObject());

            Assert.AreEqual("validation_error", neverOpened["error"]?["type"]?.ToString());
            Assert.AreEqual(
                "prefab_session_lost_error",
                lost["error"]?["type"]?.ToString());
            Assert.AreNotEqual(
                neverOpened["error"]?["type"]?.ToString(),
                lost["error"]?["type"]?.ToString());
        }

        [Test]
        public void StructuredObjectPathReference_ResolvesInsidePrefabContents()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            var failures = new List<string>();
            var warnings = new List<string>();

            object resolved = SerializedFieldConverter.ConvertJTokenToValue(
                new JObject { ["objectPath"] = PrefabRootName + "/Inside" },
                typeof(GameObject),
                null,
                failures,
                warnings,
                root);

            Assert.AreSame(root.transform.Find("Inside").gameObject, resolved);
            Assert.IsEmpty(failures);
        }

        [Test]
        public void RootQualifiedObjectPath_UpdatesPrefabObjectWithoutFallingBackToSceneImposter()
        {
            GameObject sceneImposterRoot = new GameObject(PrefabRootName);
            GameObject sceneImposterChild = new GameObject("Inside");
            sceneImposterChild.transform.SetParent(sceneImposterRoot.transform, false);
            try
            {
                GameObject prefabRoot = PrefabEditingService.Open(TestPrefabPath);
                GameObject prefabChild = prefabRoot.transform.Find("Inside").gameObject;

                JObject result = new UpdateGameObjectTool().Execute(new JObject
                {
                    ["objectPath"] = PrefabRootName + "/Inside",
                    ["gameObjectData"] = new JObject { ["activeSelf"] = false }
                });

                Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
                Assert.IsFalse(prefabChild.activeSelf);
                Assert.IsTrue(sceneImposterChild.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(sceneImposterRoot);
            }
        }

        [Test]
        public void DirectResolver_RootQualifiedObjectPathExercisesPrefabSessionBranch()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);

            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, PrefabRootName + "/Inside", out GameObject resolved);

            Assert.IsNull(error);
            Assert.AreSame(root.transform.Find("Inside").gameObject, resolved);
        }

        [Test]
        public void PrefabInstanceId_MainlineUpdatesPreviewObject()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject inside = root.transform.Find("Inside").gameObject;

            JObject result = new UpdateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = inside.GetInstanceID(),
                ["gameObjectData"] = new JObject { ["activeSelf"] = false }
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.IsFalse(inside.activeSelf);
        }

        [Test]
        public void NameResolver_FindsInsidePrefabAndRejectsSceneOnlyName()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);

            JObject foundError = PrefabSessionScope.TryResolveGameObjectByName(
                "Inside", out GameObject found);
            JObject missError = PrefabSessionScope.TryResolveGameObjectByName(
                _sceneObject.name, out GameObject missed);

            Assert.IsNull(foundError);
            Assert.AreSame(root.transform.Find("Inside").gameObject, found);
            Assert.AreEqual("prefab_context_miss_error", missError["error"]?["type"]?.ToString());
            Assert.IsNull(missed);
        }

        [Test]
        public void ExternalUnloadInSameDomain_TransitionsToLostAndBlocksPhantomCreation()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            int beforeCount = CountSceneRootsNamed("S6PhantomRoot");

            Assert.AreEqual(PrefabEditingSessionStatus.Lost, PrefabEditingService.Status);
            Assert.IsNull(PrefabEditingService.AssetPath);
            Assert.AreEqual(TestPrefabPath, PrefabEditingService.LostAssetPath);

            JObject result = new UpdateGameObjectTool().Execute(new JObject
            {
                ["objectPath"] = "S6PhantomRoot/Deep",
                ["gameObjectData"] = new JObject { ["activeSelf"] = false }
            });

            Assert.AreEqual("prefab_session_lost_error", result["error"]?["type"]?.ToString());
            Assert.AreEqual(beforeCount, CountSceneRootsNamed("S6PhantomRoot"));
        }

        [Test]
        public void DeletePrefabRoot_IsRejectedWithoutDestroyingSession()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);

            JObject result = new DeleteGameObjectTool().Execute(new JObject
            {
                ["objectPath"] = PrefabRootName
            });

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("contents root"));
            Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            Assert.AreSame(root, PrefabEditingService.PrefabRoot);
        }

        [Test]
        public void DeleteAncestorSubtreeContainingPrefabRoot_IsRejected()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject inside = root.transform.Find("Inside").gameObject;
            inside.transform.SetParent(null, false);
            root.transform.SetParent(inside.transform, false);
            try
            {
                JObject result = new DeleteGameObjectTool().Execute(new JObject
                {
                    ["instanceId"] = inside.GetInstanceID()
                });

                Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
                Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("subtree"));
                Assert.IsTrue(root != null);
                Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            }
            finally
            {
                if (root != null)
                    root.transform.SetParent(null, false);
                if (root != null && inside != null)
                    inside.transform.SetParent(root.transform, false);
            }
        }

        [Test]
        public void ReparentChildToPreviewRoot_IsRejectedAndSavePreservesSubtreeOnDisk()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject inside = root.transform.Find("Inside").gameObject;
            GameObject descendant = new GameObject("R3PersistedSubtreeMarker");
            descendant.transform.SetParent(inside.transform, false);

            JObject result = new ReparentGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = inside.GetInstanceID()
            });
            PrefabEditingService.Save();
            string prefabYaml = File.ReadAllText(TestPrefabPath);

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("second preview root"));
            Assert.That(prefabYaml, Does.Contain("m_Name: Inside"));
            Assert.That(prefabYaml, Does.Contain("m_Name: R3PersistedSubtreeMarker"));
            Assert.AreEqual(PrefabEditingSessionStatus.None, PrefabEditingService.Status);
        }

        [Test]
        public void SaveUnloadFailure_ReportsSaveCompletedAndPreservesSessionRecord()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject marker = new GameObject("SavedBeforeUnloadFailure");
            marker.transform.SetParent(root.transform, false);
            SetServiceField(
                "_unloadPrefabContents",
                (System.Action<GameObject>)(_ => throw new System.InvalidOperationException("synthetic unload failure")));

            JObject result;
            try
            {
                result = new SavePrefabContentsTool().Execute(new JObject());
            }
            finally
            {
                RestoreUnloadPrefabContents();
            }

            Assert.AreEqual("prefab_cleanup_error", result["error"]?["type"]?.ToString());
            Assert.IsTrue(result["error"]?["details"]?["saveCompleted"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(
                PrefabEditingSessionStatus.Active.ToString(),
                result["error"]?["details"]?["sessionStatus"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("saved successfully"));
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("Call save_prefab_contents again"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Not.Contain("Failed to save"));
            Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            Assert.AreEqual(TestPrefabPath, SessionState.GetString(SessionAssetPathKey, string.Empty));

            GameObject savedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
            Assert.IsNotNull(savedAsset.transform.Find("SavedBeforeUnloadFailure"));
        }

        [Test]
        public void SaveUnloadFailure_AfterPreviewLossReportsLostAcknowledgementGuidance()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            SetServiceField(
                "_unloadPrefabContents",
                (System.Action<GameObject>)(loadedRoot =>
                {
                    PrefabUtility.UnloadPrefabContents(loadedRoot);
                    throw new System.InvalidOperationException("synthetic unload-after-loss failure");
                }));

            JObject result;
            try
            {
                result = new SavePrefabContentsTool().Execute(new JObject());
            }
            finally
            {
                RestoreUnloadPrefabContents();
            }

            Assert.AreEqual("prefab_cleanup_error", result["error"]?["type"]?.ToString());
            Assert.AreEqual(
                PrefabEditingSessionStatus.Lost.ToString(),
                result["error"]?["details"]?["sessionStatus"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("session is Lost"));
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("discard=true to acknowledge"));
            Assert.AreEqual(PrefabEditingSessionStatus.Lost, PrefabEditingService.Status);
            Assert.IsTrue(root == null);
        }

        [Test]
        public void MovedPrefab_SaveUsesGuidResolvedPathInsteadOfRecreatingOldPath()
        {
            string originalGuid = AssetDatabase.AssetPathToGUID(TestPrefabPath);
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject marker = new GameObject("MovedAssetMarker");
            marker.transform.SetParent(root.transform, false);

            string moveError = AssetDatabase.MoveAsset(TestPrefabPath, MovedPrefabPath);
            Assert.IsEmpty(moveError);
            Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            Assert.AreEqual(MovedPrefabPath, PrefabEditingService.AssetPath);

            JObject result = new SavePrefabContentsTool().Execute(new JObject());

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath));
            GameObject movedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MovedPrefabPath);
            Assert.IsNotNull(movedAsset);
            Assert.IsNotNull(movedAsset.transform.Find("MovedAssetMarker"));
            Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(MovedPrefabPath));
        }

        [Test]
        public void MovedPrefab_SaveUnloadFailurePreservesOldIdentityRecordAndActivePreview()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            string moveError = AssetDatabase.MoveAsset(TestPrefabPath, MovedPrefabPath);
            Assert.IsEmpty(moveError);
            SetServiceField(
                "_unloadPrefabContents",
                (System.Action<GameObject>)(_ => throw new System.InvalidOperationException("synthetic unload failure")));

            JObject result;
            try
            {
                result = new SavePrefabContentsTool().Execute(new JObject());
            }
            finally
            {
                RestoreUnloadPrefabContents();
            }

            Assert.AreEqual("prefab_cleanup_error", result["error"]?["type"]?.ToString());
            Assert.AreEqual(PrefabEditingSessionStatus.Active, PrefabEditingService.Status);
            Assert.AreSame(root, PrefabEditingService.PrefabRoot);
            Assert.AreEqual(MovedPrefabPath, PrefabEditingService.AssetPath);
            Assert.AreEqual(
                TestPrefabPath,
                SessionState.GetString(SessionAssetPathKey, string.Empty));
        }

        [Test]
        public void SuccessfulSaveAndDiscard_ClearAllPersistedSessionKeys()
        {
            PrefabEditingService.Open(TestPrefabPath);
            PrefabEditingService.Save();
            AssertPersistedSessionCleared();

            PrefabEditingService.Open(TestPrefabPath);
            PrefabEditingService.Discard();
            AssertPersistedSessionCleared();
        }

        [Test]
        public void AssetInstanceId_CannotBeUsedAsOperationTargetDuringSession()
        {
            GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
            string originalName = assetRoot.name;
            PrefabEditingService.Open(TestPrefabPath);

            JObject result = new UpdateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = assetRoot.GetInstanceID(),
                ["gameObjectData"] = new JObject { ["name"] = "S6MutatedAsset" }
            });
            JObject siblingResult = new SetSiblingIndexTool().Execute(new JObject
            {
                ["instanceId"] = assetRoot.GetInstanceID(),
                ["siblingIndex"] = 0
            });

            Assert.AreEqual("prefab_context_miss_error", result["error"]?["type"]?.ToString());
            Assert.AreEqual(
                "prefab_context_miss_error",
                siblingResult["error"]?["type"]?.ToString());
            Assert.AreEqual(originalName, assetRoot.name);
        }

        [Test]
        public void AssetInstanceId_RemainsAllowedAsSerializedReferenceDuringSession()
        {
            GameObject assetRoot = AssetDatabase.LoadAssetAtPath<GameObject>(TestPrefabPath);
            GameObject previewRoot = PrefabEditingService.Open(TestPrefabPath);
            var failures = new List<string>();

            object resolved = SerializedFieldConverter.ConvertJTokenToValue(
                new JValue(assetRoot.GetInstanceID()),
                typeof(GameObject),
                null,
                failures,
                null,
                previewRoot);

            Assert.AreSame(assetRoot, resolved);
            Assert.IsEmpty(failures);
        }

        [Test]
        public void PreviewObjectReference_ToScriptableObjectAssetFailsWithoutWritingFileIdZero()
        {
            const string holderPath = TestDirectory + "/ReferenceHolder.asset";
            PrefabSessionReferenceHolder holder =
                ScriptableObject.CreateInstance<PrefabSessionReferenceHolder>();
            AssetDatabase.CreateAsset(holder, holderPath);
            AssetDatabase.SaveAssets();
            PrefabEditingService.Open(TestPrefabPath);

            JObject result = new UpdateScriptableObjectTool().Execute(new JObject
            {
                ["assetPath"] = holderPath,
                ["fieldValues"] = new JObject
                {
                    ["target"] = new JObject
                    {
                        ["objectPath"] = PrefabRootName + "/Inside"
                    }
                }
            });

            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
            Assert.That(
                result["failedFields"]?[0]?["reason"]?.ToString(),
                Does.Contain("prefab_context_miss_error"));
            Assert.IsNull(holder.target);
        }

        [Test]
        public void PreviewObjectReference_WithUnknownWriteOwnerFailsClosed()
        {
            PrefabEditingService.Open(TestPrefabPath);
            var failures = new List<string>();

            object resolved = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["objectPath"] = PrefabRootName + "/Inside" },
                typeof(GameObject),
                failures);

            Assert.IsNull(resolved);
            Assert.That(failures, Has.Some.Contains("prefab_context_miss_error"));
            Assert.That(failures, Has.Some.Contains("unknown write target"));
        }

        [Test]
        public void StructuredReference_ScopeFailureFallsBackToObjectPathWithDisclosure()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            var failures = new List<string>();
            var warnings = new List<string>();

            object resolved = SerializedFieldConverter.ConvertJTokenToValue(
                new JObject
                {
                    ["assetPath"] = TestDirectory + "/Missing.asset",
                    ["instanceId"] = _sceneObject.GetInstanceID(),
                    ["objectPath"] = PrefabRootName + "/Inside"
                },
                typeof(GameObject),
                null,
                failures,
                warnings,
                root);

            Assert.AreSame(root.transform.Find("Inside").gameObject, resolved);
            Assert.IsEmpty(failures);
            Assert.That(warnings, Has.Some.Contains("prefab_context_miss_error"));
            Assert.That(warnings, Has.Some.Contains("resolved successfully via locator 'objectPath'"));
        }

        [Test]
        public void SerializedPropertyReference_ScopeFailureFallsBackToObjectPathWithDisclosure()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            EventSystem eventSystem = root.AddComponent<EventSystem>();
            var serializedObject = new SerializedObject(eventSystem);
            SerializedProperty property = serializedObject.FindProperty("m_FirstSelected");
            Assert.IsNotNull(property, "EventSystem.m_FirstSelected must remain serialized");
            var warnings = new List<string>();

            bool success = SerializedPropertyHelper.SetValue(
                property,
                new JObject
                {
                    ["assetPath"] = TestDirectory + "/Missing.asset",
                    ["instanceId"] = _sceneObject.GetInstanceID(),
                    ["objectPath"] = PrefabRootName + "/Inside"
                },
                warnings,
                "m_FirstSelected",
                out SerializedPropertyHelper.ObjectReferenceWrite write);

            Assert.IsTrue(success);
            Assert.AreSame(root.transform.Find("Inside").gameObject, write.AttemptedValue);
            Assert.That(warnings, Has.Some.Contains("prefab_context_miss_error"));
            Assert.That(warnings, Has.Some.Contains("resolved successfully via locator 'objectPath'"));
        }

        [Test]
        public void PrefabUiAutoCreation_IsRejectedWithoutAddingCanvasEventSystemOrPartialPath()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);

            JObject canvasResult = new CreateCanvasTool().Execute(new JObject
            {
                ["objectPath"] = PrefabRootName + "/Canvas"
            });
            JObject elementResult = new CreateUIElementTool().Execute(new JObject
            {
                ["objectPath"] = PrefabRootName + "/Panel/Button",
                ["elementType"] = "Button",
                ["requireCanvas"] = true
            });

            Assert.AreEqual("validation_error", canvasResult["error"]?["type"]?.ToString());
            Assert.AreEqual("canvas_error", elementResult["error"]?["type"]?.ToString());
            Assert.IsNull(root.GetComponentInChildren<Canvas>(true));
            Assert.IsNull(root.transform.Find("Canvas"));
            Assert.IsNull(root.transform.Find("Panel"));
            Assert.IsNull(root.transform.Find("EventSystem"));
        }

        [Test]
        public void NoSession_InactiveMultiScenePathResolutionMatchesHierarchyCreator()
        {
            Scene runnerScene = SceneManager.GetActiveScene();
            Assert.IsTrue(runnerScene.IsValid());
            Assert.IsTrue(
                EditorSceneManager.SaveScene(runnerScene, AdditiveTestScenePath, true),
                "The runner's isolated scene must be saved as a temporary scene-asset copy");
            _additiveTestScene = EditorSceneManager.OpenScene(
                AdditiveTestScenePath,
                OpenSceneMode.Additive);
            GameObject inactiveRoot = new GameObject("S6InactiveRoot");
            SceneManager.MoveGameObjectToScene(inactiveRoot, _additiveTestScene);
            GameObject inactiveChild = new GameObject("Deep");
            inactiveChild.transform.SetParent(inactiveRoot.transform, false);
            inactiveRoot.SetActive(false);
            try
            {
                JObject resolveError = PrefabSessionScope.TryResolveGameObject(
                    null, "S6InactiveRoot/Deep", out GameObject resolved);
                JObject createError = GameObjectHierarchyCreator.TryFindOrCreateHierarchicalGameObject(
                    "S6InactiveRoot/Deep", out GameObject foundOrCreated);
                var conversionFailures = new List<string>();
                object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                    new JObject { ["objectPath"] = "S6InactiveRoot/Deep" },
                    typeof(GameObject),
                    conversionFailures);

                Assert.IsNull(resolveError);
                Assert.IsNull(createError);
                Assert.AreSame(inactiveChild, resolved);
                Assert.AreSame(inactiveChild, foundOrCreated);
                Assert.AreSame(inactiveChild, converted);
                Assert.IsEmpty(conversionFailures);
                Assert.AreEqual(1, CountSceneRootsNamed("S6InactiveRoot"));
            }
            finally
            {
                Object.DestroyImmediate(inactiveRoot);
            }
        }

        [Test]
        public void NoSession_PathResolutionSkipsBuiltInPreviewScenes()
        {
            Scene previewScene = NewOwnedPreviewScene();
            GameObject previewRoot = new GameObject("S6BuiltInPreviewRoot");
            SceneManager.MoveGameObjectToScene(previewRoot, previewScene);
            GameObject previewChild = new GameObject("HiddenPanel");
            previewChild.transform.SetParent(previewRoot.transform, false);
            GameObject createdRoot = null;
            try
            {
                JObject matchesBeforeUpdate = new GetGameObjectsByNameTool().Execute(
                    new JObject { ["name"] = "HiddenPanel" });
                JObject result = new UpdateGameObjectTool().Execute(new JObject
                {
                    ["objectPath"] = "S6BuiltInPreviewRoot/HiddenPanel",
                    ["gameObjectData"] = new JObject { ["activeSelf"] = false }
                });

                Assert.IsTrue(matchesBeforeUpdate["success"]?.ToObject<bool>() ?? false);
                Assert.AreEqual(0, matchesBeforeUpdate["count"]?.ToObject<int>() ?? -1);
                Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
                createdRoot = EditorUtility.InstanceIDToObject(
                    result["instanceId"].ToObject<int>()) as GameObject;
                Assert.IsNotNull(createdRoot);
                Assert.AreNotSame(previewChild, createdRoot);
                Assert.IsFalse(EditorSceneManager.IsPreviewScene(createdRoot.scene));
                Assert.IsFalse(createdRoot.activeSelf);
                Assert.IsTrue(previewChild.activeSelf);
            }
            finally
            {
                if (createdRoot != null)
                    Object.DestroyImmediate(createdRoot.transform.root.gameObject);
                CloseOwnedPreviewScene(previewScene);
            }
        }

        [Test]
        public void ScreenshotCamera_DefaultWithoutSessionUsesLoadedSceneMainCamera()
        {
            GameObject cameraObject = new GameObject("S6SceneMainCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            try
            {
                JObject result = new ScreenshotCameraTool().Execute(new JObject
                {
                    ["width"] = 8,
                    ["height"] = 8
                });

                Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
                Assert.AreEqual("image/png", result["mimeType"]?.ToString());
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ScreenshotCamera_DefaultDuringSessionUsesPrefabMainCamera()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            GameObject cameraObject = new GameObject("S6PreviewMainCamera");
            try
            {
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();

                JObject result = new ScreenshotCameraTool().Execute(new JObject
                {
                    ["width"] = 8,
                    ["height"] = 8
                });

                Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
                Assert.That(result["message"]?.ToString(), Does.Contain(cameraObject.name));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void TearDown_NewOrphanPreviewCamera_ClosesSceneAndFailsFixture()
        {
            Scene leakedScene = NewOwnedPreviewScene();
            var leakedCameraObject = new GameObject("S8LeakGuardCamera");
            SceneManager.MoveGameObjectToScene(leakedCameraObject, leakedScene);
            leakedCameraObject.AddComponent<Camera>();
            try
            {
                AssertionException failure = Assert.Throws<AssertionException>(() => TearDown());

                Assert.IsTrue(
                    leakedCameraObject == null,
                    "Closing the leaked preview scene must destroy its camera objects");
                Assert.That(failure.Message, Does.Contain("S8LeakGuardCamera"));
                Assert.That(failure.Message, Does.Contain("new orphan preview scene"));
                Assert.IsFalse(_ownsFixtureState, "Manual TearDown must not run twice");
            }
            finally
            {
                if (leakedScene.IsValid())
                    CloseOwnedPreviewScene(leakedScene);
            }
        }

        [Test]
        public void TearDown_ActiveSessionCameraSnapshotFailsEvenThoughDiscardUnloadsScene()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            var cameraObject = new GameObject("S8SessionLeakSnapshotCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.AddComponent<Camera>();

            AssertionException failure = Assert.Throws<AssertionException>(() => TearDown());

            Assert.IsTrue(cameraObject == null, "Discard should unload the session preview scene");
            Assert.That(failure.Message, Does.Contain("S8SessionLeakSnapshotCamera"));
            Assert.That(failure.Message, Does.Contain("before Discard"));
            Assert.IsFalse(_ownsFixtureState, "Manual TearDown must not run twice");
        }

        [Test]
        public void LeakGuard_UnownedOrphanReportsDetailsWithoutClosingScene()
        {
            Scene unownedScene = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("S8UnownedOrphanCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, unownedScene);
            cameraObject.AddComponent<Camera>();
            try
            {
                string failure = CloseNewOrphanPreviewScenesWithGameCameras();

                Assert.That(failure, Does.Contain("S8UnownedOrphanCamera"));
                Assert.That(failure, Does.Contain("scenePath=''"));
                Assert.That(failure, Does.Contain("left open"));
                Assert.IsTrue(unownedScene.IsValid());
                Assert.IsNotNull(cameraObject);
            }
            finally
            {
                if (unownedScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(unownedScene);
            }
        }

        [Test]
        public void TearDown_CleanupExceptionRemainsPrimaryWhenLeakIsAlsoDetected()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            var cameraObject = new GameObject("S8LeakAlongsideCleanupFailure");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.AddComponent<Camera>();
            _discardSession = () =>
                throw new System.InvalidOperationException("Injected discard cleanup failure");
            try
            {
                System.InvalidOperationException failure =
                    Assert.Throws<System.InvalidOperationException>(() => TearDown());

                Assert.AreEqual("Injected discard cleanup failure", failure.Message);
                Assert.That(
                    failure.Data["PrefabSessionScopeTests.CameraLeak"]?.ToString(),
                    Does.Contain("S8LeakAlongsideCleanupFailure"));
                Assert.IsFalse(_ownsFixtureState, "Manual TearDown must not run twice");
            }
            finally
            {
                _discardSession = PrefabEditingService.Discard;
                if (PrefabEditingService.Status != PrefabEditingSessionStatus.None)
                    PrefabEditingService.Discard();
                ResetSessionState();
                if (AssetDatabase.IsValidFolder(TestDirectory))
                    AssetDatabase.DeleteAsset(TestDirectory);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void ScreenshotCamera_DefaultDuringSessionNeverFallsBackToSceneCamera()
        {
            GameObject sceneCameraObject = new GameObject("S6SceneFallbackCamera");
            sceneCameraObject.tag = "MainCamera";
            sceneCameraObject.AddComponent<Camera>();
            try
            {
                PrefabEditingService.Open(TestPrefabPath);

                JObject result = new ScreenshotCameraTool().Execute(new JObject
                {
                    ["width"] = 8,
                    ["height"] = 8
                });

                Assert.AreEqual("tool_execution_error", result["error"]?["type"]?.ToString());
                Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(TestPrefabPath));
                Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(PrefabRootName));
                Assert.That(
                    result["error"]?["message"]?.ToString(),
                    Does.Contain("does not fall back to loaded scene cameras"));
            }
            finally
            {
                Object.DestroyImmediate(sceneCameraObject);
            }
        }

        [UnityTest]
        public IEnumerator BatchExecute_AtomicDuringSessionIsRejectedBeforeExecution()
        {
            PrefabEditingService.Open(TestPrefabPath);
            var completion = new TaskCompletionSource<JObject>();
            new BatchExecuteTool(McpUnityServer.Instance).ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject()
                    }
                },
                ["atomic"] = true,
                ["stopOnError"] = true
            }, completion);

            while (!completion.Task.IsCompleted)
                yield return null;

            JObject result = completion.Task.Result;
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("not supported while a Prefab contents session is active"));
            Assert.IsNull(result["results"]);
        }

        [UnityTest]
        public IEnumerator BatchExecute_AtomicCannotOpenPrefabSessionMidBatch()
        {
            var completion = new TaskCompletionSource<JObject>();
            new BatchExecuteTool(McpUnityServer.Instance).ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "open_prefab_contents",
                        ["params"] = new JObject { ["prefabPath"] = TestPrefabPath }
                    }
                },
                ["atomic"] = true,
                ["stopOnError"] = true
            }, completion);

            while (!completion.Task.IsCompleted)
                yield return null;

            JObject result = completion.Task.Result;
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("cannot include open_prefab_contents"));
            Assert.AreEqual(PrefabEditingSessionStatus.None, PrefabEditingService.Status);
        }

        [TestCase("target")]
        [TestCase("TargetProperty")]
        public void CreatePrefab_PreviewReferenceFailureIdentifiesActualOwner(string memberName)
        {
            PrefabEditingService.Open(TestPrefabPath);

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = TestDirectory + "/OwnerAware_" + memberName,
                ["componentName"] = typeof(PrefabSessionReferenceBehaviour).FullName,
                ["fieldValues"] = new JObject
                {
                    [memberName] = new JObject
                    {
                        ["objectPath"] = PrefabRootName + "/Inside"
                    }
                }
            });

            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
            string reason = result["failedFields"]?[0]?["reason"]?.ToString();
            Assert.That(reason, Does.Contain(nameof(PrefabSessionReferenceBehaviour)));
            Assert.That(reason, Does.Not.Contain("unknown write target"));
        }

        [Test]
        public void AddAssetWithMissingParent_FailsBeforeInstantiatingInPrefabContents()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            int originalChildCount = root.transform.childCount;

            JObject result = new AddAssetToSceneTool().Execute(new JObject
            {
                ["assetPath"] = TestPrefabPath,
                ["parentId"] = int.MaxValue
            });

            Assert.AreEqual("not_found_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("was not instantiated"));
            Assert.AreEqual(originalChildCount, root.transform.childCount);
        }

        [Test]
        public void AddAssetFailureAfterInstantiation_DestroysPartialPreviewInstance()
        {
            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            int originalChildCount = root.transform.childCount;
            SetAddAssetToolField(
                "_pingObject",
                (System.Action<UnityEngine.Object>)(_ =>
                    throw new System.InvalidOperationException("synthetic ping failure")));

            JObject result;
            try
            {
                result = new AddAssetToSceneTool().Execute(new JObject
                {
                    ["assetPath"] = TestPrefabPath
                });
            }
            finally
            {
                RestoreAddAssetPingObject();
            }

            Assert.AreEqual("instantiation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("synthetic ping failure"));
            Assert.AreEqual(originalChildCount, root.transform.childCount);
        }

        [Test]
        public void ParentResolutionErrors_IdentifyNewParentForDuplicateAndReparent()
        {
            JObject duplicate = new DuplicateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = _sceneObject.GetInstanceID(),
                ["newParent"] = "S6MissingParent"
            });
            JObject reparent = new ReparentGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = _sceneObject.GetInstanceID(),
                ["newParent"] = "S6MissingParent"
            });

            Assert.That(
                duplicate["error"]?["message"]?.ToString(),
                Does.StartWith("New parent resolution failed:"));
            Assert.That(
                reparent["error"]?["message"]?.ToString(),
                Does.StartWith("New parent resolution failed:"));
        }

        [Test]
        public void HalfPersistedRecord_WithOnlyRootId_IsLostNotActive()
        {
            GameObject unsavedObject = new GameObject("S6HalfRecordObject");
            try
            {
                Assert.AreEqual(string.Empty, unsavedObject.scene.path);
                SessionState.SetInt(SessionRootInstanceIdKey, unsavedObject.GetInstanceID());
                SimulateManagedDomainReload();

                Assert.AreEqual(PrefabEditingSessionStatus.Lost, PrefabEditingService.Status);
                Assert.AreEqual("<unknown Prefab asset>", PrefabEditingService.LostAssetPath);
            }
            finally
            {
                Object.DestroyImmediate(unsavedObject);
            }
        }

        [Test]
        public void EmptyLocatorWithoutSession_ReturnsNoObjectAndNoScopeError()
        {
            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, null, out GameObject resolved);

            Assert.IsNull(error);
            Assert.IsNull(resolved);
        }

        private string CloseNewOrphanPreviewScenesWithGameCameras()
        {
            Dictionary<int, OrphanPreviewSceneSnapshot> current =
                FindOrphanPreviewScenesWithGameCameras();
            var ownedLeaks = new List<string>();
            var unownedSuspiciousScenes = new List<string>();
            var closeFailures = new List<string>();
            foreach (OrphanPreviewSceneSnapshot candidate in current.Values)
            {
                string details = DescribePreviewScene(candidate);
                if (!_ownedPreviewSceneHandles.Contains(candidate.Scene.handle))
                {
                    unownedSuspiciousScenes.Add(details);
                    continue;
                }

                ownedLeaks.Add(details);
                try
                {
                    if (IsLeakedPreviewSceneStillAlive(candidate))
                        CloseOwnedPreviewScene(candidate.Scene);
                    if (IsLeakedPreviewSceneStillAlive(candidate))
                        closeFailures.Add(details + ": scene remained valid after ClosePreviewScene");
                }
                catch (System.Exception ex)
                {
                    closeFailures.Add(details + ": " + ex.GetType().Name + ":" + ex.Message);
                }
            }

            if (ownedLeaks.Count == 0 && unownedSuspiciousScenes.Count == 0)
                return null;

            string failure = string.Empty;
            if (ownedLeaks.Count > 0)
            {
                failure =
                    $"PrefabSessionScopeTests created {ownedLeaks.Count} new orphan preview " +
                    $"scene(s) containing CameraType.Game cameras [{string.Join("; ", ownedLeaks)}]. " +
                    "The leak guard closed only fixture-owned preview scenes and is failing the test.";
            }
            if (unownedSuspiciousScenes.Count > 0)
            {
                failure = CombineFailures(
                    failure,
                    "PrefabSessionScopeTests observed suspicious orphan preview scene(s) it " +
                    $"does not own [{string.Join("; ", unownedSuspiciousScenes)}]; they were left open.");
            }
            if (closeFailures.Count > 0)
                failure += $" Close failures: {string.Join("; ", closeFailures)}";
            return failure;
        }

        private Scene NewOwnedPreviewScene()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            _ownedPreviewSceneHandles.Add(scene.handle);
            return scene;
        }

        private void CloseOwnedPreviewScene(Scene scene)
        {
            int handle = scene.handle;
            EditorSceneManager.ClosePreviewScene(scene);
            if (!scene.IsValid())
                _ownedPreviewSceneHandles.Remove(handle);
        }

        private void RememberActivePrefabSessionScene()
        {
            GameObject root = PrefabEditingService.PrefabRoot;
            if (root != null
                && root.scene.IsValid()
                && EditorSceneManager.IsPreviewScene(root.scene))
            {
                _ownedPreviewSceneHandles.Add(root.scene.handle);
            }
        }

        private static string DescribePreviewScene(OrphanPreviewSceneSnapshot snapshot)
        {
            return $"scenePath='{snapshot.ScenePath ?? string.Empty}' " +
                $"cameras=[{string.Join(", ", snapshot.CameraNames)}]";
        }

        private static bool IsLeakedPreviewSceneStillAlive(
            OrphanPreviewSceneSnapshot snapshot)
        {
            foreach (Camera camera in snapshot.Cameras)
            {
                if (camera != null
                    && camera.gameObject.scene == snapshot.Scene
                    && EditorSceneManager.IsPreviewScene(camera.gameObject.scene))
                {
                    return true;
                }
            }
            return false;
        }

        private static string FindActiveSessionGameCameraLeak()
        {
            if (PrefabEditingService.Status == PrefabEditingSessionStatus.None)
                return null;

            GameObject prefabRoot = PrefabEditingService.PrefabRoot;
            if (prefabRoot == null || !prefabRoot.scene.IsValid())
                return null;

            var cameraNames = new List<string>();
            foreach (Camera camera in prefabRoot.GetComponentsInChildren<Camera>(true))
            {
                if (camera != null && camera.cameraType == CameraType.Game)
                    cameraNames.Add(camera.name);
            }
            if (cameraNames.Count == 0)
                return null;

            return
                "PrefabSessionScopeTests left CameraType.Game camera(s) inside the active " +
                $"session preview scene before Discard [{string.Join(", ", cameraNames)}].";
        }

        private static string CombineFailures(params string[] failures)
        {
            var present = new List<string>();
            foreach (string failure in failures)
            {
                if (!string.IsNullOrEmpty(failure))
                    present.Add(failure);
            }
            return present.Count == 0 ? null : string.Join(" ", present);
        }

        private static Dictionary<int, OrphanPreviewSceneSnapshot>
            FindOrphanPreviewScenesWithGameCameras()
        {
            var loadedSceneHandles = new HashSet<int>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(sceneIndex);
                if (loadedScene.IsValid())
                    loadedSceneHandles.Add(loadedScene.handle);
            }

            var contextSceneHandles = new HashSet<int>();
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.IsValid())
                contextSceneHandles.Add(prefabStage.scene.handle);
            if (PrefabEditingService.Status != PrefabEditingSessionStatus.None)
            {
                GameObject prefabRoot = PrefabEditingService.PrefabRoot;
                if (prefabRoot != null && prefabRoot.scene.IsValid())
                    contextSceneHandles.Add(prefabRoot.scene.handle);
            }

            var result = new Dictionary<int, OrphanPreviewSceneSnapshot>();
            foreach (Camera camera in UnityEngine.Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera == null || camera.cameraType != CameraType.Game)
                    continue;

                Scene scene = camera.gameObject.scene;
                if (!scene.IsValid()
                    || loadedSceneHandles.Contains(scene.handle)
                    || contextSceneHandles.Contains(scene.handle)
                    || !EditorSceneManager.IsPreviewScene(scene))
                {
                    continue;
                }

                if (!result.TryGetValue(scene.handle, out OrphanPreviewSceneSnapshot snapshot))
                {
                    snapshot = new OrphanPreviewSceneSnapshot
                    {
                        Scene = scene,
                        ScenePath = scene.path ?? string.Empty
                    };
                    result.Add(scene.handle, snapshot);
                }
                snapshot.CameraNames.Add(camera.name);
                snapshot.Cameras.Add(camera);
            }
            return result;
        }

        private static void TryCleanup(
            System.Action cleanup,
            ref System.Exception firstFailure)
        {
            try
            {
                cleanup();
            }
            catch (System.Exception ex)
            {
                if (firstFailure == null)
                    firstFailure = ex;
            }
        }

        private static int CountSceneRootsNamed(string name)
        {
            int count = 0;
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == name)
                        count++;
                }
            }
            return count;
        }

        private static void AssertPersistedSessionCleared()
        {
            Assert.AreEqual(string.Empty, SessionState.GetString(SessionAssetPathKey, string.Empty));
            Assert.AreEqual(string.Empty, SessionState.GetString(SessionAssetGuidKey, string.Empty));
            Assert.AreEqual(0, SessionState.GetInt(SessionRootInstanceIdKey, 0));
        }

        private static System.Exception CaptureSaveAsPrefabAssetResult(
            GameObject root,
            string assetPath,
            string caseName,
            ICollection<string> returnedFalseCases)
        {
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, assetPath, out bool success);
                if (!success)
                    returnedFalseCases.Add(caseName);
                return null;
            }
            catch (System.Exception ex)
            {
                return ex;
            }
        }

        private static void SimulateManagedDomainReload()
        {
            SetServiceField("_prefabRoot", null);
            SetServiceField("_assetPath", null);
            SetServiceField("_assetGuid", null);
            SetServiceField("_sessionLost", false);
            SetServiceField("_lostAssetPath", null);
            SetServiceField("_lostPrefabRoot", null);
            SetServiceField("_lostPreviewWasUnloaded", false);
        }

        private static void ResetSessionState()
        {
            SessionState.EraseString(SessionAssetPathKey);
            SessionState.EraseString(SessionAssetGuidKey);
            SessionState.EraseInt(SessionRootInstanceIdKey);
            SimulateManagedDomainReload();
        }

        private static void RestoreUnloadPrefabContents()
        {
            SetServiceField(
                "_unloadPrefabContents",
                OriginalUnloadPrefabContents);
        }

        private static void RestoreSavePrefabContents()
        {
            SetServiceField(
                "_savePrefabContents",
                OriginalSavePrefabContents);
        }

        private static void RestoreAddAssetPingObject()
        {
            SetAddAssetToolField(
                "_pingObject",
                OriginalPingObject);
        }

        private static void SetAddAssetToolField(string name, object value)
        {
            FieldInfo field = typeof(AddAssetToSceneTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                Assert.Fail($"AddAssetToSceneTool private field '{name}' was not found");
            field.SetValue(null, value);
        }

        private static object GetAddAssetToolField(string name)
        {
            FieldInfo field = typeof(AddAssetToSceneTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new System.MissingFieldException(
                    typeof(AddAssetToSceneTool).FullName, name);
            return field.GetValue(null);
        }

        private static void SetServiceField(string name, object value)
        {
            FieldInfo field = typeof(PrefabEditingService).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                Assert.Fail($"PrefabEditingService private field '{name}' was not found");
            field.SetValue(null, value);
        }

        private static object GetServiceField(string name)
        {
            FieldInfo field = typeof(PrefabEditingService).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new System.MissingFieldException(
                    typeof(PrefabEditingService).FullName, name);
            return field.GetValue(null);
        }
    }
}
