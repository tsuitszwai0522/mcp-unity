using System;
using System.Collections.Generic;
using System.IO;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public enum ConvertEnumReaderShapeMode
    {
        Off = 0,
        Low = 2,
        High = 5
    }

    public enum ConvertEnumReaderShapeOutOfOrderMode
    {
        High = 5,
        Low = 2,
        Off = 0
    }

    public enum ConvertEnumReaderShapeDisplayMode
    {
        Off = 0,
        [InspectorName("Maximum")]
        High = 5
    }

    [Flags]
    public enum ConvertEnumReaderShapeFlags
    {
        None = 0,
        Read = 1,
        Write = 2
    }

    public class ConvertEnumReaderShapeProbeBehaviour : MonoBehaviour
    {
        public ConvertEnumReaderShapeMode mode = ConvertEnumReaderShapeMode.Off;
        public ConvertEnumReaderShapeOutOfOrderMode outOfOrder =
            ConvertEnumReaderShapeOutOfOrderMode.Off;
        public ConvertEnumReaderShapeFlags flags = ConvertEnumReaderShapeFlags.None;
    }

    public class ConvertEnumReaderShapeScriptableObject : ScriptableObject
    {
        public ConvertEnumReaderShapeOutOfOrderMode mode =
            ConvertEnumReaderShapeOutOfOrderMode.Off;
    }

    [TestFixture]
    public class ConvertEnumReaderShapeTests
    {
        private const string AssetDirectory = "Assets/ConvertEnumReaderShapeTests";
        private const string ScriptableObjectPath = AssetDirectory + "/Probe.asset";
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
                AssetDatabase.CreateFolder("Assets", "ConvertEnumReaderShapeTests");
            Assert.IsFalse(string.IsNullOrEmpty(folderGuid));
            _ownsAssetDirectory = true;
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
        public void ValueOnlyReaderShape_UsesExistingStringConversion()
        {
            object converted = Convert(
                new JObject { ["value"] = "High" },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(ConvertEnumReaderShapeMode.High, converted);
            Assert.IsEmpty(failures);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void CompleteConsistentReaderShape_HasNoWarnings()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = 5,
                    ["index"] = 2,
                    ["name"] = "high"
                },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(ConvertEnumReaderShapeMode.High, converted);
            Assert.IsEmpty(failures);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void NameMismatch_WarnsAndUsesValue()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = 5,
                    ["name"] = "Low"
                },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(ConvertEnumReaderShapeMode.High, converted);
            Assert.IsEmpty(failures);
            Assert.That(string.Join("; ", warnings), Does.Contain("supplied name 'Low'"));
            Assert.That(string.Join("; ", warnings), Does.Contain("Used 'value'"));
        }

        [Test]
        public void IndexMismatch_WarnsAndUsesValue()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = 2,
                    ["index"] = 2
                },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(ConvertEnumReaderShapeMode.Low, converted);
            Assert.IsEmpty(failures);
            Assert.That(string.Join("; ", warnings), Does.Contain("resolved to index 1"));
        }

        [Test]
        public void UnknownReaderShapeKey_FailsAndListsAllowedKeys()
        {
            object converted = Convert(
                new JObject { ["value"] = 5, ["label"] = "High" },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out _);

            Assert.IsNull(converted);
            Assert.That(string.Join("; ", failures), Does.Contain("Unknown enum key 'label'"));
            Assert.That(string.Join("; ", failures), Does.Contain("value, index, name"));
        }

        [Test]
        public void MissingValue_Fails()
        {
            object converted = Convert(
                new JObject { ["name"] = "High", ["index"] = 2 },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out _);

            Assert.IsNull(converted);
            Assert.That(string.Join("; ", failures), Does.Contain("must include 'value'"));
        }

        [Test]
        public void FlagsCombination_AcceptsReaderShapeAndSemanticName()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = 3,
                    ["index"] = -1,
                    ["name"] = "Read, Write"
                },
                typeof(ConvertEnumReaderShapeFlags),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(
                ConvertEnumReaderShapeFlags.Read | ConvertEnumReaderShapeFlags.Write,
                converted);
            Assert.IsEmpty(failures);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void InvalidNumericValue_FailsAndListsLegalValues()
        {
            object converted = Convert(
                new JObject { ["value"] = 4 },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out _);

            Assert.IsNull(converted);
            Assert.That(string.Join("; ", failures), Does.Contain("invalid"));
            Assert.That(string.Join("; ", failures), Does.Contain("Off=0, Low=2, High=5"));
        }

        [Test]
        public void NestedReaderShapeValue_FailsInsteadOfInventingRecursiveGrammar()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = new JObject { ["value"] = 5 }
                },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out _);

            Assert.IsNull(converted);
            Assert.That(
                string.Join("; ", failures),
                Does.Contain("Expected an enum name or integer"));
        }

        [Test]
        public void SerializedPropertyReaderShape_RejectsNonNameAndNonIntegerValuesLoudly()
        {
            var gameObject = new GameObject("ConvertEnumReaderShapeInvalidValues");
            try
            {
                ConvertEnumReaderShapeProbeBehaviour probe =
                    gameObject.AddComponent<ConvertEnumReaderShapeProbeBehaviour>();
                var serializedObject = new SerializedObject(probe);
                SerializedProperty property = serializedObject.FindProperty("outOfOrder");
                JToken[] invalidValues =
                {
                    new JValue(1.5),
                    new JValue(true),
                    JValue.CreateNull(),
                    new JObject { ["value"] = 5 }
                };

                foreach (JToken invalidValue in invalidValues)
                {
                    var warnings = new List<string>();
                    bool written = SerializedPropertyHelper.SetValue(
                        property,
                        new JObject { ["value"] = invalidValue },
                        warnings,
                        "outOfOrder");

                    Assert.IsFalse(written, invalidValue.ToString());
                    Assert.That(
                        string.Join("; ", warnings),
                        Does.Contain("Expected an enum name or integer"));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void InspectorDisplayNameMatchingResolvedMemberDoesNotWarn()
        {
            object converted = Convert(
                new JObject
                {
                    ["value"] = 5,
                    ["name"] = "maximum"
                },
                typeof(ConvertEnumReaderShapeDisplayMode),
                out List<string> failures,
                out List<string> warnings);

            Assert.AreEqual(ConvertEnumReaderShapeDisplayMode.High, converted);
            Assert.IsEmpty(failures);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void InvalidStringValue_FailsAndListsLegalValues()
        {
            object converted = Convert(
                new JObject { ["value"] = "Turbo" },
                typeof(ConvertEnumReaderShapeMode),
                out List<string> failures,
                out _);

            Assert.IsNull(converted);
            Assert.That(string.Join("; ", failures), Does.Contain("Turbo"));
            Assert.That(string.Join("; ", failures), Does.Contain("Valid values"));
        }

        [Test]
        public void UpdateComponent_FieldInfoPathAcceptsReaderShape()
        {
            var gameObject = new GameObject("ConvertEnumReaderShapeProbe");
            try
            {
                ConvertEnumReaderShapeProbeBehaviour probe =
                    gameObject.AddComponent<ConvertEnumReaderShapeProbeBehaviour>();

                JObject result = new UpdateComponentTool().Execute(new JObject
                {
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["componentName"] = typeof(ConvertEnumReaderShapeProbeBehaviour).FullName,
                    ["componentData"] = new JObject
                    {
                        ["mode"] = new JObject
                        {
                            ["value"] = 5,
                            ["index"] = 0,
                            ["name"] = "Low"
                        }
                    }
                });

                Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
                Assert.AreEqual(ConvertEnumReaderShapeMode.High, probe.mode);
                Assert.That(
                    string.Join("; ", result["warnings"].ToObject<string[]>()),
                    Does.Contain("Reader-shaped enum metadata mismatch"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TrueReaderShapes_RoundTripThroughConverterAndSerializedPropertyWithoutWarnings()
        {
            var gameObject = new GameObject("ConvertEnumReaderShapeParity");
            try
            {
                ConvertEnumReaderShapeProbeBehaviour probe =
                    gameObject.AddComponent<ConvertEnumReaderShapeProbeBehaviour>();
                probe.outOfOrder = ConvertEnumReaderShapeOutOfOrderMode.High;
                probe.flags = ConvertEnumReaderShapeFlags.Read | ConvertEnumReaderShapeFlags.Write;

                JObject read = new ReadSerializedFieldsTool().Execute(new JObject
                {
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["componentName"] = typeof(ConvertEnumReaderShapeProbeBehaviour).FullName,
                    ["fieldNames"] = new JArray("outOfOrder", "flags")
                });
                JObject outOfOrderShape =
                    (JObject)read["fields"]["outOfOrder"].DeepClone();
                JObject flagsShape = (JObject)read["fields"]["flags"].DeepClone();
                Assert.AreEqual(3, flagsShape["value"].ToObject<int>());
                Assert.AreEqual(-1, flagsShape["index"].ToObject<int>());
                Assert.AreEqual("3", flagsShape["name"].ToString());

                probe.outOfOrder = ConvertEnumReaderShapeOutOfOrderMode.Off;
                probe.flags = ConvertEnumReaderShapeFlags.None;
                JObject converterResult = new UpdateComponentTool().Execute(new JObject
                {
                    ["instanceId"] = gameObject.GetInstanceID(),
                    ["componentName"] = typeof(ConvertEnumReaderShapeProbeBehaviour).FullName,
                    ["componentData"] = new JObject
                    {
                        ["outOfOrder"] = outOfOrderShape.DeepClone(),
                        ["flags"] = flagsShape.DeepClone()
                    }
                });

                Assert.IsTrue(
                    converterResult["success"].ToObject<bool>(),
                    converterResult.ToString());
                Assert.IsNull(converterResult["warnings"], converterResult.ToString());
                Assert.AreEqual(ConvertEnumReaderShapeOutOfOrderMode.High, probe.outOfOrder);
                Assert.AreEqual(
                    ConvertEnumReaderShapeFlags.Read | ConvertEnumReaderShapeFlags.Write,
                    probe.flags);

                probe.outOfOrder = ConvertEnumReaderShapeOutOfOrderMode.Off;
                probe.flags = ConvertEnumReaderShapeFlags.None;
                JObject serializedPropertyResult = new WriteSerializedFieldsTool().Execute(
                    new JObject
                    {
                        ["instanceId"] = gameObject.GetInstanceID(),
                        ["componentName"] = typeof(ConvertEnumReaderShapeProbeBehaviour).FullName,
                        ["fieldData"] = new JObject
                        {
                            ["outOfOrder"] = outOfOrderShape.DeepClone(),
                            ["flags"] = flagsShape.DeepClone()
                        }
                    });

                Assert.IsTrue(
                    serializedPropertyResult["success"].ToObject<bool>(),
                    serializedPropertyResult.ToString());
                Assert.IsNull(serializedPropertyResult["warnings"]);
                Assert.AreEqual(ConvertEnumReaderShapeOutOfOrderMode.High, probe.outOfOrder);
                Assert.AreEqual(
                    ConvertEnumReaderShapeFlags.Read | ConvertEnumReaderShapeFlags.Write,
                    probe.flags);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateScriptableObject_ReaderShapeIsAllOrNothing()
        {
            string invalidPath = AssetDirectory + "/InvalidCreate.asset";
            JObject invalidResult = new CreateScriptableObjectTool().Execute(new JObject
            {
                ["typeName"] = typeof(ConvertEnumReaderShapeScriptableObject).FullName,
                ["savePath"] = invalidPath,
                ["fieldValues"] = new JObject
                {
                    ["mode"] = new JObject
                    {
                        ["value"] = new JObject { ["value"] = 5 }
                    }
                }
            });

            Assert.IsFalse(invalidResult["success"].ToObject<bool>());
            Assert.IsNull(
                AssetDatabase.LoadAssetAtPath<ConvertEnumReaderShapeScriptableObject>(invalidPath));
            Assert.That(
                invalidResult["failedFields"].ToString(),
                Does.Contain("Expected an enum name or integer"));

            string validPath = AssetDirectory + "/ValidCreate.asset";
            JObject validResult = new CreateScriptableObjectTool().Execute(new JObject
            {
                ["typeName"] = typeof(ConvertEnumReaderShapeScriptableObject).FullName,
                ["savePath"] = validPath,
                ["fieldValues"] = new JObject
                {
                    ["mode"] = new JObject
                    {
                        ["value"] = 5,
                        ["index"] = 2,
                        ["name"] = "High"
                    }
                }
            });

            Assert.IsTrue(validResult["success"].ToObject<bool>(), validResult.ToString());
            ConvertEnumReaderShapeScriptableObject created =
                AssetDatabase.LoadAssetAtPath<ConvertEnumReaderShapeScriptableObject>(validPath);
            Assert.IsNotNull(created);
            Assert.AreEqual(ConvertEnumReaderShapeOutOfOrderMode.High, created.mode);
            Assert.IsNull(validResult["warnings"]);
        }

        [Test]
        public void UpdateScriptableObject_AcceptsReaderShape()
        {
            var probe = ScriptableObject.CreateInstance<ConverterFidelityScriptableObject>();
            AssetDatabase.CreateAsset(probe, ScriptableObjectPath);

            JObject result = new UpdateScriptableObjectTool().Execute(new JObject
            {
                ["assetPath"] = ScriptableObjectPath,
                ["fieldValues"] = new JObject
                {
                    ["flags"] = new JObject
                    {
                        ["value"] = 3,
                        ["index"] = -1,
                        ["name"] = "First, Second"
                    }
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual(
                ConverterFidelityFlags.First | ConverterFidelityFlags.Second,
                probe.flags);
            Assert.IsNull(result["warnings"]);
        }

        private static object Convert(
            JToken token,
            Type targetType,
            out List<string> failures,
            out List<string> warnings)
        {
            failures = new List<string>();
            warnings = new List<string>();
            return SerializedFieldConverter.ConvertJTokenToValueWithoutReferenceOwner(
                token, targetType, null, failures, warnings);
        }
    }
}
