using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace McpUnity.Tests
{
    [Serializable]
    internal class JsonFieldWriteNestedValue
    {
        public bool enabled = true;
    }

    [Serializable]
    internal struct JsonFieldWritePropertyBackedValue
    {
        private int m_Value;

        public int value
        {
            get { return m_Value; }
            set { m_Value = value; }
        }
    }

    [TestFixture]
    public class JsonFieldWriteSafetyTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _spawned.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
                }
            }
            _spawned.Clear();
        }
        [Test]
        public void WriteSerializedFields_RestoresExistingComponentReference_WhenResolvedTypeIsIncompatible()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_Target");
            Renderer renderer = target.GetComponent<Renderer>();
            Transform originalAnchor = Spawn("McpJsonWrite_OriginalAnchor").transform;
            GameObject incompatible = Spawn("McpJsonWrite_Incompatible");
            SetProbeAnchor(renderer, originalAnchor);

            var result = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = renderer.GetType().Name,
                ["fieldData"] = new JObject
                {
                    ["m_ProbeAnchor"] = new JObject
                    {
                        ["objectPath"] = incompatible.name
                    }
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.IsTrue(GetProbeAnchor(renderer) == originalAnchor);
            Assert.AreEqual(0, ((JArray)result["updatedFields"]).Count);
            JObject failure = (JObject)((JArray)result["failedFields"])[0];
            Assert.AreEqual("m_ProbeAnchor", failure["field"].ToString());
            Assert.That(failure["reason"].ToString(), Does.Contain("GameObject"));
            Assert.That(failure["reason"].ToString(), Does.Contain("not assignable"));
            Assert.That(result["message"].ToString(), Does.Contain("0 field(s) succeeded"));
            Assert.That(result["message"].ToString(), Does.Contain("1 field(s) failed"));
        }
        [Test]
        public void WriteSerializedFields_AllowsIntentionalNull_ForObjectReference()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_ClearTarget");
            Renderer renderer = target.GetComponent<Renderer>();
            Transform originalAnchor = Spawn("McpJsonWrite_ClearAnchor").transform;
            SetProbeAnchor(renderer, originalAnchor);

            var result = new WriteSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = renderer.GetType().Name,
                ["fieldData"] = new JObject
                {
                    ["m_ProbeAnchor"] = JValue.CreateNull()
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.IsNull(GetProbeAnchor(renderer));
            Assert.AreEqual(1, ((JArray)result["updatedFields"]).Count);
            Assert.AreEqual(0, ((JArray)result["failedFields"]).Count);
        }

        [Test]
        public void WriteSerializedFields_WritesCompatibleObjectReference_AtNestedPropertyPath()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_NestedPathTarget");
            Renderer renderer = target.GetComponent<Renderer>();
            var requestedMaterial = new Material(Shader.Find("Standard"));

            try
            {
                var result = new WriteSerializedFieldsTool().Execute(new JObject
                {
                    ["instanceId"] = target.GetInstanceID(),
                    ["componentName"] = renderer.GetType().Name,
                    ["fieldData"] = new JObject
                    {
                        ["m_Materials.Array.data[0]"] = requestedMaterial.GetInstanceID()
                    }
                });

                Assert.IsTrue(result["success"].ToObject<bool>());
                Assert.IsTrue(renderer.sharedMaterial == requestedMaterial);
                CollectionAssert.Contains(
                    ((JArray)result["updatedFields"]).ToObject<string[]>(),
                    "m_Materials.Array.data[0]");
                Assert.AreEqual(0, ((JArray)result["failedFields"]).Count);
            }
            finally
            {
                renderer.sharedMaterial = null;
                UnityEngine.Object.DestroyImmediate(requestedMaterial);
            }
        }

        [Test]
        public void UpdateComponent_WritesCompatibleSerializedObjectReference()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_UpdateReferenceTarget");
            Renderer renderer = target.GetComponent<Renderer>();
            Transform previousAnchor = Spawn("McpJsonWrite_PreviousAnchor").transform;
            Transform requestedAnchor = Spawn("McpJsonWrite_RequestedAnchor").transform;
            SetProbeAnchor(renderer, previousAnchor);

            var result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = renderer.GetType().Name,
                ["componentData"] = new JObject
                {
                    ["m_ProbeAnchor"] = requestedAnchor.GetInstanceID()
                }
            });

            Assert.IsTrue(result["success"].ToObject<bool>());
            Assert.IsTrue(GetProbeAnchor(renderer) == requestedAnchor);
            CollectionAssert.Contains(
                ((JArray)result["updatedFields"]).ToObject<string[]>(),
                "m_ProbeAnchor");
            Assert.AreEqual(0, ((JArray)result["failedFields"]).Count);
        }

        [Test]
        public void UpdateComponent_DoesNotWriteDefaultValue_WhenConversionFails()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_ValueTarget");
            Renderer renderer = target.GetComponent<Renderer>();
            renderer.enabled = true;

            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Error converting value to type Boolean:"));
            var result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = renderer.GetType().Name,
                ["componentData"] = new JObject
                {
                    ["enabled"] = "yes-please",
                    ["sharedMaterial"] = JValue.CreateNull()
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.IsTrue(renderer.enabled, "Failed Boolean conversion must preserve the existing value");
            Assert.IsNull(renderer.sharedMaterial, "A later valid field must still be applied");
            CollectionAssert.Contains(((JArray)result["updatedFields"]).ToObject<string[]>(), "sharedMaterial");
            JObject failure = (JObject)((JArray)result["failedFields"])[0];
            Assert.AreEqual("enabled", failure["field"].ToString());
            Assert.That(result["message"].ToString(), Does.Contain("1 field(s) succeeded"));
            Assert.That(result["message"].ToString(), Does.Contain("1 field(s) failed"));
        }

        [Test]
        public void UpdateComponent_ReportsUnknownFieldAsFailure()
        {
            GameObject target = SpawnPrimitive("McpJsonWrite_UnknownFieldTarget");
            Renderer renderer = target.GetComponent<Renderer>();
            renderer.enabled = true;

            var result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = renderer.GetType().Name,
                ["componentData"] = new JObject
                {
                    ["enabled"] = false,
                    ["m_TotallyBogusField"] = 123
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.IsFalse(renderer.enabled);
            Assert.IsNull(result["warnings"]);
            CollectionAssert.Contains(
                ((JArray)result["updatedFields"]).ToObject<string[]>(),
                "enabled");
            JObject failure = (JObject)((JArray)result["failedFields"])[0];
            Assert.AreEqual("m_TotallyBogusField", failure["field"].ToString());
            Assert.That(failure["reason"].ToString(), Does.Contain("reflection field"));
            Assert.That(failure["reason"].ToString(), Does.Contain("reflection property"));
            Assert.That(failure["reason"].ToString(), Does.Contain("SerializedProperty"));
            Assert.That(failure["reason"].ToString(), Does.Contain(renderer.GetType().Name));
        }

        [Test]
        public void UpdateComponent_ContinuesAfterPerFieldException()
        {
            GameObject target = Spawn("McpJsonWrite_ExceptionTarget");
            PropertyInfo gameObjectProperty = typeof(Transform).GetProperty("gameObject");
            Assert.IsNotNull(gameObjectProperty);
            Assert.IsFalse(gameObjectProperty.CanWrite, "Component.gameObject must remain setter-free for this probe");

            var result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = target.transform.GetType().Name,
                ["componentData"] = new JObject
                {
                    ["gameObject"] = target.GetInstanceID(),
                    ["localPosition"] = new JObject
                    {
                        ["x"] = 4f,
                        ["y"] = 5f,
                        ["z"] = 6f
                    }
                }
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual(new Vector3(4f, 5f, 6f), target.transform.localPosition);
            JObject failure = (JObject)((JArray)result["failedFields"])[0];
            Assert.AreEqual("gameObject", failure["field"].ToString());
            Assert.That(
                failure["reason"].ToString(),
                Does.Contain("Exception while setting field 'gameObject'"));
            Assert.That(failure["reason"].ToString(), Does.Match("(?i)set"));
        }

        [Test]
        public void Converter_ReportsNestedSerializableFieldFailure_WithoutWritingDefault()
        {
            var failures = new List<string>();
            LogAssert.Expect(
                LogType.Error,
                new Regex(@"Error converting value to type Boolean:"));
            object converted = SerializedFieldConverter.ConvertJTokenToValue(
                new JObject { ["enabled"] = "yes-please" },
                typeof(JsonFieldWriteNestedValue),
                failures);

            Assert.IsNull(converted);
            Assert.That(failures, Is.Not.Empty);
            Assert.That(failures[0], Does.Contain("Boolean"));
        }

        [Test]
        public void Converter_FallsBackToNewtonsoft_WhenSerializableWalkerMatchesNoFields()
        {
            var failures = new List<string>();
            object converted = SerializedFieldConverter.ConvertJTokenToValue(
                new JObject { ["value"] = 37 },
                typeof(JsonFieldWritePropertyBackedValue),
                failures);

            Assert.IsNotNull(converted);
            Assert.AreEqual(37, ((JsonFieldWritePropertyBackedValue)converted).value);
            Assert.That(failures, Is.Empty);
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }

        private GameObject SpawnPrimitive(string name)
        {
            GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            _spawned.Add(gameObject);
            return gameObject;
        }

        private static void SetProbeAnchor(Renderer renderer, Transform anchor)
        {
            var serializedObject = new SerializedObject(renderer);
            SerializedProperty property = SerializedPropertyHelper.FindProperty(serializedObject, "m_ProbeAnchor");
            Assert.IsNotNull(property, "Renderer must expose m_ProbeAnchor for this regression probe");
            property.objectReferenceValue = anchor;
            serializedObject.ApplyModifiedProperties();
        }

        private static UnityEngine.Object GetProbeAnchor(Renderer renderer)
        {
            var serializedObject = new SerializedObject(renderer);
            SerializedProperty property = SerializedPropertyHelper.FindProperty(serializedObject, "m_ProbeAnchor");
            Assert.IsNotNull(property, "Renderer must expose m_ProbeAnchor for this regression probe");
            return property.objectReferenceValue;
        }
    }
}
