using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace McpUnity.Tests
{
    [Serializable]
    public class UnityEventWiringIntEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public class UnityEventWiringPayload
    {
        public string label;
        public PersistentListenerMode mode;
    }

    public class UnityEventWiringProbe : MonoBehaviour
    {
        public UnityEvent noArgs = new UnityEvent();
        public UnityEventWiringIntEvent intEvent = new UnityEventWiringIntEvent();
        public List<UnityEventWiringPayload> payloads = new List<UnityEventWiringPayload>();

        public int receivedInt;
        public string receivedString;

        public void ReceiveInt(int value)
        {
            receivedInt = value;
        }

        public void ReceiveString(string value)
        {
            receivedString = value;
        }

        public void ReceiveAmbiguous(int value)
        {
            receivedInt = value;
        }

        public void ReceiveAmbiguous()
        {
            receivedInt = -1;
        }

        public void ReceiveNumber(int value)
        {
            receivedInt = value;
        }

        public void ReceiveNumber(float value)
        {
            receivedInt = (int)value;
        }
    }

    [Serializable]
    public class UnityEventNestedArrayPayload
    {
        public List<int> values = new List<int>();
    }

    public class UnityEventNestedArrayProbe : MonoBehaviour
    {
        public List<UnityEventNestedArrayPayload> groups =
            new List<UnityEventNestedArrayPayload>();
    }

    [TestFixture]
    public class UnityEventWiringTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

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
        public void WireUnityEvent_StaticBool_DerivesModeAndReadsBackRuntimeOnly()
        {
            UnityEventWiringProbe source = Spawn("WireEvent_StaticBool_Source")
                .AddComponent<UnityEventWiringProbe>();
            GameObject listener = Spawn("WireEvent_StaticBool_Listener");

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = source.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = listener.GetInstanceID(),
                ["methodName"] = "SetActive",
                ["staticArgument"] = false
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual("Bool", result["mode"]["name"].ToString());
            Assert.AreEqual(
                (int)PersistentListenerMode.Bool,
                result["mode"]["value"].ToObject<int>());
            Assert.AreEqual("SetActive", result["methodName"].ToString());
            Assert.AreEqual(
                (int)PersistentListenerMode.Bool,
                result["persistentCall"]["m_Mode"]["value"].ToObject<int>());

            Assert.AreEqual("RuntimeOnly", result["callState"]["name"].ToString());
            Assert.AreEqual(
                (int)UnityEventCallState.RuntimeOnly,
                result["persistentCall"]["m_CallState"]["value"].ToObject<int>());
            Assert.IsFalse(result["staticArgument"].ToObject<bool>());
        }

        [Test]
        public void WireUnityEvent_DynamicInt_DerivesEventDefinedAndReadsBackRuntimeOnly()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_DynamicInt")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveInt"
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual("EventDefined", result["mode"]["name"].ToString());
            Assert.AreEqual(
                (int)PersistentListenerMode.EventDefined,
                result["mode"]["value"].ToObject<int>());

            Assert.AreEqual("RuntimeOnly", result["callState"]["name"].ToString());
            Assert.AreEqual(
                (int)UnityEventCallState.RuntimeOnly,
                result["persistentCall"]["m_CallState"]["value"].ToObject<int>());
            Assert.AreEqual("ReceiveInt", result["persistentCall"]["m_MethodName"].ToString());
        }

        [Test]
        public void WireUnityEvent_StaticString_DoesNotFallBackToDynamicMode()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_StaticString")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveString",
                ["staticArgument"] = "localized-value"
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual("String", result["mode"]["name"].ToString());
            Assert.AreEqual("localized-value", result["staticArgument"].ToString());

            Assert.AreEqual("RuntimeOnly", result["callState"]["name"].ToString());
            Assert.AreEqual(
                "localized-value",
                result["persistentCall"]["m_Arguments"]["m_StringArgument"].ToString());
            Assert.AreEqual(
                (int)UnityEventCallState.RuntimeOnly,
                result["persistentCall"]["m_CallState"]["value"].ToObject<int>());
        }

        [Test]
        public void WireUnityEvent_RuntimeOnlyGateIsCausallyVerified()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_RuntimeOnlyGate")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveString",
                ["staticArgument"] = "causal-static-value"
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual("ReceiveString", result["methodName"].ToString());
            Assert.AreEqual("String", result["mode"]["name"].ToString());
            Assert.AreEqual(
                (int)PersistentListenerMode.String,
                result["mode"]["value"].ToObject<int>());
            Assert.AreEqual("causal-static-value", result["staticArgument"].ToString());
            Assert.AreEqual(
                UnityEventCallState.RuntimeOnly,
                probe.intEvent.GetPersistentListenerState(0));
            Assert.IsFalse(Application.isPlaying,
                "This causal control must run in EditMode.");

            probe.receivedString = null;
            probe.intEvent.Invoke(123);
            Assert.IsNull(
                probe.receivedString,
                "Negative control failed: a RuntimeOnly listener fired in EditMode.");

            probe.intEvent.SetPersistentListenerState(
                0,
                UnityEventCallState.EditorAndRuntime);
            Assert.AreEqual(
                UnityEventCallState.EditorAndRuntime,
                probe.intEvent.GetPersistentListenerState(0));
            probe.intEvent.Invoke(456);
            Assert.AreEqual("causal-static-value", probe.receivedString);
        }

        [Test]
        public void WireUnityEvent_MissingOrIncompatibleMethod_FailsWithoutMutation()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_Missing")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "DefinitelyMissing"
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual("method_not_found", result["error"]["type"].ToString());
            Assert.AreEqual(
                result["message"].ToString(),
                result["error"]["message"].ToString());
            Assert.AreEqual(0, probe.intEvent.GetPersistentEventCount());
        }

        [Test]
        public void WireUnityEvent_CallerSuppliedMode_IsRejectedWithoutMutation()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_RejectMode")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["methodName"] = "SetActive",
                ["staticArgument"] = true,
                ["m_Mode"] = PersistentListenerMode.String.ToString()
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual("validation_error", result["error"]["type"].ToString());
            StringAssert.Contains("cannot be supplied", result["error"]["message"].ToString());
            Assert.AreEqual(
                result["message"].ToString(),
                result["error"]["message"].ToString());
            Assert.AreEqual(0, probe.noArgs.GetPersistentEventCount());
        }

        [Test]
        public void WireUnityEvent_DynamicAndVoidOverloads_FailAsAmbiguous()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_Ambiguous")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveAmbiguous"
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual("method_ambiguity_error", result["error"]["type"].ToString());
            Assert.AreEqual(0, probe.intEvent.GetPersistentEventCount());
        }

        [Test]
        public void WireUnityEvent_IntegerMatchingIntAndFloatOverloads_FailsAsAmbiguous()
        {
            UnityEventWiringProbe probe = Spawn("WireEvent_NumericAmbiguous")
                .AddComponent<UnityEventWiringProbe>();

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = probe.gameObject.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveNumber",
                ["staticArgument"] = 5
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.AreEqual("method_ambiguity_error", result["error"]["type"].ToString());
            Assert.AreEqual(0, probe.noArgs.GetPersistentEventCount());
        }

        [Test]
        public void WireUnityEvent_DuplicateSourceAndListenerComponents_FailWithAllCandidates()
        {
            GameObject ambiguousSource = Spawn("WireEvent_DuplicateSource");
            UnityEventWiringProbe firstSource =
                ambiguousSource.AddComponent<UnityEventWiringProbe>();
            UnityEventWiringProbe secondSource =
                ambiguousSource.AddComponent<UnityEventWiringProbe>();
            GameObject listener = Spawn("WireEvent_DuplicateSourceListener");

            JObject sourceResult = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = ambiguousSource.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = listener.GetInstanceID(),
                ["methodName"] = "SetActive",
                ["staticArgument"] = true
            });

            Assert.IsFalse(sourceResult["success"].ToObject<bool>());
            Assert.AreEqual(
                "source_component_ambiguity_error",
                sourceResult["error"]["type"].ToString());
            CollectionAssert.AreEquivalent(
                new[] { firstSource.GetInstanceID(), secondSource.GetInstanceID() },
                sourceResult["error"]["details"]["candidates"]
                    .Select(candidate => candidate["instanceId"].ToObject<int>())
                    .ToArray());
            StringAssert.Contains(
                $"instanceId={firstSource.GetInstanceID()}",
                sourceResult["message"].ToString());
            StringAssert.Contains(
                $"instanceId={secondSource.GetInstanceID()}",
                sourceResult["message"].ToString());

            UnityEventWiringProbe uniqueSource = Spawn("WireEvent_UniqueSource")
                .AddComponent<UnityEventWiringProbe>();
            GameObject ambiguousListener = Spawn("WireEvent_DuplicateListener");
            UnityEventWiringProbe firstListener =
                ambiguousListener.AddComponent<UnityEventWiringProbe>();
            UnityEventWiringProbe secondListener =
                ambiguousListener.AddComponent<UnityEventWiringProbe>();

            JObject listenerResult = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = uniqueSource.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "intEvent",
                ["listenerInstanceId"] = ambiguousListener.GetInstanceID(),
                ["listenerComponentName"] = typeof(UnityEventWiringProbe).FullName,
                ["methodName"] = "ReceiveInt"
            });

            Assert.IsFalse(listenerResult["success"].ToObject<bool>());
            Assert.AreEqual(
                "listener_component_ambiguity_error",
                listenerResult["error"]["type"].ToString());
            CollectionAssert.AreEquivalent(
                new[] { firstListener.GetInstanceID(), secondListener.GetInstanceID() },
                listenerResult["error"]["details"]["candidates"]
                    .Select(candidate => candidate["instanceId"].ToObject<int>())
                    .ToArray());
            Assert.AreEqual(0, uniqueSource.intEvent.GetPersistentEventCount());
        }

        [Test]
        public void WireUnityEvent_SourceResolutionFailure_HasUniformFailureEnvelope()
        {
            GameObject listener = Spawn("WireEvent_ErrorEnvelopeListener");
            string missingPath = $"DefinitelyMissing-{Guid.NewGuid():N}";

            JObject result = new WireUnityEventTool().Execute(new JObject
            {
                ["objectPath"] = missingPath,
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = listener.GetInstanceID(),
                ["methodName"] = "SetActive",
                ["staticArgument"] = true
            });

            Assert.IsFalse(result["success"].ToObject<bool>());
            Assert.IsNotEmpty(result["message"].ToString());
            Assert.AreEqual(
                result["message"].ToString(),
                result["error"]["message"].ToString());
            Assert.IsNotEmpty(result["error"]["type"].ToString());
        }

        [Test]
        public void ReadSerializedFields_UnityEventExpandsCallsAndHonorsDepthLimit()
        {
            UnityEventWiringProbe source = Spawn("ReadEvent_Source")
                .AddComponent<UnityEventWiringProbe>();
            GameObject listener = Spawn("ReadEvent_Listener");
            JObject wireResult = new WireUnityEventTool().Execute(new JObject
            {
                ["instanceId"] = source.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["eventFieldName"] = "noArgs",
                ["listenerInstanceId"] = listener.GetInstanceID(),
                ["methodName"] = "SetActive",
                ["staticArgument"] = true
            });
            Assert.IsTrue(wireResult["success"].ToObject<bool>(), wireResult.ToString());

            JObject full = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = source.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["fieldNames"] = new JArray("noArgs")
            });
            JToken call = full["fields"]["noArgs"]["m_PersistentCalls"]["m_Calls"][0];
            Assert.AreEqual("SetActive", call["m_MethodName"].ToString());
            Assert.AreEqual(
                (int)PersistentListenerMode.Bool,
                call["m_Mode"]["value"].ToObject<int>());
            Assert.IsNotNull(call["m_Mode"]["index"]);

            JObject shallow = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = source.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["fieldNames"] = new JArray("noArgs"),
                ["maxDepth"] = 2
            });
            Assert.IsInstanceOf<JArray>(
                shallow["fields"]["noArgs"]["m_PersistentCalls"]["m_Calls"]);
            Assert.IsTrue(shallow["arrayMetadata"]
                ["noArgs.m_PersistentCalls.m_Calls"]["depthTruncated"].ToObject<bool>());
        }

        [Test]
        public void ReadSerializedFields_MaxElementsReturnsPrefixAndHonestArrayMetadata()
        {
            UnityEventWiringProbe probe = Spawn("Serialized_ArrayWidth")
                .AddComponent<UnityEventWiringProbe>();
            for (int i = 0; i < 5; i++)
            {
                probe.payloads.Add(new UnityEventWiringPayload
                {
                    label = $"item-{i}",
                    mode = PersistentListenerMode.Bool
                });
            }

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["fieldNames"] = new JArray("payloads"),
                ["maxElements"] = 2
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            Assert.AreEqual(2, ((JArray)result["fields"]["payloads"]).Count);
            Assert.AreEqual(5, result["arrayMetadata"]["payloads"]["total"].ToObject<int>());
            Assert.AreEqual(2, result["arrayMetadata"]["payloads"]["returned"].ToObject<int>());
            Assert.IsTrue(result["arrayMetadata"]["payloads"]["truncated"].ToObject<bool>());
        }

        [Test]
        public void ReadSerializedFields_NestedArraysShareOneGlobalElementBudget()
        {
            UnityEventNestedArrayProbe probe = Spawn("Serialized_NestedBudget")
                .AddComponent<UnityEventNestedArrayProbe>();
            for (int groupIndex = 0; groupIndex < 4; groupIndex++)
            {
                var group = new UnityEventNestedArrayPayload();
                for (int valueIndex = 0; valueIndex < 4; valueIndex++)
                {
                    group.values.Add(groupIndex * 10 + valueIndex);
                }
                probe.groups.Add(group);
            }

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventNestedArrayProbe).FullName,
                ["fieldNames"] = new JArray("groups"),
                ["maxElements"] = 5
            });

            Assert.IsTrue(result["success"].ToObject<bool>(), result.ToString());
            JArray groups = (JArray)result["fields"]["groups"];
            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual(4, ((JArray)groups[0]["values"]).Count);
            int aggregateReturned = ((JObject)result["arrayMetadata"])
                .Properties()
                .Sum(property => property.Value["returned"].ToObject<int>());
            Assert.AreEqual(5, aggregateReturned);
            Assert.IsTrue(result["arrayMetadata"]["groups"]["budgetTruncated"].ToObject<bool>());
        }

        [Test]
        public void ReadSerializedFields_MaxDepthZeroKeepsArrayShapeAndReportsDepthTruncation()
        {
            UnityEventWiringProbe probe = Spawn("Serialized_DepthZero")
                .AddComponent<UnityEventWiringProbe>();
            probe.payloads.Add(new UnityEventWiringPayload { label = "one" });
            probe.payloads.Add(new UnityEventWiringPayload { label = "two" });

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["fieldNames"] = new JArray("payloads"),
                ["maxDepth"] = 0
            });

            Assert.IsInstanceOf<JArray>(result["fields"]["payloads"]);
            Assert.AreEqual(0, ((JArray)result["fields"]["payloads"]).Count);
            Assert.AreEqual(2, result["arrayMetadata"]["payloads"]["total"].ToObject<int>());
            Assert.AreEqual(0, result["arrayMetadata"]["payloads"]["returned"].ToObject<int>());
            Assert.IsTrue(result["arrayMetadata"]["payloads"]["truncated"].ToObject<bool>());
            Assert.IsTrue(result["arrayMetadata"]["payloads"]["depthTruncated"].ToObject<bool>());
            Assert.IsFalse(result["arrayMetadata"]["payloads"]["budgetTruncated"].ToObject<bool>());
        }

        [Test]
        public void ReadSerializedFields_TruncationSummaryIsPresentInScalarMessage()
        {
            UnityEventWiringProbe probe = Spawn("Serialized_ScalarSummary")
                .AddComponent<UnityEventWiringProbe>();
            for (int i = 0; i < 3; i++)
            {
                probe.payloads.Add(new UnityEventWiringPayload { label = $"item-{i}" });
            }

            JObject result = new ReadSerializedFieldsTool().Execute(new JObject
            {
                ["instanceId"] = probe.gameObject.GetInstanceID(),
                ["componentName"] = typeof(UnityEventWiringProbe).FullName,
                ["fieldNames"] = new JArray("payloads"),
                ["maxElements"] = 1
            });

            string message = result["message"].ToString();
            StringAssert.Contains("Array traversal returned 1 of 3", message);
            StringAssert.Contains("global maxElements=1", message);
            StringAssert.Contains("truncated arrays=1", message);
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }
    }
}
