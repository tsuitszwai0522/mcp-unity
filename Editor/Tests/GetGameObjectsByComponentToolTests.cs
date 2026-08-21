using System.Collections.Generic;
using System.Linq;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public class GetGameObjectsByComponentProbe : MonoBehaviour
    {
    }

    public class GetGameObjectsByComponentToolTests
    {
        private const string ObjectPrefix = "GgbcT_";
        private const string TestPrefabDirectory = "Assets/McpUnityGetGameObjectsByComponentTests";
        private const string TestPrefabPath = TestPrefabDirectory + "/ColliderScope.prefab";
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GetGameObjectsByComponentTool _tool;
        private bool _ownsFixtureState;
        private bool _ownsPrefabSession;

        [SetUp]
        public void SetUp()
        {
            _tool = new GetGameObjectsByComponentTool();
            _spawned.Clear();
            _ownsFixtureState = false;
            _ownsPrefabSession = false;

            PrefabEditingSessionStatus existingStatus = PrefabEditingService.Status;
            if (existingStatus != PrefabEditingSessionStatus.None)
            {
                Assert.Ignore("Existing Prefab session; not claimed by this fixture.");
            }

            _ownsFixtureState = true;
            if (!AssetDatabase.IsValidFolder(TestPrefabDirectory))
                AssetDatabase.CreateFolder("Assets", "McpUnityGetGameObjectsByComponentTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (!_ownsFixtureState)
                return;

            try
            {
                if (_ownsPrefabSession
                    && PrefabEditingService.Status != PrefabEditingSessionStatus.None)
                {
                    PrefabEditingService.Discard();
                }
            }
            finally
            {
                foreach (GameObject gameObject in _spawned)
                {
                    if (gameObject != null)
                        Object.DestroyImmediate(gameObject);
                }
                _spawned.Clear();

                AssetDatabase.DeleteAsset(TestPrefabPath);
                if (AssetDatabase.IsValidFolder(TestPrefabDirectory))
                    AssetDatabase.DeleteAsset(TestPrefabDirectory);
                AssetDatabase.Refresh();
                _ownsPrefabSession = false;
                _ownsFixtureState = false;
            }
        }

        [Test]
        public void Execute_ExactTypeIncludesInactiveByDefaultAndCanExcludeIt()
        {
            GameObject active = Spawn(ObjectPrefix + "ExactActive");
            active.AddComponent<GetGameObjectsByComponentProbe>();
            GameObject inactive = Spawn(ObjectPrefix + "ExactInactive");
            inactive.AddComponent<GetGameObjectsByComponentProbe>();
            inactive.SetActive(false);

            JObject included = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName
            });

            Assert.IsTrue(included["success"]?.ToObject<bool>() ?? false, included.ToString());
            Assert.AreEqual(typeof(GetGameObjectsByComponentProbe).FullName,
                included["componentType"]?.ToString());
            Assert.AreEqual(2, included["count"]?.ToObject<int>());
            Assert.That(ResultIds(included), Contains.Item(active.GetInstanceID()));
            Assert.That(ResultIds(included), Contains.Item(inactive.GetInstanceID()));

            JObject excluded = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName,
                ["includeInactive"] = false
            });

            Assert.IsTrue(excluded["success"]?.ToObject<bool>() ?? false, excluded.ToString());
            Assert.AreEqual(1, excluded["count"]?.ToObject<int>());
            Assert.That(ResultIds(excluded), Contains.Item(active.GetInstanceID()));
            Assert.That(ResultIds(excluded), Has.No.Member(inactive.GetInstanceID()));
        }

        [Test]
        public void Execute_BaseTypeIncludesDerivedComponent()
        {
            GameObject target = Spawn(ObjectPrefix + "DerivedCollider");
            target.AddComponent<BoxCollider>();

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(Collider).FullName
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(typeof(Collider).FullName, result["componentType"]?.ToString());
            Assert.That(ResultIds(result), Contains.Item(target.GetInstanceID()));
        }

        [Test]
        public void Execute_AmbiguousShortTypeReturnsHouseError()
        {
            string ambiguousShortName =
                typeof(ContractAlpha.Partial.AmbiguousContractComponent).Name;

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = ambiguousShortName
            });

            Assert.AreEqual("component_ambiguity_error",
                result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("fully-qualified"));
        }

        [Test]
        public void Execute_LimitReportsHonestTotalAndTruncation()
        {
            var spawnedIds = new HashSet<int>();
            for (int i = 0; i < 3; i++)
            {
                GameObject spawned = Spawn(ObjectPrefix + "Truncate_" + i);
                spawned.AddComponent<GetGameObjectsByComponentProbe>();
                spawnedIds.Add(spawned.GetInstanceID());
            }

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName,
                ["limit"] = 2
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(2, result["count"]?.ToObject<int>());
            Assert.AreEqual(3, result["total"]?.ToObject<int>());
            Assert.Greater(result["total"]?.ToObject<int>() ?? 0,
                result["count"]?.ToObject<int>() ?? 0);
            Assert.IsTrue(result["truncated"]?.ToObject<bool>() ?? false);
            Assert.That(result["message"]?.ToString(), Does.Contain("of"));
            Assert.That(ResultIds(result), Is.SubsetOf(spawnedIds));
        }

        [Test]
        public void Execute_CompactDefaultsTrueAndReturnsTypeAndEnabledOnly()
        {
            GameObject target = Spawn(ObjectPrefix + "CompactDefault");
            target.AddComponent<GetGameObjectsByComponentProbe>();

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName
            });

            JObject match = FindResult(result, target);
            Assert.IsNotNull(match, result.ToString());
            foreach (JObject component in match["components"].Children<JObject>())
            {
                Assert.AreEqual(2, component.Properties().Count());
                Assert.IsNotNull(component["type"]);
                Assert.IsNotNull(component["enabled"]);
                Assert.IsNull(component["properties"]);
            }
        }

        [Test]
        public void Execute_ComponentFilterWithoutCompactReturnsOnlyFilteredProperties()
        {
            GameObject target = Spawn(ObjectPrefix + "ImplicitDetailedFilter");
            target.AddComponent<GetGameObjectsByComponentProbe>();
            target.AddComponent<BoxCollider>();

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName,
                ["componentFilter"] = new JArray(nameof(BoxCollider))
            });

            JObject match = FindResult(result, target);
            Assert.IsNotNull(match, result.ToString());

            JObject filtered = match["components"]
                .Children<JObject>()
                .Single(component => component["type"]?.ToString() == nameof(BoxCollider));
            Assert.IsNotNull(filtered["properties"]);

            foreach (JObject component in match["components"].Children<JObject>()
                .Where(component => component != filtered))
            {
                Assert.AreEqual(2, component.Properties().Count());
                Assert.IsNotNull(component["type"]);
                Assert.IsNotNull(component["enabled"]);
                Assert.IsNull(component["properties"]);
            }
        }

        [Test]
        public void Execute_ComponentFilterAcceptsFullTypeName()
        {
            GameObject target = Spawn(ObjectPrefix + "FullNameFilter");
            target.AddComponent<GetGameObjectsByComponentProbe>();
            target.AddComponent<BoxCollider>();

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName,
                ["componentFilter"] = new JArray(typeof(BoxCollider).FullName)
            });

            JObject match = FindResult(result, target);
            Assert.IsNotNull(match, result.ToString());
            JObject filtered = match["components"]
                .Children<JObject>()
                .Single(component => component["type"]?.ToString() == nameof(BoxCollider));
            Assert.IsNotNull(filtered["properties"]);
        }

        [Test]
        public void Execute_PrefabSessionScopesTraversalAndPrunesInactiveBranches()
        {
            GameObject prefabRoot = Spawn(ObjectPrefix + "PrefabRoot");
            GameObject activePrefabCollider = Spawn(ObjectPrefix + "ActivePrefabCollider");
            activePrefabCollider.transform.SetParent(prefabRoot.transform, false);
            activePrefabCollider.AddComponent<BoxCollider>();
            GameObject inactivePrefabCollider = Spawn(ObjectPrefix + "InactivePrefabCollider");
            inactivePrefabCollider.transform.SetParent(prefabRoot.transform, false);
            inactivePrefabCollider.AddComponent<BoxCollider>();
            inactivePrefabCollider.SetActive(false);

            GameObject sceneCollider = Spawn(ObjectPrefix + "SceneCollider");
            sceneCollider.AddComponent<BoxCollider>();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, TestPrefabPath);
            GameObject previewRoot = PrefabEditingService.Open(TestPrefabPath);
            _ownsPrefabSession = true;
            GameObject previewActive = previewRoot.transform
                .Find(activePrefabCollider.name).gameObject;
            GameObject previewInactive = previewRoot.transform
                .Find(inactivePrefabCollider.name).gameObject;

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(Collider).FullName,
                ["includeInactive"] = false
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            int[] resultIds = ResultIds(result);
            CollectionAssert.AreEqual(new[] { previewActive.GetInstanceID() }, resultIds);
            Assert.That(resultIds, Has.No.Member(previewInactive.GetInstanceID()));
            Assert.That(resultIds, Has.No.Member(sceneCollider.GetInstanceID()));
            JObject match = FindResult(result, previewActive);
            Assert.AreEqual(GameObjectPathUtils.GetCanonicalPath(previewActive),
                match["path"]?.ToString());
            Assert.That(match["path"]?.ToString(),
                Does.StartWith(previewRoot.name + "/"));
        }

        [Test]
        public void Execute_PathComesFromCanonicalGenerator()
        {
            GameObject parent = Spawn(ObjectPrefix + "CanonicalParent");
            GameObject target = Spawn(ObjectPrefix + "CanonicalChild");
            target.transform.SetParent(parent.transform, false);
            target.AddComponent<GetGameObjectsByComponentProbe>();

            JObject result = _tool.Execute(new JObject
            {
                ["componentType"] = typeof(GetGameObjectsByComponentProbe).FullName
            });

            JObject match = FindResult(result, target);
            Assert.IsNotNull(match, result.ToString());
            Assert.AreEqual(GameObjectPathUtils.GetCanonicalPath(target),
                match["path"]?.ToString());
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }

        private static int[] ResultIds(JObject result)
        {
            return ((JArray)result["gameObjects"])
                .Select(item => item["instanceId"].ToObject<int>())
                .ToArray();
        }

        private static JObject FindResult(JObject result, GameObject target)
        {
            return ((JArray)result["gameObjects"])
                .Children<JObject>()
                .SingleOrDefault(item =>
                    item["instanceId"]?.ToObject<int>() == target.GetInstanceID());
        }
    }
}
