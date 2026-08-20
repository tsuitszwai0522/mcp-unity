using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using AlphaAmbiguousComponent = McpUnity.Tests.ContractAlpha.Partial.AmbiguousContractComponent;
using ComponentResolver = McpUnity.Utils.ComponentTypeResolver;
using DerivedNarrowingComponent = McpUnity.Tests.InheritedDerived.InheritanceNarrowingComponent;
using NamespacedPriorityComponent = McpUnity.Tests.ContractNamespaced.GlobalPriorityContractComponent;

namespace McpUnity.Tests
{
    public class WriteContractPromotionTests
    {
        private const string TestRoot = "Assets/McpUnityWriteContractTests";
        private const string PackablesFolder = TestRoot + "/Sprites";
        private const string PrefabPath = TestRoot + "/PositionProbe.prefab";
        private const string AtlasPath = TestRoot + "/ContractAtlas.spriteatlas";
        private const string ReadbackAtlasPath = TestRoot + "/ReadbackAtlas.spriteatlas";
        private const string MismatchedAtlasPath = TestRoot + "/PathDerivedName.spriteatlas";
        private const string ObjectPrefix = "McpWriteContract_";

        // Preserve the delegates compiled into production before any test injects replacements.
        // Teardown must restore these captured values, not manufacture a known-good implementation.
        private static readonly Func<SpriteAtlas, bool> OriginalReadIncludeInBuild;
        private static readonly Func<SpriteAtlas, SpriteAtlasPackingSettings> OriginalReadPackingSettings;

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        static WriteContractPromotionTests()
        {
            OriginalReadIncludeInBuild =
                GetCreateSpriteAtlasToolField<Func<SpriteAtlas, bool>>("_readIncludeInBuild");
            OriginalReadPackingSettings =
                GetCreateSpriteAtlasToolField<Func<SpriteAtlas, SpriteAtlasPackingSettings>>(
                    "_readPackingSettings");
        }

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.CreateFolder("Assets", "McpUnityWriteContractTests");
            }
            AssetDatabase.CreateFolder(TestRoot, "Sprites");
        }

        [TearDown]
        public void TearDown()
        {
            RestoreCreateSpriteAtlasReadback();

            foreach (GameObject gameObject in _createdObjects.Where(item => item != null))
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            _createdObjects.Clear();

            foreach (GameObject gameObject in UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject != null
                    && !EditorUtility.IsPersistent(gameObject)
                    && gameObject.name.StartsWith(ObjectPrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void R1_2_UpdateGameObject_EmptyAndNullNames_AreFieldFailures()
        {
            JToken[] invalidNames = { new JValue(string.Empty), JValue.CreateNull() };

            foreach (JToken invalidName in invalidNames)
            {
                GameObject target = Track(new GameObject(ObjectPrefix + "InvalidNameTarget"));
                string originalName = target.name;

                JObject result = new UpdateGameObjectTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["gameObjectData"] = new JObject { ["name"] = invalidName }
                });

                Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
                CollectionAssert.DoesNotContain(
                    result["updatedFields"]?.ToObject<string[]>(), "name");
                Assert.IsNotNull(FindFailure((JArray)result["failedFields"], "name"));
                Assert.AreEqual(originalName, target.name);
            }
        }

        [Test]
        public void R1_2_UpdateGameObject_EveryKnownAliasAndUnknownKey_IsAccountedFor()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "AllKeysTarget"));

            JObject result = new UpdateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["gameObjectData"] = new JObject
                {
                    ["layer"] = JValue.CreateNull(),
                    ["activeSelf"] = true,
                    ["isActiveSelf"] = false,
                    ["isStatic"] = JValue.CreateNull(),
                    ["static"] = true,
                    ["mysteryField"] = 123
                }
            });

            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
            CollectionAssert.AreEquivalent(
                new[] { "activeSelf" },
                result["updatedFields"]?.ToObject<string[]>());
            CollectionAssert.AreEquivalent(
                new[] { "layer", "isActiveSelf", "isStatic", "static", "mysteryField" },
                ((JArray)result["failedFields"])
                    .OfType<JObject>()
                    .Select(failure => failure["field"]?.ToString())
                    .ToArray());
        }

        [Test]
        public void R1_UpdateGameObject_InvalidTagAndLayer_ReturnsFieldFailuresAndStructuredWarning()
        {
            GameObject canvasParent = Track(new GameObject(ObjectPrefix + "Canvas", typeof(Canvas)));
            GameObject target = Track(new GameObject(ObjectPrefix + "Target"));
            target.transform.SetParent(canvasParent.transform, false);
            int originalLayer = target.layer;
            string originalTag = target.tag;
            string missingTag = ObjectPrefix + Guid.NewGuid().ToString("N");
            string updatedName = ObjectPrefix + "RenamedTarget";

            JObject result = new UpdateGameObjectTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["gameObjectData"] = new JObject
                {
                    ["tag"] = missingTag,
                    ["layer"] = 32,
                    ["name"] = updatedName
                }
            });

            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
            CollectionAssert.AreEquivalent(
                new[] { "name" },
                result["updatedFields"]?.ToObject<string[]>());
            JArray failedFields = (JArray)result["failedFields"];
            Assert.AreEqual(2, failedFields.Count);
            Assert.That(FindFailure(failedFields, "tag")?["reason"]?.ToString(),
                Does.Contain(missingTag));
            Assert.That(FindFailure(failedFields, "layer")?["reason"]?.ToString(),
                Does.Contain("0-31"));
            Assert.That(result["warnings"]?.ToObject<string[]>(),
                Has.Some.Contains("no RectTransform"));
            Assert.AreEqual(originalTag, target.tag);
            Assert.AreEqual(originalLayer, target.layer);
            Assert.AreEqual(updatedName, target.name);
            Assert.AreEqual(updatedName, result["name"]?.ToString());
        }

        [Test]
        public void R2_AddAsset_DefaultWorldPosition_RemainsWorldAfterParentingAndReadsBackBothSpaces()
        {
            CreatePrefabAsset();
            GameObject parent = Track(new GameObject(ObjectPrefix + "WorldParent"));
            parent.transform.position = new Vector3(10f, 0f, 0f);
            Vector3 requestedWorldPosition = new Vector3(2f, 3f, 4f);

            JObject result = new AddAssetToSceneTool().Execute(new JObject
            {
                ["assetPath"] = PrefabPath,
                ["parentId"] = parent.GetInstanceID(),
                ["position"] = ToJObject(requestedWorldPosition)
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            GameObject instance = Track(EditorUtility.InstanceIDToObject(
                result["instanceId"].ToObject<int>()) as GameObject);
            Assert.IsNotNull(instance);
            Assert.AreSame(parent.transform, instance.transform.parent);
            AssertVector(instance.transform.position, requestedWorldPosition);
            AssertVector(instance.transform.localPosition, new Vector3(-8f, 3f, 4f));
            AssertVector(result["worldPosition"], instance.transform.position);
            AssertVector(result["localPosition"], instance.transform.localPosition);
        }

        [Test]
        public void R2_AddAsset_LocalPosition_IsRelativeToParentAndReadsBackFinalWorldPosition()
        {
            CreatePrefabAsset();
            GameObject parent = Track(new GameObject(ObjectPrefix + "LocalParent"));
            parent.transform.position = new Vector3(10f, 0f, 0f);
            Vector3 requestedLocalPosition = new Vector3(1f, 2f, 3f);

            JObject result = new AddAssetToSceneTool().Execute(new JObject
            {
                ["assetPath"] = PrefabPath,
                ["parentId"] = parent.GetInstanceID(),
                ["positionSpace"] = "local",
                ["position"] = ToJObject(requestedLocalPosition)
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            GameObject instance = Track(EditorUtility.InstanceIDToObject(
                result["instanceId"].ToObject<int>()) as GameObject);
            Assert.IsNotNull(instance);
            AssertVector(instance.transform.localPosition, requestedLocalPosition);
            AssertVector(instance.transform.position, new Vector3(11f, 2f, 3f));
            AssertVector(result["worldPosition"], instance.transform.position);
            AssertVector(result["localPosition"], instance.transform.localPosition);
        }

        [Test]
        public void R3_ComponentTools_AmbiguousShortOrPartialNames_FailWithEveryCandidate()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "AmbiguousTarget"));
            string shortName = typeof(AlphaAmbiguousComponent).Name;
            const string partialName = "Partial.AmbiguousContractComponent";

            JObject[] results =
            {
                new UpdateComponentTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["componentName"] = shortName
                }),
                new RemoveComponentTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["componentName"] = shortName
                }),
                new ReadSerializedFieldsTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["componentName"] = partialName
                }),
                new WriteSerializedFieldsTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["componentName"] = partialName,
                    ["fieldData"] = new JObject { ["value"] = 1 }
                })
            };

            foreach (JObject result in results)
            {
                Assert.AreEqual("component_ambiguity_error", result["error"]?["type"]?.ToString());
                string message = result["error"]?["message"]?.ToString();
                Assert.That(message, Does.Contain(
                    "McpUnity.Tests.ContractAlpha.Partial.AmbiguousContractComponent"));
                Assert.That(message, Does.Contain(
                    "McpUnity.Tests.ContractBeta.Partial.AmbiguousContractComponent"));
                Assert.That(message, Does.Contain("fully-qualified"));
            }
        }

        [Test]
        public void R3_UpdateComponent_OnlyAttachedAmbiguousCandidate_IsSelectedWithWarning()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "NarrowedTarget"));
            AlphaAmbiguousComponent component = target.AddComponent<AlphaAmbiguousComponent>();

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = typeof(AlphaAmbiguousComponent).Name,
                ["componentData"] = new JObject { ["value"] = 17 }
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.AreEqual(17, component.value);
            Assert.That(result["warnings"]?.ToObject<string[]>(),
                Has.Some.Contains(typeof(AlphaAmbiguousComponent).FullName));
            Assert.That(result["warnings"]?.ToObject<string[]>(),
                Has.Some.Contains("ContractBeta"));
        }

        [Test]
        public void R1_1_UpdateComponent_InheritedShortName_SelectsOnlyExactAttachedType()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "InheritedNarrowingTarget"));
            target.AddComponent<DerivedNarrowingComponent>();

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = typeof(DerivedNarrowingComponent).Name
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.That(result["warnings"]?.ToObject<string[]>(),
                Has.Some.Contains(typeof(DerivedNarrowingComponent).FullName));
            Assert.That(result["warnings"]?.ToObject<string[]>(),
                Has.Some.Contains("only exact candidate type"));
        }

        [Test]
        public void R3_UpdateComponent_ExactFullNameTakesPriorityOverShortNameCandidate()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "FullNamePriorityTarget"));
            target.AddComponent<NamespacedPriorityComponent>();

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = typeof(global::GlobalPriorityContractComponent).FullName
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.IsNotNull(target.GetComponent<global::GlobalPriorityContractComponent>());
            Assert.IsNull(result["warnings"]);
        }

        [Test]
        public void R1_6_ComponentResolver_CommonUnityShortNameStillResolves()
        {
            GameObject target = Track(new GameObject(ObjectPrefix + "CommonShortNameTarget"));

            Type resolvedType = ComponentResolver.FindComponentType(
                "Transform",
                target,
                out string warning,
                out string ambiguityError);

            Assert.AreSame(typeof(Transform), resolvedType);
            Assert.IsNull(warning);
            Assert.IsNull(ambiguityError);
        }

        [Test]
        public void R4_CreateSpriteAtlas_MismatchedName_ReturnsValidationErrorWithoutAsset()
        {
            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "CallerSuppliedName",
                ["savePath"] = MismatchedAtlasPath,
                ["folderPath"] = PackablesFolder
            });

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("PathDerivedName"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SpriteAtlas>(MismatchedAtlasPath));
        }

        [Test]
        public void R4_CreateSpriteAtlas_ResponseNameComesFromSavedAsset()
        {
            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "ContractAtlas",
                ["savePath"] = AtlasPath,
                ["folderPath"] = PackablesFolder,
                ["includeInBuild"] = false,
                ["allowRotation"] = false,
                ["tightPacking"] = true
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            SpriteAtlas savedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            Assert.IsNotNull(savedAtlas);
            Assert.AreEqual(savedAtlas.name, result["atlasName"]?.ToString());
            Assert.AreEqual("ContractAtlas", savedAtlas.name);
            SpriteAtlasPackingSettings savedPackingSettings = savedAtlas.GetPackingSettings();
            Assert.AreEqual(
                savedAtlas.IsIncludeInBuild(), result["includeInBuild"]?.ToObject<bool>());
            Assert.AreEqual(
                savedPackingSettings.enableRotation, result["allowRotation"]?.ToObject<bool>());
            Assert.AreEqual(
                savedPackingSettings.enableTightPacking, result["tightPacking"]?.ToObject<bool>());
        }

        [Test]
        public void R1_3_CreateSpriteAtlas_ResponseSettingsUseReadbackValues()
        {
            SetCreateSpriteAtlasToolField(
                "_readIncludeInBuild",
                (Func<SpriteAtlas, bool>)(_ => false));
            SetCreateSpriteAtlasToolField(
                "_readPackingSettings",
                (Func<SpriteAtlas, SpriteAtlasPackingSettings>)(_ =>
                    new SpriteAtlasPackingSettings
                    {
                        enableRotation = true,
                        enableTightPacking = false,
                        padding = 4
                    }));

            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "ReadbackAtlas",
                ["savePath"] = ReadbackAtlasPath,
                ["folderPath"] = PackablesFolder,
                ["includeInBuild"] = true,
                ["allowRotation"] = false,
                ["tightPacking"] = true
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false);
            Assert.IsFalse(result["includeInBuild"]?.ToObject<bool>() ?? true);
            Assert.IsTrue(result["allowRotation"]?.ToObject<bool>() ?? false);
            Assert.IsFalse(result["tightPacking"]?.ToObject<bool>() ?? true);
        }

        [Test]
        public void R1_5_CreateSpriteAtlas_MixedCaseExtension_IsNotAppendedOrRejectedAsNameMismatch()
        {
            const string missingFolder = TestRoot + "/MissingPackables";
            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "MixedCaseAtlas",
                ["savePath"] = TestRoot + "/MixedCaseAtlas.SpriteAtlas",
                ["folderPath"] = missingFolder
            });

            Assert.AreEqual("not_found_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain(missingFolder));
        }

        private void CreatePrefabAsset()
        {
            GameObject source = new GameObject(ObjectPrefix + "PrefabSource");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, PrefabPath, out bool success);
                Assert.IsTrue(success, "Test Prefab setup must succeed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private GameObject Track(GameObject gameObject)
        {
            if (gameObject != null && !_createdObjects.Contains(gameObject))
            {
                _createdObjects.Add(gameObject);
            }
            return gameObject;
        }

        private static JObject FindFailure(JArray failures, string fieldName)
        {
            return failures
                .OfType<JObject>()
                .FirstOrDefault(failure => failure["field"]?.ToString() == fieldName);
        }

        private static void RestoreCreateSpriteAtlasReadback()
        {
            SetCreateSpriteAtlasToolField(
                "_readIncludeInBuild",
                OriginalReadIncludeInBuild);
            SetCreateSpriteAtlasToolField(
                "_readPackingSettings",
                OriginalReadPackingSettings);
        }

        private static T GetCreateSpriteAtlasToolField<T>(string name)
        {
            FieldInfo field = typeof(CreateSpriteAtlasTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException(typeof(CreateSpriteAtlasTool).FullName, name);
            return (T)field.GetValue(null);
        }

        private static void SetCreateSpriteAtlasToolField(string name, object value)
        {
            FieldInfo field = typeof(CreateSpriteAtlasTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                Assert.Fail($"CreateSpriteAtlasTool private field '{name}' was not found");
            field.SetValue(null, value);
        }

        private static JObject ToJObject(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z
            };
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }

        private static void AssertVector(JToken actual, Vector3 expected)
        {
            Assert.IsNotNull(actual);
            Assert.That(actual["x"]?.ToObject<float>(), Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual["y"]?.ToObject<float>(), Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual["z"]?.ToObject<float>(), Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}

namespace McpUnity.Tests.ContractAlpha.Partial
{
    public class AmbiguousContractComponent : MonoBehaviour
    {
        public int value;
    }
}

namespace McpUnity.Tests.ContractBeta.Partial
{
    public class AmbiguousContractComponent : MonoBehaviour
    {
        public int value;
    }
}

namespace McpUnity.Tests.ContractNamespaced
{
    public class GlobalPriorityContractComponent : MonoBehaviour
    {
    }
}

namespace McpUnity.Tests.InheritedBase
{
    public class InheritanceNarrowingComponent : MonoBehaviour
    {
    }
}

namespace McpUnity.Tests.InheritedDerived
{
    public class InheritanceNarrowingComponent :
        global::McpUnity.Tests.InheritedBase.InheritanceNarrowingComponent
    {
    }
}

public class GlobalPriorityContractComponent : MonoBehaviour
{
}
