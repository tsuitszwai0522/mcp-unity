using System;
using System.Collections.Generic;
using System.IO;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace McpUnity.Tests.CollisionA
{
    public class SameNameReferenceAsset : ScriptableObject
    {
    }
}

namespace McpUnity.Tests.CollisionB
{
    public class SameNameReferenceAsset : ScriptableObject
    {
    }
}

namespace McpUnity.Tests
{
    [Serializable]
    public struct SerializedPropertyWriteItem
    {
        public int count;
        public string label;
    }

    [Serializable]
    public class SerializedPropertyWriteSettings
    {
        public int count = 3;
        public string label = "kept";
    }

    public class SerializedPropertyWriteReferenceAsset : ScriptableObject
    {
    }

    public class SerializedPropertyWriteOtherAsset : ScriptableObject
    {
    }

    [Serializable]
    public class SerializedPropertyWriteReferences
    {
        public SerializedPropertyWriteReferenceAsset primary;
        public SerializedPropertyWriteReferenceAsset secondary;
        public int nonReferenceValue = 8;
    }

    [Serializable]
    public abstract class SerializedPropertyWriteManagedBase
    {
        public int value;
    }

    [Serializable]
    public class SerializedPropertyWriteManagedLeaf : SerializedPropertyWriteManagedBase
    {
    }

    public class SerializedPropertyWriteProbeBehaviour : MonoBehaviour
    {
        [SerializeField]
        private int[] m_Numbers = { 1, 2 };

        [SerializeField]
        private List<string> m_Labels = new List<string> { "a", "b" };

        [SerializeField]
        private SerializedPropertyWriteItem[] m_Items =
        {
            new SerializedPropertyWriteItem { count = 4, label = "first" },
            new SerializedPropertyWriteItem { count = 5, label = "second" }
        };

        [SerializeField]
        private SerializedPropertyWriteSettings m_Settings = new SerializedPropertyWriteSettings();

        [SerializeField]
        private SerializedPropertyWriteReferences m_References = new SerializedPropertyWriteReferences();

        [SerializeField]
        private SerializedPropertyWriteReferenceAsset[] m_ReferenceArray =
            new SerializedPropertyWriteReferenceAsset[0];

        [SerializeField]
        private CollisionA.SameNameReferenceAsset[] m_CollisionReferences =
            new CollisionA.SameNameReferenceAsset[0];

        [SerializeField]
        private UnityEngine.Object m_SceneReference;

        [SerializeField]
        private UnityEvent m_Event = new UnityEvent();

        [SerializeField]
        private string m_Text = "original";

        [SerializeField]
        private LayerMask m_LayerMask;

        [SerializeField]
        private int m_PersistentCallsBudget;

        [SerializeReference]
        private SerializedPropertyWriteManagedBase m_Managed =
            new SerializedPropertyWriteManagedLeaf { value = 6 };

        public int[] Numbers => m_Numbers;
        public List<string> Labels => m_Labels;
        public SerializedPropertyWriteItem[] Items => m_Items;
        public SerializedPropertyWriteSettings Settings => m_Settings;
        public SerializedPropertyWriteReferences References => m_References;
        public SerializedPropertyWriteReferenceAsset[] ReferenceArray
        {
            get => m_ReferenceArray;
            set => m_ReferenceArray = value;
        }
        public UnityEngine.Object SceneReference
        {
            get => m_SceneReference;
            set => m_SceneReference = value;
        }
        public CollisionA.SameNameReferenceAsset[] CollisionReferences
        {
            get => m_CollisionReferences;
            set => m_CollisionReferences = value;
        }
        public string Text => m_Text;
        public LayerMask LayerMask => m_LayerMask;
        public int PersistentCallsBudget => m_PersistentCallsBudget;
    }

    [TestFixture]
    public class SerializedPropertyWriteTests
    {
        private const string AssetDirectory = "Assets/SerializedPropertyWriteTests";
        private const string FirstAssetPath = AssetDirectory + "/First.asset";
        private const string SecondAssetPath = AssetDirectory + "/Second.asset";
        private const string OtherAssetPath = AssetDirectory + "/Other.asset";
        private const string CollisionPreviousPath = AssetDirectory + "/CollisionPrevious.asset";
        private const string CollisionAttemptedPath = AssetDirectory + "/CollisionAttempted.asset";

