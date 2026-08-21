using System.Collections.Generic;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public class DuplicateGameObjectToolTests
    {
        private const string ObjectPrefix = "DuplicateLocalTransformT_";
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                    Object.DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();
        }

        [Test]
        public void Execute_ScaledParentPreservesEveryLocalTransformComponent()
        {
            GameObject parent = Spawn(ObjectPrefix + "Parent");
            parent.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            GameObject source = CreateSource(parent, ObjectPrefix + "Source");

            JObject result = new DuplicateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["newName"] = ObjectPrefix + "Copy"
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            GameObject duplicate = ResolveDuplicate(result);
            Assert.AreSame(parent.transform, duplicate.transform.parent);
            AssertLocalTransformComponentsEqual(source.transform, duplicate.transform);
        }

        [Test]
        public void Execute_ExplicitNewParentAlsoPreservesEveryLocalTransformComponent()
        {
            GameObject sourceParent = Spawn(ObjectPrefix + "SourceParent");
            sourceParent.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            GameObject targetParent = Spawn(ObjectPrefix + "TargetParent");
            targetParent.transform.localScale = new Vector3(0.25f, 0.75f, 1.5f);
            GameObject source = CreateSource(sourceParent, ObjectPrefix + "ReparentSource");

            JObject result = new DuplicateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["newName"] = ObjectPrefix + "ReparentedCopy",
                ["newParent"] = McpUnity.Utils.GameObjectPathUtils.GetCanonicalPath(targetParent)
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            GameObject duplicate = ResolveDuplicate(result);
            Assert.AreSame(targetParent.transform, duplicate.transform.parent);
            AssertLocalTransformComponentsEqual(source.transform, duplicate.transform);
        }

        [Test]
        public void Execute_ExplicitNewParentWithWorldPositionStaysPreservesInstantiateWorldPosition()
        {
            GameObject source = Spawn(ObjectPrefix + "WorldPoseSource");
            source.transform.localPosition = new Vector3(13.25f, -7.5f, 2.75f);
            source.transform.localRotation = Quaternion.Euler(17f, 29f, 43f);
            source.transform.localScale = new Vector3(1.25f, 0.8f, 2.5f);
            Vector3 instantiateWorldPosition = source.transform.localPosition;

            GameObject targetParent = Spawn(ObjectPrefix + "OffsetTargetParent");
            targetParent.transform.position = new Vector3(100f, -50f, 25f);
            targetParent.transform.rotation = Quaternion.Euler(8f, 16f, 32f);
            targetParent.transform.localScale = new Vector3(2f, 3f, 4f);

            JObject result = new DuplicateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["newName"] = ObjectPrefix + "WorldPoseCopy",
                ["newParent"] = McpUnity.Utils.GameObjectPathUtils.GetCanonicalPath(targetParent),
                ["worldPositionStays"] = true
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            GameObject duplicate = ResolveDuplicate(result);
            Assert.AreSame(targetParent.transform, duplicate.transform.parent);
            Assert.That(Vector3.Distance(instantiateWorldPosition, duplicate.transform.position),
                Is.LessThan(0.0001f));
        }

        private GameObject CreateSource(GameObject parent, string name)
        {
            GameObject source = Spawn(name);
            source.transform.SetParent(parent.transform, false);
            source.transform.localPosition = new Vector3(13.25f, -7.5f, 2.75f);
            source.transform.localRotation = Quaternion.Euler(17f, 29f, 43f);
            source.transform.localScale = new Vector3(1.25f, 0.8f, 2.5f);
            return source;
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }

        private GameObject ResolveDuplicate(JObject result)
        {
            int duplicateId = result["duplicatedObjects"]?[0]?["instanceId"]?.ToObject<int>() ?? 0;
            var duplicate = EditorUtility.InstanceIDToObject(duplicateId) as GameObject;
            Assert.IsNotNull(duplicate, result.ToString());
            _spawned.Add(duplicate);
            return duplicate;
        }

        private static void AssertLocalTransformComponentsEqual(
            Transform expected,
            Transform actual)
        {
            Assert.AreEqual(expected.localPosition.x, actual.localPosition.x);
            Assert.AreEqual(expected.localPosition.y, actual.localPosition.y);
            Assert.AreEqual(expected.localPosition.z, actual.localPosition.z);
            Assert.AreEqual(expected.localRotation.x, actual.localRotation.x);
            Assert.AreEqual(expected.localRotation.y, actual.localRotation.y);
            Assert.AreEqual(expected.localRotation.z, actual.localRotation.z);
            Assert.AreEqual(expected.localRotation.w, actual.localRotation.w);
            Assert.AreEqual(expected.localScale.x, actual.localScale.x);
            Assert.AreEqual(expected.localScale.y, actual.localScale.y);
            Assert.AreEqual(expected.localScale.z, actual.localScale.z);
        }
    }
}
