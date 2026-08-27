using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using McpUnity.Resources;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Tests
{
    public class ObjectPathAmbiguityTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<Scene> _createdScenes = new List<Scene>();

        private string _prefix;
        private string _assetFolder;
        private const string SceneFolderName = "McpUnityObjectPathSceneTests";
        private const string SceneFolder = "Assets/" + SceneFolderName;
        private bool _openedPrefabSession;
        private Scene _originalActiveScene;

        [SetUp]
        public void SetUp()
        {
            PrefabEditingSessionStatus status = PrefabEditingService.Status;
            if (status != PrefabEditingSessionStatus.None)
            {
                Assert.Ignore(
                    $"ObjectPathAmbiguityTests will not discard an existing {status} Prefab " +
                    $"session for '{PrefabEditingService.AssetPath ?? PrefabEditingService.LostAssetPath}'.");
            }

            _prefix = "S7ObjectPath_" + Guid.NewGuid().ToString("N");
            _originalActiveScene = SceneManager.GetActiveScene();

            // Self-heal residue from a previous run killed mid-test (e.g. the 180s run_tests
            // cap): the scene folder name is constant, so leftovers are always claimable here.
            // UTF only warns about leftover assets, it never fails the run, so TearDown alone
            // is not a sufficient guarantee.
            bool scenesClosed = CloseScenesUnder(SceneFolder);
            if (scenesClosed && AssetDatabase.IsValidFolder(SceneFolder))
            {
                // Never DeleteAsset a folder that still holds a loaded scene; a failed close
                // leaves the folder for the next self-heal attempt instead.
                AssetDatabase.DeleteAsset(SceneFolder);
                AssetDatabase.Refresh();
            }
            // Also reclaim prefix-named roots a killed run may have left in the runner scene
            // (they would otherwise be duplicated into every future saveAsCopy snapshot).
            // Empty-name residue is not reclaimable by name; see the review defer note.
            foreach (GameObject root in _originalActiveScene.GetRootGameObjects())
            {
                if (root != null && root.name.StartsWith("S7ObjectPath_", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_openedPrefabSession && PrefabEditingService.Status != PrefabEditingSessionStatus.None)
                PrefabEditingService.Discard();
            _openedPrefabSession = false;

            foreach (GameObject gameObject in _createdObjects.Where(item => item != null))
                UnityEngine.Object.DestroyImmediate(gameObject);
            _createdObjects.Clear();

            if (_originalActiveScene.IsValid() && _originalActiveScene.isLoaded
                && SceneManager.GetActiveScene() != _originalActiveScene)
            {
                SceneManager.SetActiveScene(_originalActiveScene);
            }

            for (int i = _createdScenes.Count - 1; i >= 0; i--)
            {
                Scene scene = _createdScenes[i];
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
            _createdScenes.Clear();

            if (AssetDatabase.IsValidFolder(SceneFolder))
            {
                if (CloseScenesUnder(SceneFolder))
                {
                    AssetDatabase.DeleteAsset(SceneFolder);
                    AssetDatabase.Refresh();
                }
            }

            if (!string.IsNullOrEmpty(_assetFolder))
            {
                AssetDatabase.DeleteAsset(_assetFolder);
                _assetFolder = null;
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Site01_PrefabRootWalker_AmbiguousChildFailsWithEveryCandidate()
        {
            string rootName = _prefix + "_PrefabRoot";
            GameObject previewRoot = OpenTestPrefab(rootName);
            GameObject first = new GameObject("Twin");
            GameObject second = new GameObject("Twin");
            first.transform.SetParent(previewRoot.transform, false);
            second.transform.SetParent(previewRoot.transform, false);

            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, rootName + "/Twin", out GameObject resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, rootName + "/Twin", first, second);
            Assert.AreSame(first, previewRoot.transform.GetChild(0).gameObject);
            Assert.AreSame(second, previewRoot.transform.GetChild(1).gameObject);
        }

        [Test]
        public void Site02_ScenePathResolver_AmbiguousChildDoesNotUseFirstMatch()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, root.name + "/Twin", out GameObject resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
            Assert.AreEqual(first.GetInstanceID(), root.transform.GetChild(0).gameObject.GetInstanceID());
            Assert.AreEqual(second.GetInstanceID(), root.transform.GetChild(1).gameObject.GetInstanceID());
        }

        [Test]
        public void Site03_CrossSceneRootWalker_AmbiguousRootsFailTogether()
        {
            string rootName = _prefix + "_CrossSceneRoot";
            Scene firstScene = CreateScene(_prefix + "_SceneA");
            Scene secondScene = CreateScene(_prefix + "_SceneB");
            GameObject first = Track(new GameObject(rootName));
            GameObject second = Track(new GameObject(rootName));
            SceneManager.MoveGameObjectToScene(first, firstScene);
            SceneManager.MoveGameObjectToScene(second, secondScene);

            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, rootName, out GameObject resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, rootName, first, second);
            var scenes = ((JArray)error["error"]["details"]["candidates"])
                .Select(candidate => candidate["scene"]?.ToString())
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { firstScene.name, secondScene.name }, scenes);

            string firstPath = GameObjectPathUtils.GetCanonicalPath(first);
            string secondPath = GameObjectPathUtils.GetCanonicalPath(second);
            CollectionAssert.AreEqual(
                new[] { rootName + "[0]", rootName + "[1]" },
                new[] { firstPath, secondPath });
            Assert.IsNull(PrefabSessionScope.TryResolveGameObject(
                null, firstPath, out GameObject resolvedFirst));
            Assert.IsNull(PrefabSessionScope.TryResolveGameObject(
                null, secondPath, out GameObject resolvedSecond));
            Assert.AreSame(first, resolvedFirst);
            Assert.AreSame(second, resolvedSecond);
        }

        [Test]
        public void Site04_GetInteractableElementsRoot_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = InvokePrivatePathResolver(
                typeof(GetInteractableElementsTool),
                "TryResolveRootTransform",
                root.name + "/Twin",
                out object resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site05_WaitConditionPoll_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = InvokePrivatePathResolver(
                typeof(WaitForConditionTool),
                "TryResolveConditionTarget",
                root.name + "/Twin",
                out object resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site06_WaitFinalStateRead_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = InvokePrivatePathResolver(
                typeof(WaitForConditionTool),
                "TryResolveFinalStateTarget",
                root.name + "/Twin",
                out object resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site07_SimulateDragTarget_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = InvokePrivatePathResolver(
                typeof(SimulateDragTool),
                "TryResolveDragTarget",
                root.name + "/Twin",
                out object resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site08_SimulateDragDropTarget_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);

            JObject error = InvokePrivatePathResolver(
                typeof(SimulateDragTool),
                "TryResolveDropTarget",
                root.name + "/Twin",
                out object resolved);

            Assert.IsNull(resolved);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site09_UguiPrefabCanvasWalker_AmbiguityIsReturned()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);
            MethodInfo method = typeof(UGUIToolUtils).GetMethod(
                "TryFindExistingCanvasInPrefabPath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);
            object[] arguments = { root, root.name + "/Twin", null };

            JObject error = (JObject)method.Invoke(null, arguments);

            Assert.IsNull(arguments[2]);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
        }

        [Test]
        public void Site10_HierarchyCreator_AmbiguityFailsWithoutCreatingAnotherSibling()
        {
            CreateDuplicateHierarchy(out GameObject root, out GameObject first, out GameObject second);
            int childCountBefore = root.transform.childCount;

            JObject error = GameObjectHierarchyCreator.TryFindOrCreateHierarchicalGameObject(
                root.name + "/Twin", out GameObject foundOrCreated);

            Assert.IsNull(foundOrCreated);
            AssertAmbiguity(error, root.name + "/Twin", first, second);
            Assert.AreEqual(childCountBefore, root.transform.childCount);
            Assert.AreSame(first, root.transform.GetChild(0).gameObject);
            Assert.AreSame(second, root.transform.GetChild(1).gameObject);
        }

        [Test]
        public void HierarchyCreator_ShortNestedNameMiss_ListsCanonicalCandidateWithoutCreatingRoot()
        {
            string nestedName = _prefix + "_NestedOnly";
            GameObject parent = Track(new GameObject(_prefix + "_Parent"));
            GameObject nested = Track(new GameObject(nestedName));
            nested.transform.SetParent(parent.transform, false);
            int rootCountBefore = parent.scene.rootCount;

            JObject error = GameObjectHierarchyCreator.TryFindOrCreateHierarchicalGameObject(
                nestedName, out GameObject foundOrCreated);

            Assert.IsNull(foundOrCreated);
            Assert.AreEqual("not_found_error", error?["error"]?["type"]?.ToString());
            Assert.AreEqual(1, error?["error"]?["details"]?["candidateCount"]?.ToObject<int>());
            Assert.AreEqual(
                GameObjectPathUtils.GetCanonicalPath(nested),
                error?["error"]?["details"]?["candidates"]?[0]?["path"]?.ToString());
            Assert.That(error?["error"]?["message"]?.ToString(), Does.Contain("canonical paths"));
            Assert.AreEqual(rootCountBefore, parent.scene.rootCount);
            Assert.AreSame(nested, parent.transform.GetChild(0).gameObject);
        }

        [Test]
        public void GetGameObject_PlainNestedDuplicateName_ReturnsEveryCandidate()
        {
            string nestedName = _prefix + "_NestedDuplicate";
            GameObject firstParent = Track(new GameObject(_prefix + "_FirstParent"));
            GameObject secondParent = Track(new GameObject(_prefix + "_SecondParent"));
            GameObject first = Track(new GameObject(nestedName));
            GameObject second = Track(new GameObject(nestedName));
            first.transform.SetParent(firstParent.transform, false);
            second.transform.SetParent(secondParent.transform, false);

            JObject result = new GetGameObjectTool().Execute(
                new JObject { ["idOrName"] = nestedName });

            AssertAmbiguity(result, nestedName, first, second);
        }

        [Test]
        public void GetGameObjectEntrypoints_RootCanonicalIndexRoundTrips()
        {
            string rootName = _prefix + "_LookupRoot";
            GameObject first = Track(new GameObject(rootName));
            GameObject second = Track(new GameObject(rootName));
            string firstPath = GameObjectPathUtils.GetCanonicalPath(first);
            string secondPath = GameObjectPathUtils.GetCanonicalPath(second);

            JObject toolResult = new GetGameObjectTool().Execute(
                new JObject { ["idOrName"] = firstPath });
            JObject resourceResult = new GetGameObjectResource().Fetch(
                new JObject { ["idOrName"] = secondPath });

            Assert.AreEqual(rootName + "[0]", firstPath);
            Assert.AreEqual(rootName + "[1]", secondPath);
            Assert.IsTrue(toolResult["success"]?.ToObject<bool>() ?? false, toolResult.ToString());
            Assert.AreEqual(first.GetInstanceID(), toolResult["instanceId"]?.ToObject<int>());
            Assert.IsTrue(
                resourceResult["success"]?.ToObject<bool>() ?? false,
                resourceResult.ToString());
            Assert.AreEqual(second.GetInstanceID(), resourceResult["instanceId"]?.ToObject<int>());

            JObject ambiguousPlainName = new GetGameObjectTool().Execute(
                new JObject { ["idOrName"] = rootName });
            AssertAmbiguity(ambiguousPlainName, rootName, first, second);
        }

        [Test]
        public void IndexedMiss_IsSoftForWaitExistsAndNotExists_WithoutLiteralFallback()
        {
            GameObject root = Track(new GameObject(_prefix + "_PollingRoot"));
            GameObject literal = Track(new GameObject("Button[0]"));
            literal.transform.SetParent(root.transform, false);
            string indexedPath = root.name + "/Button[0]";

            bool exists = InvokeWaitCheckCondition(
                indexedPath, "exists", out JObject existsError);
            bool notExists = InvokeWaitCheckCondition(
                indexedPath, "not_exists", out JObject notExistsError);

            Assert.IsFalse(exists);
            Assert.IsNull(existsError, existsError?.ToString());
            Assert.IsTrue(notExists);
            Assert.IsNull(notExistsError, notExistsError?.ToString());
            Assert.AreSame(literal, root.transform.GetChild(0).gameObject);
        }

        [Test]
        public void Polling_PrefabRootPrefixMiss_IsHardEvenWhenSceneObjectExists()
        {
            GameObject sceneRoot = Track(new GameObject(_prefix + "_SceneCanvas"));
            GameObject sceneChild = Track(new GameObject(_prefix + "_SceneButton"));
            sceneChild.transform.SetParent(sceneRoot.transform, false);
            string scenePath = GameObjectPathUtils.GetCanonicalPath(sceneChild);
            OpenTestPrefab(_prefix + "_HUD");

            bool notExists = InvokeWaitCheckCondition(
                scenePath, "not_exists", out JObject resolutionError);

            Assert.IsFalse(notExists);
            Assert.AreEqual(
                PrefabSessionScope.ContextMissErrorType,
                resolutionError?["error"]?["type"]?.ToString());
            Assert.That(
                resolutionError?["error"]?["message"]?.ToString(),
                Does.Contain(sceneRoot.name));
            Assert.AreSame(sceneChild, sceneRoot.transform.GetChild(0).gameObject);
        }

        [Test]
        public void HierarchyCreator_IndexedMissAfterCreation_RollsBackOwnedObjectsAndClearsOut()
        {
            GameObject root = Track(new GameObject(_prefix + "_RollbackRoot"));
            int childCountBefore = root.transform.childCount;

            JObject error = GameObjectHierarchyCreator.TryFindOrCreateHierarchicalGameObject(
                root.name + "/P/Foo[0]", out GameObject foundOrCreated);

            Assert.IsNotNull(error);
            Assert.AreEqual("not_found_error", error["error"]?["type"]?.ToString());
            Assert.That(error["error"]?["message"]?.ToString(), Does.Contain("same-name index 0"));
            Assert.IsNull(foundOrCreated);
            Assert.AreEqual(childCountBefore, root.transform.childCount);
            Assert.IsNull(root.transform.Find("P"));
        }

        [Test]
        public void OpenPrefabChildren_UseCanonicalGeneratorForDuplicatesBracketsAndSlashes()
        {
            GameObject root = Track(new GameObject(_prefix + "_OpenPrefabRoot"));
            GameObject first = Track(new GameObject("Twin"));
            GameObject second = Track(new GameObject("Twin"));
            GameObject bracket = Track(new GameObject("Foo[0]"));
            GameObject slash = Track(new GameObject("A/B"));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            bracket.transform.SetParent(root.transform, false);
            slash.transform.SetParent(root.transform, false);

            MethodInfo builder = typeof(OpenPrefabContentsTool).GetMethod(
                "BuildChildrenArray",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(builder);
            JArray children = (JArray)builder.Invoke(
                new OpenPrefabContentsTool(), new object[] { root.transform });
            string[] paths = children
                .Select(child => child["path"]?.ToString())
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    GameObjectPathUtils.GetCanonicalPath(first),
                    GameObjectPathUtils.GetCanonicalPath(second),
                    GameObjectPathUtils.GetCanonicalPath(bracket),
                    GameObjectPathUtils.GetCanonicalPath(slash)
                },
                paths);
            CollectionAssert.AllItemsAreUnique(paths);
        }

        [Test]
        public void NonAmbiguousPath_RemainsByteIdentical_AndLeadingSlashResolves()
        {
            GameObject root = Track(new GameObject(_prefix + "_StableRoot"));
            GameObject child = Track(new GameObject("Panel"));
            GameObject leaf = Track(new GameObject("Button"));
            child.transform.SetParent(root.transform, false);
            leaf.transform.SetParent(child.transform, false);
            string expectedPath = root.name + "/Panel/Button";

            string canonicalPath = GameObjectPathUtils.GetCanonicalPath(leaf);
            JObject resolveError = PrefabSessionScope.TryResolveGameObject(
                null, "/" + canonicalPath, out GameObject resolved);
            JObject query = new GetGameObjectsByNameTool().Execute(
                new JObject { ["name"] = "Button" });
            JObject queryItem = ((JArray)query["gameObjects"])
                .Cast<JObject>()
                .Single(item => item["instanceId"]?.ToObject<int>() == leaf.GetInstanceID());

            Assert.AreEqual(expectedPath, canonicalPath);
            Assert.AreEqual(expectedPath, GameObjectPathUtils.GetCanonicalPath(leaf.transform));
            Assert.IsNull(resolveError);
            Assert.AreSame(leaf, resolved);
            Assert.AreEqual(leaf.GetInstanceID(), resolved.GetInstanceID());
            Assert.AreEqual(expectedPath, queryItem["path"]?.ToString());
            Assert.AreSame(leaf, child.transform.GetChild(0).gameObject);
        }

        [Test]
        public void EscapeGrammar_FirstFoo_EmitsIndexZeroAndRoundTrips()
        {
            CreateFooGrammarHierarchy(
                out GameObject root, out GameObject first, out _, out _);
            string expectedPath = root.name + "/Foo[0]";

            AssertCanonicalRoundTrip(first, expectedPath);
            Assert.AreSame(first, root.transform.GetChild(0).gameObject);
        }

        [Test]
        public void EscapeGrammar_SecondFoo_EmitsIndexOneAndRoundTrips()
        {
            CreateFooGrammarHierarchy(
                out GameObject root, out _, out GameObject second, out _);
            string expectedPath = root.name + "/Foo[1]";

            AssertCanonicalRoundTrip(second, expectedPath);
            Assert.AreSame(second, root.transform.GetChild(1).gameObject);
        }

        [Test]
        public void EscapeGrammar_LiteralFooIndexZero_EscapesBracketAndRoundTrips()
        {
            CreateFooGrammarHierarchy(
                out GameObject root, out _, out _, out GameObject literal);
            string expectedPath = root.name + "/Foo\\[0]";

            AssertCanonicalRoundTrip(literal, expectedPath);
            Assert.AreSame(literal, root.transform.GetChild(2).gameObject);
        }

        [Test]
        public void EscapeGrammar_DuplicateBackslashNames_EmitEvenSlashParityAndRoundTrip()
        {
            GameObject root = Track(new GameObject(_prefix + "_BackslashRoot"));
            GameObject first = Track(new GameObject("Foo\\"));
            GameObject second = Track(new GameObject("Foo\\"));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);

            AssertCanonicalRoundTrip(first, root.name + "/Foo\\\\[0]");
            AssertCanonicalRoundTrip(second, root.name + "/Foo\\\\[1]");
            Assert.AreEqual("Foo\\", root.transform.GetChild(0).name);
            Assert.AreEqual("Foo\\", root.transform.GetChild(1).name);
        }

        [Test]
        public void EscapeGrammar_SlashInName_IsEscapedAndRoundTrips()
        {
            GameObject root = Track(new GameObject(_prefix + "_SlashRoot"));
            GameObject child = Track(new GameObject("A/B"));
            child.transform.SetParent(root.transform, false);

            AssertCanonicalRoundTrip(child, root.name + "/A\\/B");
            Assert.AreEqual("A/B", root.transform.GetChild(0).name);
        }

        [Test]
        public void EscapeGrammar_UniqueEmptyChild_PreservesTrailingDelimiterAndRoundTrips()
        {
            GameObject root = Track(new GameObject(_prefix + "_EmptyChildRoot"));
            GameObject child = Track(new GameObject(string.Empty));
            child.transform.SetParent(root.transform, false);

            AssertCanonicalRoundTrip(child, root.name + "/");
            Assert.AreSame(child, root.transform.GetChild(0).gameObject);
        }

        [Test]
        public void EmptyNameCanonicalPath_IsAcceptedByUpdateAndCreateUiEntrypoints()
        {
            GameObject root = Track(new GameObject(_prefix + "_EmptyEntrypointRoot"));
            GameObject child = Track(new GameObject(string.Empty));
            child.transform.SetParent(root.transform, false);
            string canonicalPath = GameObjectPathUtils.GetCanonicalPath(child);

            JObject updateResult = new UpdateGameObjectTool().Execute(new JObject
            {
                ["objectPath"] = canonicalPath,
                ["gameObjectData"] = new JObject { ["activeSelf"] = false }
            });
            JObject createUiResult = new CreateUIElementTool().Execute(new JObject
            {
                ["objectPath"] = canonicalPath,
                ["elementType"] = "Panel",
                ["requireCanvas"] = false
            });

            Assert.IsTrue(updateResult["success"]?.ToObject<bool>() ?? false, updateResult.ToString());
            Assert.AreEqual(child.GetInstanceID(), updateResult["instanceId"]?.ToObject<int>());
            Assert.IsFalse(child.activeSelf);
            Assert.IsTrue(
                createUiResult["success"]?.ToObject<bool>() ?? false,
                createUiResult.ToString());
            Assert.AreEqual(child.GetInstanceID(), createUiResult["instanceId"]?.ToObject<int>());
            Assert.AreEqual(canonicalPath, createUiResult["path"]?.ToString());
        }

        [Test]
        public void EscapeGrammar_UniqueEmptyRoot_UsesEmptyCanonicalSegmentAndRoundTrips()
        {
            GameObject root = Track(new GameObject(string.Empty));

            AssertCanonicalRoundTrip(root, string.Empty);
            JObject toolResult = new GetGameObjectTool().Execute(
                new JObject { ["idOrName"] = string.Empty });
            JObject leadingSlashError = PrefabSessionScope.TryResolveGameObject(
                null, "/", out GameObject resolvedWithLeadingSlash);

            Assert.IsTrue(toolResult["success"]?.ToObject<bool>() ?? false, toolResult.ToString());
            Assert.AreEqual(root.GetInstanceID(), toolResult["instanceId"]?.ToObject<int>());
            Assert.IsNull(leadingSlashError, leadingSlashError?.ToString());
            Assert.AreSame(root, resolvedWithLeadingSlash);
        }

        [Test]
        public void EscapeGrammar_LeadingBracketName_RemainsUnescaped()
        {
            GameObject root = Track(new GameObject(_prefix + "_BracketRoot"));
            GameObject cameras = Track(new GameObject("[Cameras]"));
            cameras.transform.SetParent(root.transform, false);

            AssertCanonicalRoundTrip(cameras, root.name + "/[Cameras]");
            Assert.AreSame(cameras, root.transform.GetChild(0).gameObject);
        }

        [Test]
        public void LegacyLiteralTrailingIndex_FailsLoudAndTeachesEscapedSpelling()
        {
            GameObject root = Track(new GameObject(_prefix + "_LegacyRoot"));
            GameObject literal = Track(new GameObject("Foo[0]"));
            literal.transform.SetParent(root.transform, false);

            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, root.name + "/Foo[0]", out GameObject resolved);

            Assert.IsNull(resolved);
            Assert.AreEqual("not_found_error", error["error"]?["type"]?.ToString());
            Assert.That(error["error"]?["message"]?.ToString(), Does.Contain("same-name index 0"));
            Assert.That(error["error"]?["message"]?.ToString(), Does.Contain("Foo\\[0]"));
            Assert.AreSame(literal, root.transform.GetChild(0).gameObject);
        }

        [Test]
        public void PrefabEditingFindByPath_AmbiguityPreservesNullablePublicContract()
        {
            string rootName = _prefix + "_FindByPathRoot";
            GameObject previewRoot = OpenTestPrefab(rootName);
            GameObject first = new GameObject("Twin");
            GameObject second = new GameObject("Twin");
            first.transform.SetParent(previewRoot.transform, false);
            second.transform.SetParent(previewRoot.transform, false);

            GameObject resolved = PrefabEditingService.FindByPath(rootName + "/Twin");

            Assert.IsNull(resolved);
            Assert.AreSame(first, previewRoot.transform.GetChild(0).gameObject);
            Assert.AreSame(second, previewRoot.transform.GetChild(1).gameObject);
        }

        [Test]
        public void ToolCallGraph_DirectlyUsesAmbiguityAwareWrappersAndHierarchyCreator()
        {
            MethodInfo getInteractablesExecute = typeof(GetInteractableElementsTool).GetMethod(
                "Execute", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getInteractablesResolver = GetPrivateStaticMethod(
                typeof(GetInteractableElementsTool), "TryResolveRootTransform");
            AssertDirectCall(getInteractablesExecute, getInteractablesResolver);

            MethodInfo waitCoroutine = GetPrivateInstanceMethod(
                typeof(WaitForConditionTool), "ExecuteCoroutine");
            MethodInfo waitMoveNext = GetIteratorMoveNext(waitCoroutine);
            MethodInfo checkCondition = GetPrivateStaticMethod(
                typeof(WaitForConditionTool), "CheckCondition");
            MethodInfo conditionResolver = GetPrivateStaticMethod(
                typeof(WaitForConditionTool), "TryResolveConditionTarget");
            MethodInfo finalStateResolver = GetPrivateStaticMethod(
                typeof(WaitForConditionTool), "TryResolveFinalStateTarget");
            AssertDirectCall(waitMoveNext, checkCondition);
            AssertDirectCall(checkCondition, conditionResolver);
            AssertDirectCall(
                conditionResolver,
                typeof(PrefabSessionScope).GetMethod(
                    "TryResolveGameObjectForPolling",
                    BindingFlags.Public | BindingFlags.Static));
            AssertDirectCall(waitMoveNext, finalStateResolver);

            MethodInfo dragCoroutine = GetPrivateInstanceMethod(
                typeof(SimulateDragTool), "ExecuteCoroutine");
            MethodInfo dragMoveNext = GetIteratorMoveNext(dragCoroutine);
            AssertDirectCall(
                dragMoveNext,
                GetPrivateStaticMethod(typeof(SimulateDragTool), "TryResolveDragTarget"));
            AssertDirectCall(
                dragMoveNext,
                GetPrivateStaticMethod(typeof(SimulateDragTool), "TryResolveDropTarget"));

            MethodInfo createUiExecute = typeof(CreateUIElementTool).GetMethod(
                "Execute", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo hierarchyCreator = typeof(GameObjectHierarchyCreator).GetMethod(
                "TryFindOrCreateHierarchicalGameObject",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(hierarchyCreator);
            AssertDirectCall(createUiExecute, hierarchyCreator);
        }

        private void CreateDuplicateHierarchy(
            out GameObject root,
            out GameObject first,
            out GameObject second)
        {
            root = Track(new GameObject(_prefix + "_Root"));
            first = Track(new GameObject("Twin"));
            second = Track(new GameObject("Twin"));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
        }

        private void CreateFooGrammarHierarchy(
            out GameObject root,
            out GameObject first,
            out GameObject second,
            out GameObject literal)
        {
            root = Track(new GameObject(_prefix + "_FooRoot"));
            first = Track(new GameObject("Foo"));
            second = Track(new GameObject("Foo"));
            literal = Track(new GameObject("Foo[0]"));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            literal.transform.SetParent(root.transform, false);
        }

        private static bool InvokeWaitCheckCondition(
            string objectPath,
            string condition,
            out JObject resolutionError)
        {
            MethodInfo method = GetPrivateStaticMethod(
                typeof(WaitForConditionTool), "CheckCondition");
            object[] arguments = { objectPath, condition, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            resolutionError = arguments[3] as JObject;
            return result;
        }

        private static MethodInfo GetPrivateStaticMethod(Type declaringType, string methodName)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(
                method,
                $"Expected private static method '{declaringType.FullName}.{methodName}'.");
            return method;
        }

        private static MethodInfo GetPrivateInstanceMethod(Type declaringType, string methodName)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(
                method,
                $"Expected private instance method '{declaringType.FullName}.{methodName}'.");
            return method;
        }

        private static MethodInfo GetIteratorMoveNext(MethodInfo iteratorMethod)
        {
            IteratorStateMachineAttribute attribute =
                iteratorMethod.GetCustomAttribute<IteratorStateMachineAttribute>();
            Assert.IsNotNull(
                attribute,
                $"Expected '{iteratorMethod.DeclaringType?.FullName}.{iteratorMethod.Name}' " +
                "to compile as an iterator state machine.");
            MethodInfo moveNext = attribute.StateMachineType.GetMethod(
                "MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(moveNext, "Iterator state machine has no MoveNext method.");
            return moveNext;
        }

        private static void AssertDirectCall(MethodBase caller, MethodInfo expectedCallee)
        {
            Assert.IsNotNull(caller);
            Assert.IsNotNull(expectedCallee);
            byte[] il = caller.GetMethodBody()?.GetILAsByteArray();
            Assert.IsNotNull(
                il,
                $"Method '{caller.DeclaringType?.FullName}.{caller.Name}' has no readable IL body.");
            Assert.AreEqual(
                caller.Module,
                expectedCallee.Module,
                "Direct-call assertion requires caller and callee to share one module.");

            // Supplemental wiring guard only: this deliberately small scan does not fully decode
            // IL, so operand bytes can resemble opcodes; it also cannot prove runtime reachability.
            for (int i = 0; i + sizeof(int) < il.Length; i++)
            {
                if ((il[i] == 0x28 || il[i] == 0x6f)
                    && BitConverter.ToInt32(il, i + 1) == expectedCallee.MetadataToken)
                {
                    return;
                }
            }

            Assert.Fail(
                $"Supplemental IL wiring check expected " +
                $"'{caller.DeclaringType?.FullName}.{caller.Name}' to directly call/callvirt " +
                $"'{expectedCallee.DeclaringType?.FullName}.{expectedCallee.Name}'. This guard " +
                "does not prove runtime reachability.");
        }

        private GameObject OpenTestPrefab(string rootName)
        {
            EnsureAssetFolder();
            string prefabPath = _assetFolder + "/" + rootName + ".prefab";
            GameObject source = new GameObject(rootName);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, prefabPath, out bool success);
                Assert.IsTrue(success, "Test Prefab creation must succeed.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            GameObject root = PrefabEditingService.Open(prefabPath);
            _openedPrefabSession = true;
            return root;
        }

        private Scene CreateScene(string sceneName)
        {
            // UTF runs EditMode tests with an untitled active scene (measured: path == "",
            // isDirty == false), NewScene(Additive) rejects creation whenever ANY untitled
            // scene exists, and SceneManager.CreateScene is Play-Mode-only. Proven shape
            // (PrefabSessionScopeTests S6, same assembly, shipped green): save a COPY of the
            // runner scene — saveAsCopy:true leaves the runner scene untouched — into this
            // class's constant folder, then OpenScene(Additive), which has no untitled guard.
            // The copy carries the runner scene's bootstrap roots (Main Camera / Directional
            // Light); tests only assert on roots they create themselves.
            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                Assert.IsFalse(string.IsNullOrEmpty(
                    AssetDatabase.CreateFolder("Assets", SceneFolderName)));
                AssetDatabase.Refresh();
            }

            string scenePath = SceneFolder + "/" + sceneName + ".unity";
            Scene runner = SceneManager.GetActiveScene();
            Assert.IsTrue(
                EditorSceneManager.SaveScene(runner, scenePath, true),
                $"Failed to save a runner-scene copy to '{scenePath}'.");
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            _createdScenes.Add(scene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded);
            Assert.AreEqual(sceneName, scene.name);
            // The copy carries whatever the runner scene held (bootstrap objects, or an entire
            // level when a consumer runs these tests with a content scene open). Tests need an
            // EMPTY container, so clear the copied roots immediately — this also removes the
            // duplicated bootstrap roots (e.g. three same-name "Main Camera" across scenes).
            foreach (GameObject copiedRoot in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(copiedRoot);
            Assert.AreEqual(0, scene.rootCount);
            return scene;
        }

        private static bool CloseScenesUnder(string folder)
        {
            bool allClosed = true;
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.path.StartsWith(folder + "/", StringComparison.Ordinal))
                    allClosed &= EditorSceneManager.CloseScene(scene, true);
            }
            return allClosed;
        }

        private void EnsureAssetFolder()
        {
            if (!string.IsNullOrEmpty(_assetFolder))
                return;
            _assetFolder = "Assets/" + _prefix;
            string folderGuid = AssetDatabase.CreateFolder("Assets", _prefix);
            Assert.IsFalse(string.IsNullOrEmpty(folderGuid));
            // SaveScene silently returns false when targeting a folder that has not been
            // imported yet (measured); flush the new folder before anyone saves into it.
            AssetDatabase.Refresh();
        }

        private GameObject Track(GameObject gameObject)
        {
            _createdObjects.Add(gameObject);
            return gameObject;
        }

        private static JObject InvokePrivatePathResolver(
            Type declaringType,
            string methodName,
            string path,
            out object resolved)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(
                method,
                $"Expected private resolver '{declaringType.FullName}.{methodName}' was not found.");
            object[] arguments = { path, null };
            JObject result = (JObject)method.Invoke(null, arguments);
            resolved = arguments[1];
            return result;
        }

        private static void AssertCanonicalRoundTrip(GameObject expected, string expectedPath)
        {
            string canonicalPath = GameObjectPathUtils.GetCanonicalPath(expected);
            JObject error = PrefabSessionScope.TryResolveGameObject(
                null, canonicalPath, out GameObject resolved);

            Assert.AreEqual(expectedPath, canonicalPath);
            Assert.IsNull(error, error?.ToString());
            Assert.AreSame(expected, resolved);
            Assert.AreEqual(expected.GetInstanceID(), resolved.GetInstanceID());
        }

        private static void AssertAmbiguity(
            JObject error,
            string requestedPath,
            params GameObject[] expectedCandidates)
        {
            Assert.IsNotNull(error);
            Assert.AreEqual(
                PrefabSessionScope.ObjectPathAmbiguityErrorType,
                error["error"]?["type"]?.ToString());
            Assert.AreEqual(
                requestedPath,
                error["error"]?["details"]?["objectPath"]?.ToString());
            Assert.AreEqual(
                expectedCandidates.Length,
                error["error"]?["details"]?["candidateCount"]?.ToObject<int>());

            JArray candidates = error["error"]?["details"]?["candidates"] as JArray;
            Assert.IsNotNull(candidates);
            Assert.AreEqual(expectedCandidates.Length, candidates.Count);
            Assert.That(
                error["error"]?["message"]?.ToString(),
                Does.Contain(expectedCandidates.Length + " candidates"));

            int[] expectedIds = expectedCandidates
                .Select(candidate => candidate.GetInstanceID())
                .ToArray();
            int[] actualIds = candidates
                .Select(candidate => candidate["instanceId"].ToObject<int>())
                .ToArray();
            CollectionAssert.AreEqual(expectedIds, actualIds);

            foreach (GameObject expectedCandidate in expectedCandidates)
            {
                int instanceId = expectedCandidate.GetInstanceID();
                JObject detail = candidates
                    .Cast<JObject>()
                    .Single(candidate => candidate["instanceId"].ToObject<int>() == instanceId);
                Assert.AreEqual(
                    GameObjectPathUtils.GetCanonicalPath(expectedCandidate),
                    detail["path"]?.ToString());
                string expectedSceneName = string.IsNullOrEmpty(expectedCandidate.scene.name)
                    ? "<unnamed>"
                    : expectedCandidate.scene.name;
                Assert.AreEqual(expectedSceneName, detail["scene"]?.ToString());
                Assert.That(
                    error["error"]?["message"]?.ToString(),
                    Does.Contain("instanceId=" + instanceId));
            }
        }
    }
}