        private GameObject _gameObject;
        private SerializedPropertyWriteProbeBehaviour _probe;
        private SerializedPropertyWriteReferenceAsset _firstAsset;
        private SerializedPropertyWriteReferenceAsset _secondAsset;
        private CollisionA.SameNameReferenceAsset _collisionPrevious;
        private string _assetDirectoryFullPath;
        private bool _ownsAssetDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.IsTrue(
                AssetPathUtils.TryNormalizeAssetPath(
                    AssetDirectory,
                    out _,
                    out _assetDirectoryFullPath,
                    out string pathError),
                pathError);
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(AssetDirectory)
                    || Directory.Exists(_assetDirectoryFullPath),
                $"Refusing to claim pre-existing test folder '{AssetDirectory}'.");

            string folderGuid =
                AssetDatabase.CreateFolder("Assets", "SerializedPropertyWriteTests");
            Assert.IsFalse(string.IsNullOrEmpty(folderGuid));
            _ownsAssetDirectory = true;

            _firstAsset = ScriptableObject.CreateInstance<SerializedPropertyWriteReferenceAsset>();
            _secondAsset = ScriptableObject.CreateInstance<SerializedPropertyWriteReferenceAsset>();
            SerializedPropertyWriteOtherAsset otherAsset =
                ScriptableObject.CreateInstance<SerializedPropertyWriteOtherAsset>();
            AssetDatabase.CreateAsset(_firstAsset, FirstAssetPath);
            AssetDatabase.CreateAsset(_secondAsset, SecondAssetPath);
            AssetDatabase.CreateAsset(otherAsset, OtherAssetPath);
            _collisionPrevious =
                ScriptableObject.CreateInstance<CollisionA.SameNameReferenceAsset>();
            CollisionB.SameNameReferenceAsset collisionAttempted =
                ScriptableObject.CreateInstance<CollisionB.SameNameReferenceAsset>();
            AssetDatabase.CreateAsset(_collisionPrevious, CollisionPreviousPath);
            AssetDatabase.CreateAsset(collisionAttempted, CollisionAttemptedPath);
        }

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("SerializedPropertyWriteProbe");
            _probe = _gameObject.AddComponent<SerializedPropertyWriteProbeBehaviour>();
            _probe.References.primary = _firstAsset;
            _probe.References.secondary = _firstAsset;
            _probe.ReferenceArray = new[] { _firstAsset, _secondAsset };
            _probe.CollisionReferences =
                new[] { _collisionPrevious, _collisionPrevious };
            _probe.SceneReference = _gameObject;
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (!_ownsAssetDirectory)
                return;

            bool deleted = AssetDatabase.DeleteAsset(AssetDirectory);
            AssetDatabase.Refresh();
            Assert.IsTrue(deleted, $"Failed to delete owned test folder '{AssetDirectory}'.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(AssetDirectory));
            Assert.IsFalse(Directory.Exists(_assetDirectoryFullPath));
            Assert.IsFalse(File.Exists(_assetDirectoryFullPath + ".meta"));
            _ownsAssetDirectory = false;
        }

        [Test]
        public void IntArray_ReaderShapeRoundTripsThroughWriteTool()
        {
            JObject read = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["fieldNames"] = new JArray("numbers")
            });
            JToken readerShape = read["fields"]["m_Numbers"].DeepClone();
            readerShape[0] = 42;

            JObject write = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["fieldData"] = new JObject { ["numbers"] = readerShape }
            });
            JObject reread = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["fieldNames"] = new JArray("numbers")
            });

            Assert.IsTrue(write["success"].ToObject<bool>(), write.ToString());
            CollectionAssert.AreEqual(new[] { 42, 2 }, _probe.Numbers);
            Assert.IsTrue(JToken.DeepEquals(readerShape, reread["fields"]["m_Numbers"]));
        }

        [Test]
        public void IntArray_GrowReplacesWholeArray()
        {
            AssertWrite("m_Numbers", new JArray(8, 9, 10, 11));
            CollectionAssert.AreEqual(new[] { 8, 9, 10, 11 }, _probe.Numbers);
        }

        [Test]
        public void IntArray_ShrinkReplacesWholeArray()
        {
            AssertWrite("m_Numbers", new JArray(7));
            CollectionAssert.AreEqual(new[] { 7 }, _probe.Numbers);
        }

        [Test]
        public void IntArray_ShrinkThroughWriteToolReturnsDiscardWarning()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["numbers"] = new JArray(7)
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            CollectionAssert.AreEqual(new[] { 7 }, _probe.Numbers);
            Assert.That(
                result["warnings"].ToObject<string[]>(),
                Contains.Item(
                    "Shrinking array 'm_Numbers' from 2 to 1 elements; " +
                    "the removed elements are discarded"));
        }

        [Test]
        public void IntArray_EmptyArrayClearsCollection()
        {
            AssertWrite("m_Numbers", new JArray());
            Assert.IsEmpty(_probe.Numbers);
        }

        [Test]
        public void Array_RejectsNonJArray()
        {
            bool written = TryWrite(
                "m_Numbers", new JObject { ["value"] = 1 }, false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("expects a JArray"));
            CollectionAssert.AreEqual(new[] { 1, 2 }, _probe.Numbers);
        }

        [Test]
        public void ArraySize_GrowDuplicatesLastElementAndWarns()
        {
            bool written = TryWrite(
                "m_Numbers.Array.size", 4, true,
                out List<string> warnings, out _, out _);

            Assert.IsTrue(written);
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 2 }, _probe.Numbers);
            Assert.That(
                string.Join("; ", warnings),
                Does.Contain("Unity grow duplicates the last element value"));
        }

        [Test]
        public void ArraySize_GrowThroughWriteToolReturnsDuplicateWarning()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_Numbers.Array.size"] = 4
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            CollectionAssert.AreEqual(new[] { 1, 2, 2, 2 }, _probe.Numbers);
            Assert.That(
                result["warnings"].ToObject<string[]>(),
                Contains.Item("Growing array size 'm_Numbers.Array.size' from 2 to 4: " +
                    "Unity grow duplicates the last element value"));
        }

        [Test]
        public void ArraySize_AboveDirectLimitFailsWithoutMutation()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_Numbers.Array.size"] = SerializedPropertyHelper.MaxDirectArraySize + 1
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.That(
                result["failedFields"].ToString(),
                Does.Contain(SerializedPropertyHelper.MaxDirectArraySize.ToString()));
            CollectionAssert.AreEqual(new[] { 1, 2 }, _probe.Numbers);
        }

        [Test]
        public void ArraySize_GrowFromZeroDoesNotClaimLastElementDuplication()
        {
            AssertWrite("m_Labels", new JArray());

            JObject result = ExecuteWrite(new JObject
            {
                ["m_Labels.Array.size"] = 2
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.That(
                result["warnings"].ToString(),
                Does.Contain("default values").And.Not.Contain("duplicates the last element"));
        }

        [Test]
        public void ArraySize_CannotResizeString()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_Text.Array.size"] = 2
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.That(
                result["failedFields"].ToString(),
                Does.Contain("cannot resize a string via Array.size"));
            Assert.AreEqual("original", _probe.Text);
        }

        [Test]
        public void ArraySize_ShrinkWritesDirectly()
        {
            AssertWrite("m_Numbers.Array.size", 1);
            CollectionAssert.AreEqual(new[] { 1 }, _probe.Numbers);
        }

        [Test]
        public void ArraySize_NegativeFailsWithoutMutation()
        {
            bool written = TryWrite(
                "m_Numbers.Array.size", -1, false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("cannot be negative"));
            CollectionAssert.AreEqual(new[] { 1, 2 }, _probe.Numbers);
        }

        [Test]
        public void ArraySize_NonIntegerFailsWithoutMutation()
        {
            bool written = TryWrite(
                "m_Numbers.Array.size", "3", false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("expects an integer"));
            CollectionAssert.AreEqual(new[] { 1, 2 }, _probe.Numbers);
        }

        [Test]
        public void StringList_ReplacesElements()
        {
            AssertWrite("m_Labels", new JArray("x", "y", "z"));
            CollectionAssert.AreEqual(new[] { "x", "y", "z" }, _probe.Labels);
        }

        [Test]
        public void StringList_ShrinksElements()
        {
            AssertWrite("m_Labels", new JArray("only"));
            CollectionAssert.AreEqual(new[] { "only" }, _probe.Labels);
        }

        [Test]
        public void StructArrayElement_PartialMergePreservesMissingChild()
        {
            AssertWrite(
                "m_Items",
                new JArray(
                    new JObject { ["count"] = 40 },
                    new JObject { ["label"] = "changed" }));

            Assert.AreEqual(40, _probe.Items[0].count);
            Assert.AreEqual("first", _probe.Items[0].label);
            Assert.AreEqual(5, _probe.Items[1].count);
            Assert.AreEqual("changed", _probe.Items[1].label);
        }

        [Test]
        public void StructArray_GrownElementPartialMergeStartsFromTypeDefault()
        {
            AssertWrite(
                "m_Items",
                new JArray(
                    new JObject { ["count"] = 10 },
                    new JObject { ["count"] = 20 },
                    new JObject { ["count"] = 30 }));

            Assert.AreEqual(3, _probe.Items.Length);
            Assert.AreEqual(30, _probe.Items[2].count);
            Assert.AreEqual(string.Empty, _probe.Items[2].label);
        }

        [Test]
        public void Generic_UnknownChildFailsAndListsLegalNames()
        {
            bool written = TryWrite(
                "m_Settings", new JObject { ["typo"] = 9 }, false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("Unknown child key 'typo'"));
            Assert.That(string.Join("; ", warnings), Does.Contain("count"));
            Assert.That(string.Join("; ", warnings), Does.Contain("label"));
        }

        [Test]
        public void Array_ElementConversionFailureDoesNotApplyAnyStagedChanges()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["numbers"] = new JArray(99, "not-an-int", 100)
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.That(
                result["failedFields"].ToString(),
                Does.Contain("numbers").And.Contain("Error setting"));
            CollectionAssert.AreEqual(new[] { 1, 2 }, _probe.Numbers);
        }

        [Test]
        public void ObjectReferenceArray_WrongTypeShrinkFailsWithoutChangingShapeOrElements()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_ReferenceArray"] = new JArray(
                    new JObject { ["assetPath"] = OtherAssetPath })
            });

            Assert.IsFalse(result["success"].ToObject<bool>(), result.ToString());
            Assert.That(result["failedFields"].ToString(), Does.Contain("not assignable"));
            Assert.AreEqual(2, _probe.ReferenceArray.Length);
            Assert.AreSame(_firstAsset, _probe.ReferenceArray[0]);
            Assert.AreSame(_secondAsset, _probe.ReferenceArray[1]);
        }

        [Test]
        public void ObjectReferenceArray_WrongTypeGrowFailsWithoutChangingShapeOrElements()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_ReferenceArray"] = new JArray(
                    new JObject { ["assetPath"] = FirstAssetPath },
                    new JObject { ["assetPath"] = SecondAssetPath },
                    new JObject { ["assetPath"] = OtherAssetPath })
            });

            Assert.IsFalse(result["success"].ToObject<bool>(), result.ToString());
            Assert.That(result["failedFields"].ToString(), Does.Contain("not assignable"));
            Assert.AreEqual(2, _probe.ReferenceArray.Length);
            Assert.AreSame(_firstAsset, _probe.ReferenceArray[0]);
            Assert.AreSame(_secondAsset, _probe.ReferenceArray[1]);
        }

        [Test]
        public void ResidualVerifyFailure_DisclosesShrinkRollbackAndFoldsWarningsIntoFieldFailure()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_CollisionReferences"] = new JArray(
                    new JObject { ["assetPath"] = CollisionAttemptedPath })
            });

            Assert.IsFalse(result["success"].ToObject<bool>(), result.ToString());
            string reason = result["failedFields"][0]["reason"].ToString();
            Assert.That(reason, Does.Contain("object-reference writes restored"));
            Assert.That(reason, Does.Contain("array size changes were not rolled back"));
            Assert.That(reason, Does.Contain("Shrinking array 'm_CollisionReferences'"));
            Assert.That(reason, Does.Not.Contain("Non-reference children"));
            Assert.AreEqual(1, _probe.CollisionReferences.Length);
            Assert.AreSame(_collisionPrevious, _probe.CollisionReferences[0]);
        }

        [Test]
        public void UpdateComponent_ResidualVerifyFailureFoldsPropertyWarningsIntoReason()
        {
            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["componentData"] = new JObject
                {
                    ["collisionReferences"] = new JArray(
                        new JObject { ["assetPath"] = CollisionAttemptedPath })
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>(), result.ToString());
            string reason = result["failedFields"][0]["reason"].ToString();
            Assert.That(reason, Does.Contain("array size changes were not rolled back"));
            Assert.That(reason, Does.Contain("Shrinking array 'm_CollisionReferences'"));
            Assert.AreEqual(1, _probe.CollisionReferences.Length);
            Assert.AreSame(_collisionPrevious, _probe.CollisionReferences[0]);
        }

        [Test]
        public void NestedObjectReference_AssetPathWritesAndVerifies()
        {
            bool written = TryWrite(
                "m_References",
                new JObject
                {
                    ["primary"] = new JObject { ["assetPath"] = SecondAssetPath }
                },
                true,
                out _,
                out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> writes,
                out string verificationFailure);

            Assert.IsTrue(written, verificationFailure);
            Assert.AreEqual(1, writes.Count);
            Assert.That(writes[0].PropertyPath, Does.EndWith("m_References.primary"));
            Assert.AreSame(_secondAsset, _probe.References.primary);
            Assert.AreSame(_firstAsset, _probe.References.secondary);
        }

        [Test]
        public void NestedObjectReference_InvalidTokenFailsBeforeApply()
        {
            bool written = TryWrite(
                "m_References",
                new JObject { ["primary"] = true },
                false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("expects an asset path"));
            Assert.AreSame(_firstAsset, _probe.References.primary);
        }

        [Test]
        public void ObjectReferenceStringMissProducesOnlyResolutionWarning()
        {
            bool written = TryWrite(
                "m_References.primary",
                AssetDirectory + "/DoesNotExist.asset",
                false,
                out List<string> warnings,
                out _,
                out _);

            Assert.IsFalse(written);
            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("Asset not found"));
        }

        [Test]
        public void NestedObjectReference_WrongAssetTypeFailsBeforeApply()
        {
            bool written = TryWrite(
                "m_References",
                new JObject
                {
                    ["nonReferenceValue"] = 99,
                    ["primary"] = new JObject { ["assetPath"] = OtherAssetPath },
                    ["secondary"] = new JObject { ["assetPath"] = SecondAssetPath }
                },
                false,
                out List<string> warnings, out _, out string verificationFailure);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("not assignable"));
            Assert.IsNull(verificationFailure);
            Assert.AreSame(_firstAsset, _probe.References.primary);
            Assert.AreSame(_firstAsset, _probe.References.secondary);
            Assert.AreEqual(8, _probe.References.nonReferenceValue);
        }

        [Test]
        public void NestedObjectReference_CollectsEveryWrittenPath()
        {
            bool written = TryWrite(
                "m_References",
                new JObject
                {
                    ["primary"] = new JObject { ["assetPath"] = SecondAssetPath },
                    ["secondary"] = new JObject { ["assetPath"] = SecondAssetPath }
                },
                true,
                out _,
                out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> writes,
                out string verificationFailure);

            Assert.IsTrue(written, verificationFailure);
            Assert.AreEqual(2, writes.Count);
            Assert.That(writes[0].PropertyPath, Does.EndWith("primary"));
            Assert.That(writes[1].PropertyPath, Does.EndWith("secondary"));
        }

        [Test]
        public void CompatibilityOverload_MultipleReferencesWarnsThatOnlyFirstIsReturned()
        {
            var serializedObject = new SerializedObject(_probe);
            SerializedProperty references = serializedObject.FindProperty("m_References");
            var warnings = new List<string>();

            bool written = SerializedPropertyHelper.SetValue(
                references,
                new JObject
                {
                    ["primary"] = new JObject { ["assetPath"] = SecondAssetPath },
                    ["secondary"] = new JObject { ["assetPath"] = SecondAssetPath }
                },
                warnings,
                "m_References",
                out SerializedPropertyHelper.ObjectReferenceWrite returnedWrite);

            Assert.IsTrue(written, string.Join("; ", warnings.ToArray()));
            Assert.IsNotNull(returnedWrite);
            Assert.That(
                string.Join("; ", warnings),
                Does.Contain("returns only the first assignment"));
        }

        [Test]
        public void Generic_PartialMergePreservesMissingChildren()
        {
            AssertWrite("m_Settings", new JObject { ["count"] = 42 });
            Assert.AreEqual(42, _probe.Settings.count);
            Assert.AreEqual("kept", _probe.Settings.label);
        }

        [Test]
        public void Generic_RejectsNonObjectValue()
        {
            bool written = TryWrite(
                "m_Settings", 12, false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("expects a JObject"));
            Assert.AreEqual(3, _probe.Settings.count);
        }

        [Test]
        public void PersistentCalls_DirectChildWriteWarnsButDoesNotBlock()
        {
            bool written = TryWrite(
                "m_Event",
                new JObject
                {
                    ["m_PersistentCalls"] = new JObject { ["m_Calls"] = new JArray() }
                },
                true,
                out List<string> warnings, out _, out string verificationFailure);

            Assert.IsTrue(written, verificationFailure);
            Assert.That(string.Join("; ", warnings), Does.Contain("mode derivation validation"));
            Assert.That(string.Join("; ", warnings), Does.Contain("wire_unity_event"));
            var serializedObject = new SerializedObject(_probe);
            SerializedProperty calls =
                serializedObject.FindProperty("m_Event.m_PersistentCalls.m_Calls");
            Assert.IsNotNull(calls);
            Assert.AreEqual(0, calls.arraySize);
        }

        [Test]
        public void StringProperty_RemainsScalarAndDoesNotEnterArrayPath()
        {
            AssertWrite("m_Text", "updated");
            Assert.AreEqual("updated", _probe.Text);
        }

        [Test]
        public void ManagedReference_FailsWithHonestUnsupportedMessage()
        {
            bool written = TryWrite(
                "m_Managed", new JObject { ["value"] = 20 }, false,
                out List<string> warnings, out _, out _);

            Assert.IsFalse(written);
            Assert.That(string.Join("; ", warnings), Does.Contain("Managed reference"));
            Assert.That(string.Join("; ", warnings), Does.Contain("not supported"));
        }

        [Test]
        public void LayerMask_ReaderIntegerShapeWritesThroughSerializedProperty()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_LayerMask"] = 37
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual(37, _probe.LayerMask.value);
        }

        [Test]
        public void PersistentCalls_SubstringWithoutSegmentBoundaryDoesNotWarn()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_PersistentCallsBudget"] = 12
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual(12, _probe.PersistentCallsBudget);
            Assert.IsNull(result["warnings"]);
        }

        [Test]
        public void SceneReference_ReaderNullAssetPathDoesNotProduceLocatorFailureWarning()
        {
            JObject read = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["fieldNames"] = new JArray("m_SceneReference")
            });

            JObject result = ExecuteWrite(new JObject
            {
                ["m_SceneReference"] = read["fields"]["m_SceneReference"].DeepClone()
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreSame(_gameObject, _probe.SceneReference);
            string[] warnings = result["warnings"]?.ToObject<string[]>() ?? new string[0];
            Assert.That(string.Join("; ", warnings), Does.Not.Contain("Locator 'assetPath'"));
        }

        [Test]
        public void Converter_StructuredReferenceIgnoresNullLocatorAndDisclosesDescriptiveKeys()
        {
            var failures = new List<string>();
            var warnings = new List<string>();
            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["assetPath"] = JValue.CreateNull(),
                    ["instanceId"] = _gameObject.GetInstanceID(),
                    ["name"] = _gameObject.name,
                    ["type"] = nameof(GameObject)
                },
                typeof(GameObject),
                null,
                failures,
                warnings);

            Assert.AreSame(_gameObject, converted);
            Assert.IsEmpty(failures);
            Assert.That(string.Join("; ", warnings), Does.Contain("Ignored descriptive keys"));
            Assert.That(string.Join("; ", warnings), Does.Not.Contain("Locator 'assetPath'"));
        }

        [Test]
        public void UpdateComponent_FallbackWritesArrayThenArraySize()
        {
            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["componentData"] = new JObject
                {
                    ["numbers"] = new JArray(3, 4),
                    ["numbers.Array.size"] = 3
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            CollectionAssert.AreEqual(new[] { 3, 4, 4 }, _probe.Numbers);
            Assert.That(
                string.Join("; ", result["warnings"].ToObject<string[]>()),
                Does.Contain("Unity grow duplicates the last element value"));
        }

        [Test]
        public void StructArray_UnknownNestedChildFailsWithoutApplyingEarlierElement()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["m_Items"] = new JArray(
                    new JObject { ["count"] = 77 },
                    new JObject { ["unknown"] = 12 })
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.That(
                result["failedFields"].ToString(),
                Does.Contain("Unknown child key 'unknown'"));
            Assert.AreEqual(4, _probe.Items[0].count);
            Assert.AreEqual(5, _probe.Items[1].count);
        }

        [Test]
        public void MixedFields_ReturnsSerializedPathForSuccessAndSpecificFailureReason()
        {
            JObject result = ExecuteWrite(new JObject
            {
                ["numbers"] = new JArray(8, 9),
                ["settings"] = new JObject { ["unknown"] = 12 }
            });

            Assert.IsFalse(result["success"].ToObject<bool>(), result.ToString());
            Assert.That(result["updatedFields"].ToObject<string[]>(), Contains.Item("m_Numbers"));
            Assert.That(result["updatedFields"].ToObject<string[]>(), Has.No.Member("numbers"));
            JObject failedField = (JObject)result["failedFields"][0];
            Assert.That(failedField["reason"].ToString(), Does.Contain("Unknown child key 'unknown'"));
            Assert.That(failedField["reason"].ToString(), Does.Not.Contain("Value could not be assigned"));
        }

        [Test]
        public void MissingReferencePrevious_IsNotWrittenAsNullWhenSiblingVerificationFails()
        {
            string missingPath = AssetDirectory + "/MissingPrevious.asset";
            string holderPath = AssetDirectory + "/MissingHolder.asset";
            var missing = ScriptableObject.CreateInstance<SerializedPropertyWriteReferenceAsset>();
            var holder = ScriptableObject.CreateInstance<SerializedPropertyWriteReferenceHolderAsset>();
            AssetDatabase.CreateAsset(missing, missingPath);
            string missingGuid = AssetDatabase.AssetPathToGUID(missingPath);
            holder.References.primary = missing;
            holder.References.secondary = _firstAsset;
            AssetDatabase.CreateAsset(holder, holderPath);
            AssetDatabase.SaveAssets();
            Assert.IsTrue(AssetDatabase.DeleteAsset(missingPath));
            AssetDatabase.Refresh();

            holder = AssetDatabase.LoadAssetAtPath<SerializedPropertyWriteReferenceHolderAsset>(holderPath);
            var serializedObject = new SerializedObject(holder);
            SerializedProperty references = serializedObject.FindProperty("m_References");
            Assert.IsNotNull(references);
            SerializedProperty missingProperty = references.FindPropertyRelative("primary");
            Assert.IsTrue(missingProperty.objectReferenceValue == null);
            Assert.AreNotEqual(0, missingProperty.objectReferenceInstanceIDValue);
            string absoluteHolderPath = Path.GetFullPath(holderPath);
            Assert.That(
                File.ReadAllText(absoluteHolderPath),
                Does.Contain(missingGuid),
                "Test setup must retain a Missing reference GUID");

            var warnings = new List<string>();
            bool staged = SerializedPropertyHelper.SetValue(
                references,
                new JObject
                {
                    ["primary"] = new JObject { ["assetPath"] = SecondAssetPath },
                    ["secondary"] = new JObject { ["assetPath"] = SecondAssetPath }
                },
                warnings,
                "m_References",
                out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> writes);
            Assert.IsTrue(staged, string.Join("; ", warnings.ToArray()));
            serializedObject.ApplyModifiedProperties();

            var simulateRejectedSibling = new SerializedObject(holder);
            simulateRejectedSibling.FindProperty("m_References.secondary")
                .objectReferenceValue = _firstAsset;
            simulateRejectedSibling.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            string beforeVerification = File.ReadAllText(absoluteHolderPath);

            bool verified = SerializedPropertyHelper.VerifyObjectReferenceWrites(
                holder, writes, out string failureReason);
            AssetDatabase.SaveAssets();
            string afterVerification = File.ReadAllText(absoluteHolderPath);

            Assert.IsFalse(verified);
            Assert.That(failureReason, Does.Contain("skipped (missing-reference previous)"));
            Assert.That(failureReason, Does.Contain("m_References.primary"));
            Assert.That(failureReason, Does.Contain("missing-reference GUID"));
            Assert.AreEqual(
                beforeVerification,
                afterVerification,
                "Rollback verification must not serialize null over the missing-reference path");
            Assert.AreSame(_secondAsset, holder.References.primary);
            Assert.AreSame(_firstAsset, holder.References.secondary);
        }

        [Test]
        public void LiveSceneObjectPrevious_IsRestoredAndNotSkippedWhenSiblingVerificationFails()
        {
            var serializedObject = new SerializedObject(_probe);
            var warnings = new List<string>();
            var writes = new List<SerializedPropertyHelper.ObjectReferenceWriteRecord>();

            SerializedProperty sceneReference = serializedObject.FindProperty("m_SceneReference");
            bool sceneStaged = SerializedPropertyHelper.SetValue(
                sceneReference,
                new JObject { ["assetPath"] = SecondAssetPath },
                warnings,
                "m_SceneReference",
                out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> sceneWrites);
            Assert.IsTrue(sceneStaged, string.Join("; ", warnings.ToArray()));
            writes.AddRange(sceneWrites);

            SerializedProperty siblingReference =
                serializedObject.FindProperty("m_References.primary");
            bool siblingStaged = SerializedPropertyHelper.SetValue(
                siblingReference,
                new JObject { ["assetPath"] = SecondAssetPath },
                warnings,
                "m_References.primary",
                out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> siblingWrites);
            Assert.IsTrue(siblingStaged, string.Join("; ", warnings.ToArray()));
            writes.AddRange(siblingWrites);
            serializedObject.ApplyModifiedProperties();

            var simulateRejectedSibling = new SerializedObject(_probe);
            simulateRejectedSibling.FindProperty("m_References.primary")
                .objectReferenceValue = _firstAsset;
            simulateRejectedSibling.ApplyModifiedPropertiesWithoutUndo();

            bool verified = SerializedPropertyHelper.VerifyObjectReferenceWrites(
                _probe, writes, out string failureReason);

            Assert.IsFalse(verified);
            Assert.AreSame(_gameObject, _probe.SceneReference);
            Assert.That(
                failureReason,
                Does.Contain(
                    "object-reference writes restored: " +
                    "['m_References.primary', 'm_SceneReference']"));
            Assert.That(failureReason, Does.Contain("skipped (missing-reference previous): [none]"));
        }

        private JObject ExecuteWrite(JObject fieldData)
        {
            return new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = _gameObject.GetInstanceID(),
                ["componentName"] = typeof(SerializedPropertyWriteProbeBehaviour).FullName,
                ["fieldData"] = fieldData
            });
        }

        private void AssertWrite(string propertyPath, JToken value)
        {
            bool written = TryWrite(
                propertyPath, value, true,
                out List<string> warnings, out _, out string verificationFailure);
            Assert.IsTrue(
                written,
                verificationFailure ?? string.Join("; ", warnings.ToArray()));
        }

        private bool TryWrite(
            string propertyPath,
            JToken value,
            bool apply,
            out List<string> warnings,
            out List<SerializedPropertyHelper.ObjectReferenceWriteRecord> objectReferenceWrites,
            out string verificationFailure)
        {
            var serializedObject = new SerializedObject(_probe);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            Assert.IsNotNull(property, propertyPath);
            warnings = new List<string>();
            bool written = SerializedPropertyHelper.SetValue(
                property,
                value,
                warnings,
                propertyPath,
                out objectReferenceWrites);
            verificationFailure = null;
            if (!written || !apply)
            {
                return written;
            }

            serializedObject.ApplyModifiedProperties();
            return SerializedPropertyHelper.VerifyObjectReferenceWrites(
                _probe, objectReferenceWrites, out verificationFailure);
        }
    }
}
