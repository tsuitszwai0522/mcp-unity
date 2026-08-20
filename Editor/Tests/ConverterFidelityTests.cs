using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using McpUnity.Tools;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    [Serializable]
    internal class ConverterFidelityColorContainer
    {
        public Color color;
    }

    [Serializable]
    internal class ConverterFidelityNestedValue
    {
        public int value = 7;
    }

    [Serializable]
    internal class ConverterFidelityWalkerValue
    {
        public ConverterFidelityNestedValue nested = new ConverterFidelityNestedValue();
        public string reference = "original";
        public int number = 5;
    }

    [Serializable]
    internal class ConverterFidelityBaseValue
    {
        [SerializeField]
        private int baseValue = 3;

        public int BaseValue => baseValue;
    }

    [Serializable]
    internal class ConverterFidelityDerivedValue : ConverterFidelityBaseValue
    {
        public int derivedValue = 4;
    }

    [Serializable]
    internal class ConverterFidelityItem
    {
        public string name;
        public int qty;
    }

    [Serializable]
    public class ConverterFidelityStats
    {
        public int hp = 10;
        public int stamina = 20;
    }

    [Serializable]
    internal class ConverterFidelityFallbackLeaf
    {
        public int v = 5;
    }

    [Serializable]
    internal class ConverterFidelityFallbackContainer
    {
        [SerializeField]
        private ConverterFidelityFallbackLeaf m_X = new ConverterFidelityFallbackLeaf();

        [SerializeField]
        private int m_Number = 8;

        public ConverterFidelityFallbackLeaf X
        {
            get => m_X;
            set => m_X = value;
        }

        public int Number
        {
            get => m_Number;
            set => m_Number = value;
        }
    }

    [Serializable]
    internal struct ConverterFidelityFallbackStruct
    {
        public int Writable { get; set; }
        public int ReadOnly => Writable * 2;
    }

    [Flags]
    public enum ConverterFidelityFlags
    {
        None = 0,
        First = 1,
        Second = 2
    }

    public enum ConverterFidelityByteEnum : byte
    {
        None = 0,
        A = 1
    }



    /// <summary>
    /// Fidelity contract tests for JSON conversion and write-tool responses.
    /// </summary>
    public class ConverterFidelityBehaviour : MonoBehaviour
    {
        public static int ObjectGetterReadCount;

        public int number;
        public Color color = new Color(0.2f, 0.3f, 0.4f, 0.5f);
        public ConverterFidelityFlags flags;
        public UnityEngine.Object assetReference;
        private UnityEngine.Object _objectReference;
        [SerializeField]
        private ConverterFidelityStats statsProperty = new ConverterFidelityStats();
        [SerializeField]
        private Vector2Int[] cellsProperty = { new Vector2Int(1, 2) };

        public ConverterFidelityStats StatsProperty
        {
            get => statsProperty;
            set => statsProperty = value;
        }

        public Vector2Int[] CellsProperty
        {
            get => cellsProperty;
            set => cellsProperty = value;
        }

        public UnityEngine.Object ObjectReferenceProperty
        {
            get
            {
                ObjectGetterReadCount++;
                return _objectReference;
            }
            set => _objectReference = value;
        }
    }

    public class ConverterFidelityTests
    {
        private const string TestAssetDirectory = "Assets/ConverterFidelityTests";
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _spawnedObjects.Clear();
            ConverterFidelityBehaviour.ObjectGetterReadCount = 0;
            if (!AssetDatabase.IsValidFolder(TestAssetDirectory))
            {
                AssetDatabase.CreateFolder("Assets", "ConverterFidelityTests");
            }
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawnedObjects.Count - 1; i >= 0; i--)
            {
                if (_spawnedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawnedObjects[i]);
                }
            }
            _spawnedObjects.Clear();

            if (AssetDatabase.IsValidFolder(TestAssetDirectory))
            {
                Assert.IsTrue(
                    AssetDatabase.DeleteAsset(TestAssetDirectory),
                    $"Expected test asset folder '{TestAssetDirectory}' to be deleted");
            }
            AssetDatabase.Refresh();
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(TestAssetDirectory),
                $"Test asset folder '{TestAssetDirectory}' still exists after cleanup");
        }

        [Test]
        public void Converter_PartialNestedColor_PreservesUnmentionedComponents()
        {
            var seed = new ConverterFidelityColorContainer
            {
                color = new Color(0.2f, 0.3f, 0.4f, 0.5f)
            };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["color"] = new JObject { ["r"] = 1f }
                },
                typeof(ConverterFidelityColorContainer),
                seed,
                failures);

            Assert.That(failures, Is.Empty);
            Assert.AreSame(seed, converted);
            Assert.AreEqual(1f, seed.color.r);
            Assert.AreEqual(0.3f, seed.color.g);
            Assert.AreEqual(0.4f, seed.color.b);
            Assert.AreEqual(0.5f, seed.color.a);
        }

        [Test]
        public void Converter_StructUnknownKey_ReportsValidKeys()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1f, ["tpyo"] = 2f },
                typeof(Vector2),
                new Vector2(7f, 8f),
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("tpyo"));
            Assert.That(failures, Has.Some.Contains("Valid keys: x, y"));
        }

        [Test]
        public void Converter_WalkerNestedTypo_FailsWithoutMutation()
        {
            var seed = new ConverterFidelityWalkerValue();
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["nested"] = new JObject
                    {
                        ["value"] = 99,
                        ["tpyo"] = 1
                    }
                },
                typeof(ConverterFidelityWalkerValue),
                seed,
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("tpyo"));
            Assert.AreEqual(7, seed.nested.value);
        }

        [Test]
        public void Converter_WalkerWritesBasePrivateSerializeField()
        {
            var seed = new ConverterFidelityDerivedValue { derivedValue = 91 };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["baseValue"] = 42 },
                typeof(ConverterFidelityDerivedValue),
                seed,
                failures);

            Assert.AreSame(seed, converted);
            Assert.That(failures, Is.Empty);
            Assert.AreEqual(42, seed.BaseValue);
            Assert.AreEqual(91, seed.derivedValue);
        }

        [Test]
        public void Converter_WalkerExplicitNull_ClearsReference()
        {
            var seed = new ConverterFidelityWalkerValue { reference = "keep" };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["reference"] = JValue.CreateNull() },
                typeof(ConverterFidelityWalkerValue),
                seed,
                failures);

            Assert.AreSame(seed, converted);
            Assert.That(failures, Is.Empty);
            Assert.IsNull(seed.reference);
        }

        [Test]
        public void Converter_WalkerExplicitNullForValueType_FailsWithoutMutation()
        {
            var seed = new ConverterFidelityWalkerValue { number = 17 };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["number"] = JValue.CreateNull() },
                typeof(ConverterFidelityWalkerValue),
                seed,
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("non-nullable"));
            Assert.AreEqual(17, seed.number);
        }

        [Test]
        public void Converter_WalkerMixedValidAndUnknown_FailsWithoutApplyingAnyField()
        {
            var seed = new ConverterFidelityWalkerValue
            {
                reference = "unchanged",
                number = 23
            };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["number"] = 99,
                    ["tpyo"] = true
                },
                typeof(ConverterFidelityWalkerValue),
                seed,
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("tpyo"));
            Assert.AreEqual(23, seed.number);
            Assert.AreEqual("unchanged", seed.reference);
        }

        [Test]
        public void Converter_Vector2IntWhitelist_PreservesFullAndPartialWrites()
        {
            var fullFailures = new List<string>();
            object full = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1, ["y"] = 2 },
                typeof(Vector2Int),
                null,
                fullFailures);

            var partialFailures = new List<string>();
            object partial = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1 },
                typeof(Vector2Int),
                new Vector2Int(7, 8),
                partialFailures);

            Assert.That(fullFailures, Is.Empty);
            Assert.AreEqual(new Vector2Int(1, 2), (Vector2Int)full);
            Assert.That(partialFailures, Is.Empty);
            Assert.AreEqual(new Vector2Int(1, 8), (Vector2Int)partial);
        }

        [Test]
        public void Converter_Vector2IntWhitelist_RejectsUnknownKey()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1, ["typo"] = 2 },
                typeof(Vector2Int),
                new Vector2Int(7, 8),
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("typo"));
        }

        [Test]
        public void Converter_ClassArrayFailure_DoesNotMutateEarlierLiveElement()
        {
            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Error converting value to type Int32"));
            var first = new ConverterFidelityItem { name = "sword", qty = 1 };
            var second = new ConverterFidelityItem { name = "shield", qty = 2 };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JArray
                {
                    new JObject { ["name"] = "axe" },
                    new JObject { ["qty"] = "bad" }
                },
                typeof(ConverterFidelityItem[]),
                new[] { first, second },
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("Array element 1"));
            Assert.AreEqual("sword", first.name);
            Assert.AreEqual(1, first.qty);
        }

        [Test]
        public void Converter_ClassListFailure_DoesNotMutateEarlierLiveElement()
        {
            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Error converting value to type Int32"));
            var first = new ConverterFidelityItem { name = "sword", qty = 1 };
            var second = new ConverterFidelityItem { name = "shield", qty = 2 };
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JArray
                {
                    new JObject { ["name"] = "axe" },
                    new JObject { ["qty"] = "bad" }
                },
                typeof(List<ConverterFidelityItem>),
                new List<ConverterFidelityItem> { first, second },
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("List element 1"));
            Assert.AreEqual("sword", first.name);
            Assert.AreEqual(1, first.qty);
        }

        [Test]
        public void Converter_NewtonsoftFallbackFailure_DoesNotMutateLiveGrandchild()
        {
            var seed = new ConverterFidelityFallbackContainer();
            ConverterFidelityFallbackLeaf liveLeaf = seed.X;
            var failures = new List<string>();
            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Error converting value to type ConverterFidelityFallbackContainer:"));

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["X"] = new JObject { ["v"] = 99 },
                    ["Number"] = "bad"
                },
                typeof(ConverterFidelityFallbackContainer),
                seed,
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Is.Not.Empty);
            Assert.AreSame(liveLeaf, seed.X);
            Assert.AreEqual(5, liveLeaf.v);
        }

        [Test]
        public void Converter_PartialStructWithoutSeed_RequiresAllKeys()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["r"] = 0.25f },
                typeof(Color),
                null,
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("no readable current value"));
            Assert.That(failures, Has.Some.Contains("{r, g, b, a}"));
        }

        [Test]
        public void Converter_IntegerUnityStructWhitelistUsesStrictIntegerSemantics()
        {
            var vectorFailures = new List<string>();
            var rectFailures = new List<string>();
            var boundsFailures = new List<string>();
            var colorFailures = new List<string>();
            var fractionalFailures = new List<string>();

            object vector = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 },
                typeof(Vector3Int),
                null,
                vectorFailures);
            object rect = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 4, ["y"] = 5, ["width"] = 6, ["height"] = 7 },
                typeof(RectInt),
                null,
                rectFailures);
            object bounds = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["position"] = new JObject { ["x"] = 1, ["y"] = 2, ["z"] = 3 },
                    ["size"] = new JObject { ["x"] = 4, ["y"] = 5, ["z"] = 6 }
                },
                typeof(BoundsInt),
                null,
                boundsFailures);
            object color = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["r"] = 10, ["g"] = 20, ["b"] = 30, ["a"] = 40 },
                typeof(Color32),
                null,
                colorFailures);
            object fractional = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1.5, ["y"] = 2 },
                typeof(Vector2Int),
                null,
                fractionalFailures);

            Assert.AreEqual(new Vector3Int(1, 2, 3), (Vector3Int)vector);
            Assert.AreEqual(new RectInt(4, 5, 6, 7), (RectInt)rect);
            Assert.AreEqual(
                new BoundsInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6)),
                (BoundsInt)bounds);
            Assert.AreEqual(new Color32(10, 20, 30, 40), (Color32)color);
            Assert.That(vectorFailures, Is.Empty);
            Assert.That(rectFailures, Is.Empty);
            Assert.That(boundsFailures, Is.Empty);
            Assert.That(colorFailures, Is.Empty);
            Assert.IsNull(fractional);
            Assert.That(fractionalFailures, Has.Some.Contains("JSON integer"));
        }

        [Test]
        public void Converter_FallbackDictionaryDoesNotTreatKeysAsMembers()
        {
            var failures = new List<string>();
            var seed = new Dictionary<string, int> { ["shield"] = 2 };

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["sword"] = 3 },
                typeof(Dictionary<string, int>),
                seed,
                failures);

            Assert.That(failures, Is.Empty);
            Assert.AreEqual(3, ((Dictionary<string, int>)converted)["sword"]);
            Assert.IsFalse(seed.ContainsKey("sword"));
            Assert.AreEqual(2, seed["shield"]);
        }

        [Test]
        public void Converter_FallbackStructRejectsReadOnlyMember()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["ReadOnly"] = 8 },
                typeof(ConverterFidelityFallbackStruct),
                new ConverterFidelityFallbackStruct { Writable = 4 },
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("ReadOnly"));
            Assert.That(failures, Has.Some.Contains("Valid public writable members"));
        }

        [Test]
        public void Converter_FloatStructRejectsStringAndBooleanCoercion()
        {
            var stringFailures = new List<string>();
            var booleanFailures = new List<string>();

            object fromString = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = "1", ["y"] = 2 },
                typeof(Vector2),
                null,
                stringFailures);
            object fromBoolean = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = true, ["y"] = 2 },
                typeof(Vector2),
                null,
                booleanFailures);

            Assert.IsNull(fromString);
            Assert.IsNull(fromBoolean);
            Assert.That(stringFailures, Has.Some.Contains("JSON number"));
            Assert.That(booleanFailures, Has.Some.Contains("JSON number"));
        }

        [Test]
        public void Converter_EnumRejectsUndefinedValuesAndAcceptsDefinedValue()
        {
            var invalidFailures = new List<string>();
            object invalid = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(999), typeof(CameraClearFlags), null, invalidFailures);

            var zeroFailures = new List<string>();
            object zero = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(0), typeof(CameraClearFlags), null, zeroFailures);

            var validFailures = new List<string>();
            object valid = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(3), typeof(CameraClearFlags), null, validFailures);

            Assert.IsNull(invalid);
            Assert.That(invalidFailures, Has.Some.Contains("Skybox"));
            Assert.IsNull(zero);
            Assert.That(zeroFailures, Has.Some.Contains("Valid values"));
            Assert.AreEqual(CameraClearFlags.Depth, valid);
            Assert.That(validFailures, Is.Empty);
        }

        [Test]
        public void Converter_FlagsEnumAcceptsDefinedBitCombination()
        {
            HideFlags requested = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor;
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue((int)requested), typeof(HideFlags), null, failures);

            Assert.AreEqual(requested, converted);
            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void Converter_FlagsEnumRejectsUndefinedBits()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(4), typeof(ConverterFidelityFlags), null, failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("Valid values"));
        }

        [TestCase(257L)]
        [TestCase(4294967296L)]
        public void Converter_EnumRejectsNumericValuesThatWouldTruncate(long requestedValue)
        {
            Type enumType = requestedValue == 257L
                ? typeof(ConverterFidelityByteEnum)
                : typeof(CameraClearFlags);
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(requestedValue), enumType, null, failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("underlying range"));
            Assert.That(failures, Has.Some.Contains("Valid values"));
        }

        [Test]
        public void Converter_EnumRejectsNumericStringThatWouldTruncate()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue("257"), typeof(ConverterFidelityByteEnum), null, failures);

            Assert.IsNull(converted);
            Assert.That(failures, Has.Some.Contains("underlying range"));
        }

        [Test]
        public void Converter_NullableEnumRoutesThroughStrictEnumConversion()
        {
            var validFailures = new List<string>();
            var invalidFailures = new List<string>();

            object valid = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue("A"), typeof(ConverterFidelityByteEnum?), null, validFailures);
            object invalid = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JValue(257), typeof(ConverterFidelityByteEnum?), null, invalidFailures);

            Assert.AreEqual(ConverterFidelityByteEnum.A, valid);
            Assert.That(validFailures, Is.Empty);
            Assert.IsNull(invalid);
            Assert.That(invalidFailures, Has.Some.Contains("underlying range"));
        }

        [Test]
        public void Converter_NullableUnityStructUsesUnderlyingTypePipeline()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1f, ["y"] = 2f, ["z"] = 3f },
                typeof(Vector3?),
                null,
                failures);

            Assert.That(failures, Is.Empty);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), converted);
        }

        [Test]
        public void Converter_InterfaceDictionaryMaterializesConcreteDictionary()
        {
            var failures = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["sword"] = 3 },
                typeof(IDictionary<string, int>),
                null,
                failures);

            Assert.That(failures, Is.Empty);
            Assert.That(converted, Is.TypeOf<Dictionary<string, int>>());
            Assert.AreEqual(3, ((IDictionary<string, int>)converted)["sword"]);
        }

        [Test]
        public void Converter_IntegerUnityStructAcceptsIntegralFloats()
        {
            var vectorFailures = new List<string>();
            var colorFailures = new List<string>();

            object vector = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["x"] = 1.0, ["y"] = 2.0 },
                typeof(Vector2Int),
                null,
                vectorFailures);
            object color = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject { ["r"] = 10.0, ["g"] = 20.0, ["b"] = 30.0, ["a"] = 40.0 },
                typeof(Color32),
                null,
                colorFailures);

            Assert.That(vectorFailures, Is.Empty);
            Assert.That(colorFailures, Is.Empty);
            Assert.AreEqual(new Vector2Int(1, 2), vector);
            Assert.AreEqual(new Color32(10, 20, 30, 40), color);
        }

        [Test]
        public void SerializedPropertyEnum_FlagsAndNumericStringUseSameMaskRules()
        {
            var holder = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            try
            {
                var serializedObject = new SerializedObject(holder);
                SerializedProperty property = serializedObject.FindProperty("flags");
                Assert.IsNotNull(property);
                var integerWarnings = new List<string>();
                var stringWarnings = new List<string>();

                bool integerWritten = SerializedPropertyHelper.SetValue(
                    property, new JValue(3), integerWarnings, "flags");
                bool stringWritten = SerializedPropertyHelper.SetValue(
                    property, new JValue("3"), stringWarnings, "flags");
                serializedObject.ApplyModifiedProperties();

                Assert.IsTrue(integerWritten);
                Assert.IsTrue(stringWritten);
                Assert.That(integerWarnings, Is.Empty);
                Assert.That(stringWarnings, Is.Empty);
                Assert.AreEqual(
                    ConverterFidelityFlags.First | ConverterFidelityFlags.Second,
                    holder.flags);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void SerializedPropertyEnum_UndefinedIntegerRestoresPreviousValue()
        {
            GameObject gameObject = Spawn("ConverterFidelity_Camera");
            Camera camera = gameObject.AddComponent<Camera>();
            var serializedObject = new SerializedObject(camera);
            SerializedProperty property = serializedObject.FindProperty("m_ClearFlags");
            Assert.IsNotNull(property);
            property.intValue = (int)CameraClearFlags.Depth;
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            int previousValue = property.intValue;
            var warnings = new List<string>();

            bool written = SerializedPropertyHelper.SetValue(
                property, new JValue(999), warnings, "m_ClearFlags");

            Assert.IsFalse(written);
            Assert.AreEqual(previousValue, property.intValue);
            serializedObject.ApplyModifiedProperties();
            Assert.AreEqual(CameraClearFlags.Depth, camera.clearFlags);
            Assert.That(warnings, Has.Some.Contains("Valid names"));
            Assert.That(warnings, Has.Some.Contains("[Flags]"));
        }

        [Test]
        public void CreateScriptableObject_FieldFailure_DoesNotCreateAsset()
        {
            string assetPath = TestAssetDirectory + "/ShouldNotExist.asset";
            var result = new CreateScriptableObjectTool().Execute(new JObject
            {
                ["typeName"] = typeof(ConverterFidelityScriptableObject).FullName,
                ["savePath"] = assetPath,
                ["fieldValues"] = new JObject
                {
                    ["notAField"] = 1
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual(
                string.Empty,
                AssetDatabase.AssetPathToGUID(
                    assetPath, AssetPathToGUIDOptions.OnlyExistingAssets));
            Assert.That(result["message"].ToString(), Does.Contain("no asset was created"));
            Assert.AreEqual(1, ((JArray)result["failedFields"]).Count);
        }

        [Test]
        public void CreatePrefab_FieldFailure_DoesNotCreateAsset()
        {
            string prefabName = TestAssetDirectory + "/ShouldNotExist";
            string prefabPath = prefabName + ".prefab";

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabName,
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldValues"] = new JObject
                {
                    ["notAField"] = 1
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual(
                string.Empty,
                AssetDatabase.AssetPathToGUID(
                    prefabPath, AssetPathToGUIDOptions.OnlyExistingAssets));
            Assert.That(result["message"].ToString(), Does.Contain("nothing was created"));
            Assert.AreEqual(1, ((JArray)result["failedFields"]).Count);
        }

        [Test]
        public void CreatePrefab_ComponentCannotBeAdded_ReturnsComponentErrorWithoutCreatingAsset()
        {
            string prefabName = TestAssetDirectory + "/InvalidComponentShouldNotExist";
            string prefabPath = prefabName + ".prefab";

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabName,
                ["componentName"] = typeof(Camera).FullName
            });

            Assert.IsNotNull(result["error"]);
            Assert.AreEqual("component_error", result["error"]["type"].ToString());
            Assert.That(result["error"]["message"].ToString(), Does.Contain("could not be added"));
            Assert.AreEqual(string.Empty, AssetDatabase.AssetPathToGUID(prefabPath));
        }

        [Test]
        public void CreateMaterial_PropertyFailure_DoesNotCreateAsset()
        {
            string materialPath = TestAssetDirectory + "/ShouldNotExist.mat";

            JObject result = new CreateMaterialTool().Execute(new JObject
            {
                ["name"] = "ShouldNotExist",
                ["shader"] = "Standard",
                ["savePath"] = materialPath,
                ["properties"] = new JObject
                {
                    ["_DefinitelyUnknown"] = 1
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual(string.Empty, AssetDatabase.AssetPathToGUID(materialPath));
            Assert.That(result["message"].ToString(), Does.Contain("no asset was created"));
            Assert.AreEqual(1, ((JArray)result["failedProperties"]).Count);
            CollectionAssert.Contains(
                ((JArray)result["unknownProperties"]).ToObject<string[]>(),
                "_DefinitelyUnknown");
        }

        [Test]
        public void ModifyMaterial_ReportsModifiedFailedAndUnknownProperties()
        {
            string materialPath = TestAssetDirectory + "/ThreeState.mat";
            var material = new Material(Shader.Find("Standard"));
            material.SetColor("_Color", new Color(0.6f, 0.7f, 0.8f, 0.9f));
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            JObject result = new ModifyMaterialTool().Execute(new JObject
            {
                ["materialPath"] = materialPath,
                ["properties"] = new JObject
                {
                    ["_Color"] = new JObject { ["r"] = 0.25f },
                    ["_DefinitelyUnknown"] = 1,
                    ["_Metallic"] = "not-a-number"
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            CollectionAssert.Contains(
                ((JArray)result["modifiedProperties"]).ToObject<string[]>(), "_Color");
            CollectionAssert.Contains(
                ((JArray)result["unknownProperties"]).ToObject<string[]>(), "_DefinitelyUnknown");
            JArray failedProperties = (JArray)result["failedProperties"];
            Assert.AreEqual(2, failedProperties.Count);
            Assert.That(failedProperties.ToString(), Does.Contain("_DefinitelyUnknown"));
            Assert.That(failedProperties.ToString(), Does.Contain("_Metallic"));
            Color updatedColor = material.GetColor("_Color");
            Assert.AreEqual(0.25f, updatedColor.r, 0.0001f);
            Assert.AreEqual(0.7f, updatedColor.g, 0.0001f);
            Assert.AreEqual(0.8f, updatedColor.b, 0.0001f);
            Assert.AreEqual(0.9f, updatedColor.a, 0.0001f);
        }

        [Test]
        public void WriteSerializedFields_PartialColorPreservesUnmentionedComponents()
        {
            GameObject gameObject = Spawn("ConverterFidelity_PartialColor");
            ConverterFidelityBehaviour behaviour = gameObject.AddComponent<ConverterFidelityBehaviour>();
            behaviour.color = new Color(0.2f, 0.3f, 0.4f, 0.5f);

            JObject result = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldData"] = new JObject
                {
                    ["color"] = new JObject { ["r"] = 0.9f }
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.AreEqual(0.9f, behaviour.color.r, 0.0001f);
            Assert.AreEqual(0.3f, behaviour.color.g, 0.0001f);
            Assert.AreEqual(0.4f, behaviour.color.b, 0.0001f);
            Assert.AreEqual(0.5f, behaviour.color.a, 0.0001f);
        }

        [Test]
        public void WriteSerializedFields_ManagedFlagsCombinationAcceptsDefinedBitsAndRejectsUnknownBit()
        {
            GameObject gameObject = Spawn("ConverterFidelity_ManagedFlags");
            ConverterFidelityBehaviour behaviour = gameObject.AddComponent<ConverterFidelityBehaviour>();

            JObject validResult = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldData"] = new JObject
                {
                    ["flags"] = 3
                }
            });

            Assert.IsTrue(validResult["success"].ToObject<bool>());
            Assert.AreEqual(3, (int)behaviour.flags);
            var serializedObject = new SerializedObject(behaviour);
            Assert.AreEqual(-1, serializedObject.FindProperty("flags").enumValueIndex);

            JObject invalidResult = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldData"] = new JObject { ["flags"] = 4 }
            });

            Assert.IsFalse(invalidResult["success"].ToObject<bool>());
            Assert.AreEqual(3, (int)behaviour.flags);
            Assert.That(invalidResult["failedFields"].ToString(), Does.Contain("Valid names"));
        }

        [Test]
        public void WriteSerializedFields_NoSuccessfulWriteDoesNotDirtyGameObject()
        {
            GameObject gameObject = Spawn("ConverterFidelity_NoDirtyOnFailure");
            gameObject.AddComponent<ConverterFidelityBehaviour>();
            EditorUtility.ClearDirty(gameObject);
            Assert.IsFalse(EditorUtility.IsDirty(gameObject));

            JObject result = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldData"] = new JObject { ["notAField"] = 1 }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.IsFalse(EditorUtility.IsDirty(gameObject));
        }

        [Test]
        public void UpdateComponent_ObjectReferencePropertyDoesNotReadMutatingGetter()
        {
            GameObject gameObject = Spawn("ConverterFidelity_MutatingGetter");
            gameObject.AddComponent<ConverterFidelityBehaviour>();

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["componentData"] = new JObject
                {
                    ["ObjectReferenceProperty"] = JValue.CreateNull()
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.AreEqual(0, ConverterFidelityBehaviour.ObjectGetterReadCount);
        }

        [Test]
        public void UpdateComponent_SerializableClassPropertyPartialWritePreservesUnmentionedField()
        {
            GameObject gameObject = Spawn("ConverterFidelity_PropertyStats");
            ConverterFidelityBehaviour behaviour = gameObject.AddComponent<ConverterFidelityBehaviour>();
            behaviour.StatsProperty = new ConverterFidelityStats { hp = 11, stamina = 37 };

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["componentData"] = new JObject
                {
                    ["StatsProperty"] = new JObject { ["hp"] = 5 }
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.AreEqual(5, behaviour.StatsProperty.hp);
            Assert.AreEqual(37, behaviour.StatsProperty.stamina);
        }

        [Test]
        public void CreatePrefab_Vector2IntArrayPropertyPartialElementPreservesSeed()
        {
            string prefabName = TestAssetDirectory + "/PropertyCells";

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabName,
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldValues"] = new JObject
                {
                    ["CellsProperty"] = new JArray(new JObject { ["x"] = 3 })
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            string prefabPath = result["prefabPath"].ToObject<string>();

            // ConverterFidelityBehaviour 住喺 Editor 測試 assembly：Unity 唔會為佢起可 attach 嘅
            // MonoScript（prefab 載返嚟 GetComponent 會 null）。序列化真相直接斷言磁碟 YAML ——
            // partial element write {x:3} 必須保住 seed 嘅 y=2。
            string prefabYaml = File.ReadAllText(Path.GetFullPath(prefabPath));
            Assert.That(prefabYaml, Does.Contain("cellsProperty"));
            Assert.That(prefabYaml, Does.Contain("{x: 3, y: 2}"));
        }

        [Test]
        public void UpdateComponent_RendererMaterialArrayFailureDoesNotReadMaterializingGetter()
        {
            GameObject gameObject = Spawn("ConverterFidelity_RendererMaterials");
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            var sharedMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"))
            {
                name = "ConverterFidelity_SharedMaterial"
            };
            renderer.sharedMaterial = sharedMaterial;

            try
            {
                JObject result = new UpdateComponentTool().Execute(new JObject
                {
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["componentName"] = "MeshRenderer",
                    ["componentData"] = new JObject
                    {
                        ["materials"] = new JArray(
                            new JObject { ["instanceId"] = 0 })
                    }
                });

                Assert.IsFalse(result["success"].ToObject<bool>());
                Assert.AreSame(sharedMaterial, renderer.sharedMaterial);
                Assert.That(renderer.sharedMaterial.name, Does.Not.Contain("Instance"));
            }
            finally
            {
                Material assignedMaterial = renderer.sharedMaterial;
                renderer.sharedMaterial = null;
                if (assignedMaterial != null && assignedMaterial != sharedMaterial)
                {
                    UnityEngine.Object.DestroyImmediate(assignedMaterial);
                }
                UnityEngine.Object.DestroyImmediate(sharedMaterial);
            }
        }

        [Test]
        public void CreatePrefab_ObjectReferencePropertyDoesNotReadMutatingGetter()
        {
            string prefabName = TestAssetDirectory + "/NoGetterRead";

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabName,
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldValues"] = new JObject
                {
                    ["ObjectReferenceProperty"] = JValue.CreateNull()
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.AreEqual(0, ConverterFidelityBehaviour.ObjectGetterReadCount);
        }

        [Test]
        public void UpdateComponent_EmptyFieldNameReturnsFailedField()
        {
            GameObject gameObject = Spawn("ConverterFidelity_EmptyField");
            gameObject.AddComponent<ConverterFidelityBehaviour>();

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["componentData"] = new JObject { [""] = 1 }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            JArray failedFields = (JArray)result["failedFields"];
            Assert.AreEqual(1, failedFields.Count);
            Assert.AreEqual(string.Empty, failedFields[0]["field"].ToString());
        }

        [Test]
        public void CreatePrefab_FieldValuesWithoutComponentNameFailsWithoutCreatingAsset()
        {
            string prefabName = TestAssetDirectory + "/MissingComponentName";
            string prefabPath = prefabName + ".prefab";

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabName,
                ["fieldValues"] = new JObject { ["number"] = 1 }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual(1, ((JArray)result["failedFields"]).Count);
            Assert.AreEqual(
                string.Empty,
                AssetDatabase.AssetPathToGUID(
                    prefabPath, AssetPathToGUIDOptions.OnlyExistingAssets));
        }

        [UnityTest]
        public IEnumerator BatchExecute_AtomicWithoutStopOnError_ReturnsValidationError()
        {
            var tool = new BatchExecuteTool(McpUnityServer.Instance);
            var completion = new TaskCompletionSource<JObject>();
            tool.ExecuteAsync(new JObject
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
                ["stopOnError"] = false
            }, completion);

            while (!completion.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = completion.Task.Result;
            Assert.AreEqual("validation_error", result["error"]["type"].ToString());
            Assert.That(result["error"]["message"].ToString(), Does.Contain("atomic requires stopOnError"));
        }

        [UnityTest]
        public IEnumerator BatchExecute_FailedOperation_PreservesToolResultPayload()
        {
            GameObject target = Spawn("ConverterFidelity_BatchTarget");
            var tool = new BatchExecuteTool(McpUnityServer.Instance);
            var completion = new TaskCompletionSource<JObject>();
            tool.ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "update_component",
                        ["params"] = new JObject
                        {
                            ["instanceId"] = target.GetInstanceID(),
                            ["componentName"] = "Transform",
                            ["componentData"] = new JObject
                            {
                                ["notAField"] = 1
                            }
                        }
                    }
                }
            }, completion);

            while (!completion.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = completion.Task.Result;
            JObject operation = (JObject)((JArray)result["results"])[0];
            Assert.IsFalse(operation["success"].ToObject<bool>());
            Assert.IsNotNull(operation["error"]);
            Assert.IsNotNull(operation["result"]);
            Assert.AreEqual(1, ((JArray)operation["result"]["failedFields"]).Count);
        }

        [Test]
        public void VerifyObjectReferenceWrite_WhenUnityPreservedPreviousValue_DoesNotRewriteSerializedData()
        {
            string holderPath = TestAssetDirectory + "/Holder.asset";
            string referencedPath = TestAssetDirectory + "/Referenced.asset";
            var holder = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            var referenced = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            AssetDatabase.CreateAsset(referenced, referencedPath);
            string referencedGuid = AssetDatabase.AssetPathToGUID(referencedPath);
            holder.reference = referenced;
            AssetDatabase.CreateAsset(holder, holderPath);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(AssetDatabase.DeleteAsset(referencedPath));
            AssetDatabase.Refresh();
            holder = AssetDatabase.LoadAssetAtPath<ConverterFidelityScriptableObject>(holderPath);
            var serializedObject = new SerializedObject(holder);
            SerializedProperty property = serializedObject.FindProperty("reference");
            Assert.IsNotNull(property);
            Assert.IsTrue(property.objectReferenceValue == null);
            string absoluteHolderPath = Path.GetFullPath(holderPath);
            string before = File.ReadAllText(absoluteHolderPath);
            Assert.That(before, Does.Contain(referencedGuid), "Test setup must retain a Missing reference GUID");

            var attempted = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            try
            {
                var write = new SerializedPropertyHelper.ObjectReferenceWrite(
                    property.objectReferenceValue, attempted, false);

                bool verified = SerializedPropertyHelper.VerifyObjectReferenceWrite(
                    holder,
                    serializedObject,
                    property,
                    property.propertyPath,
                    write,
                    out string failureReason);

                AssetDatabase.SaveAssets();
                string after = File.ReadAllText(absoluteHolderPath);
                Assert.IsFalse(verified);
                Assert.That(failureReason, Does.Contain("did not retain"));
                Assert.AreEqual(before, after, "Verification must not clear the Missing reference fileID/GUID");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(attempted);
            }
        }

        [Test]
        public void Converter_StructuredReference_StaleLocatorFallsBackWithDisclosure()
        {
            GameObject fallback = Spawn("ConverterFidelity_FallbackObject");
            var failures = new List<string>();
            var warnings = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["instanceId"] = 0,
                    ["objectPath"] = fallback.name
                },
                typeof(Transform),
                null,
                failures,
                warnings);

            Assert.AreSame(fallback.transform, converted);
            Assert.That(failures, Is.Empty);
            Assert.That(warnings, Has.Some.Contains("Locator 'instanceId'"));
            Assert.That(warnings, Has.Some.Contains("via locator 'objectPath'"));
        }

        [Test]
        public void SerializedProperty_StructuredReference_StaleLocatorFallsBackWithDisclosure()
        {
            GameObject fallback = Spawn("ConverterFidelity_SerializedFallback");
            var holder = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            try
            {
                var serializedObject = new SerializedObject(holder);
                SerializedProperty property = serializedObject.FindProperty("reference");
                Assert.IsNotNull(property);
                var warnings = new List<string>();

                bool written = SerializedPropertyHelper.SetValue(
                    property,
                    new JObject
                    {
                        ["instanceId"] = 0,
                        ["objectPath"] = fallback.name
                    },
                    warnings,
                    "reference");

                Assert.IsTrue(written);
                Assert.That(warnings, Has.Some.Contains("instanceId"));
                Assert.That(warnings, Has.Some.Contains("via locator 'objectPath'"));
                Assert.AreSame(fallback, property.objectReferenceValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void Converter_StructuredReference_AllLocatorsFailReportsEveryKey()
        {
            var failures = new List<string>();
            var warnings = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                new JObject
                {
                    ["assetPath"] = TestAssetDirectory + "/Missing.asset",
                    ["instanceId"] = 0,
                    ["objectPath"] = "ConverterFidelity/MissingObject"
                },
                typeof(Transform),
                null,
                failures,
                warnings);

            Assert.IsNull(converted);
            Assert.That(warnings, Is.Empty);
            Assert.That(failures, Has.Count.EqualTo(3));
            Assert.That(failures, Has.Some.Contains("Locator 'assetPath'"));
            Assert.That(failures, Has.Some.Contains("Locator 'instanceId'"));
            Assert.That(failures, Has.Some.Contains("Locator 'objectPath'"));
        }

        [Test]
        public void SerializedProperty_StructuredReference_AllLocatorsFailReportsEveryKey()
        {
            var holder = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            try
            {
                var serializedObject = new SerializedObject(holder);
                SerializedProperty property = serializedObject.FindProperty("reference");
                var warnings = new List<string>();

                bool written = SerializedPropertyHelper.SetValue(
                    property,
                    new JObject
                    {
                        ["assetPath"] = TestAssetDirectory + "/Missing.asset",
                        ["instanceId"] = 0,
                        ["objectPath"] = "ConverterFidelity/MissingObject"
                    },
                    warnings,
                    "reference");

                Assert.IsFalse(written);
                Assert.That(warnings, Has.Count.EqualTo(3));
                Assert.That(warnings, Has.Some.Contains("Locator 'assetPath'"));
                Assert.That(warnings, Has.Some.Contains("Locator 'instanceId'"));
                Assert.That(warnings, Has.Some.Contains("Locator 'objectPath'"));
                Assert.IsNull(property.objectReferenceValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void StructuredReference_ReaderDescriptiveKeysRoundTripWithWarnings()
        {
            GameObject referenced = Spawn("ConverterFidelity_DescriptiveReference");
            JObject readerShape = new JObject
            {
                ["instanceId"] = referenced.GetInstanceID(),
                ["name"] = referenced.name,
                ["type"] = referenced.GetType().Name,
                ["assetPath"] = JValue.CreateNull()
            };
            var converterFailures = new List<string>();
            var converterWarnings = new List<string>();

            object converted = SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                readerShape,
                typeof(UnityEngine.Object),
                null,
                converterFailures,
                converterWarnings);

            var holder = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            try
            {
                var serializedObject = new SerializedObject(holder);
                SerializedProperty property = serializedObject.FindProperty("reference");
                var warnings = new List<string>();
                bool written = SerializedPropertyHelper.SetValue(
                    property, readerShape, warnings, "reference");

                Assert.AreSame(referenced, converted);
                Assert.That(converterFailures, Is.Empty);
                Assert.That(converterWarnings, Has.Some.Contains("Locator 'assetPath'"));
                Assert.That(converterWarnings, Has.Some.Contains("via locator 'instanceId'"));
                Assert.IsTrue(written);
                Assert.AreSame(referenced, property.objectReferenceValue);
                Assert.That(warnings, Has.Some.Contains("Ignored descriptive keys"));
                Assert.That(warnings, Has.Some.Contains("Locator 'assetPath'"));
                Assert.That(warnings, Has.Some.Contains("via locator 'instanceId'"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void SerializedField_ObjectReferenceReadThenWriteUsesAssetPathWhenInstanceIdIsStale()
        {
            string assetPath = TestAssetDirectory + "/RoundTripReference.asset";
            var referenced = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            AssetDatabase.CreateAsset(referenced, assetPath);
            AssetDatabase.SaveAssets();

            GameObject gameObject = Spawn("ConverterFidelity_AssetRoundTrip");
            ConverterFidelityBehaviour behaviour = gameObject.AddComponent<ConverterFidelityBehaviour>();
            behaviour.assetReference = referenced;

            JObject readResult = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldNames"] = new JArray("assetReference")
            });
            var readerShape = (JObject)readResult["fields"]["assetReference"].DeepClone();
            readerShape["instanceId"] = 0;

            JObject writeResult = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldData"] = new JObject { ["assetReference"] = readerShape }
            });

            Assert.IsTrue(writeResult["success"].ToObject<bool>());
            Assert.AreSame(referenced, behaviour.assetReference);
            Assert.AreEqual(assetPath, readerShape["assetPath"].ToString());
        }

        [Test]
        public void SerializedFieldReader_EnumUsesUnderlyingValueAndReportsIndex()
        {
            GameObject gameObject = Spawn("ConverterFidelity_ReaderCamera");
            Camera camera = gameObject.AddComponent<Camera>();
            var serializedObject = new SerializedObject(camera);
            SerializedProperty property = serializedObject.FindProperty("m_ClearFlags");
            Assert.IsNotNull(property);
            property.intValue = (int)CameraClearFlags.Depth;
            serializedObject.ApplyModifiedProperties();
            int expectedIndex = property.enumValueIndex;

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = "Camera",
                ["fieldNames"] = new JArray("m_ClearFlags")
            });

            JObject enumValue = (JObject)result["fields"]["m_ClearFlags"];
            Assert.AreEqual((int)CameraClearFlags.Depth, enumValue["value"].ToObject<int>());
            Assert.AreEqual(expectedIndex, enumValue["index"].ToObject<int>());
            Assert.AreNotEqual(enumValue["value"].ToObject<int>(), enumValue["index"].ToObject<int>());
        }

        [Test]
        public void SerializedFieldReader_UndefinedEnumIndexUsesUnderlyingValueAsName()
        {
            GameObject gameObject = Spawn("ConverterFidelity_ReaderFlags");
            ConverterFidelityBehaviour behaviour = gameObject.AddComponent<ConverterFidelityBehaviour>();
            behaviour.flags = ConverterFidelityFlags.First | ConverterFidelityFlags.Second;

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = gameObject.GetInstanceID(),
                ["componentName"] = typeof(ConverterFidelityBehaviour).FullName,
                ["fieldNames"] = new JArray("flags")
            });

            JObject enumValue = (JObject)result["fields"]["flags"];
            Assert.AreEqual(3, enumValue["value"].ToObject<int>());
            Assert.AreEqual(-1, enumValue["index"].ToObject<int>());
            Assert.AreEqual("3", enumValue["name"].ToString());
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawnedObjects.Add(gameObject);
            return gameObject;
        }
    }
}
