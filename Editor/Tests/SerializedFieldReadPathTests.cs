using System;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace McpUnity.Tests
{
    [Serializable] public class ReadPathGroup
    {
        public int count;
        public UnityEngine.Object target;
        public int[] values;
    }
    public class ReadPathProbe : MonoBehaviour
    {
        [SerializeField] private int m_Count = 4;
        public ReadPathGroup left = new ReadPathGroup();
        public ReadPathGroup right = new ReadPathGroup();
        public UnityEngine.Object[] refs;
    }
    [TestFixture] public class SerializedFieldReadPathTests
    {
        private Scene scene;
        private GameObject root,other;
        private ReadPathProbe probe;
        [SetUp] public void Setup()
        {
            scene=EditorSceneManager.NewPreviewScene();
            root=new GameObject("ReadPathRoot");SceneManager.MoveGameObjectToScene(root,scene);
            other=new GameObject("ReadPathOther");SceneManager.MoveGameObjectToScene(other,scene);
            probe=root.AddComponent<ReadPathProbe>();
            probe.left=new ReadPathGroup{count=11,target=root,values=new[]{1,2}};
            probe.right=new ReadPathGroup{count=22,target=other,values=new[]{3,4,5}};
            probe.refs=new UnityEngine.Object[]{root,other};
        }
        [TearDown] public void Cleanup() { if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.ClosePreviewScene(scene); }
        private JObject Read(JArray fields=null,int maxElements=100)
        {
            var p=new JObject{["instanceId"]=root.GetInstanceID(),["componentName"]=typeof(ReadPathProbe).FullName,["maxElements"]=maxElements};
            if(fields!=null)p["fieldNames"]=fields;
            var r=new ReadSerializedFieldsTool().Execute(p);Assert.IsTrue(r.Value<bool>("success"),r.ToString());return r;
        }
        [Test] public void SelectedNestedDuplicateLeavesPreserveIdentity()
        {
            var fields=(JObject)Read(new JArray("left.count","right.count","left.target","right.target"))["fields"];
            Assert.AreEqual(4,fields.Count);Assert.AreEqual(11,fields.Value<int>("left.count"));Assert.AreEqual(22,fields.Value<int>("right.count"));
            Assert.AreEqual(root.GetInstanceID(),fields["left.target"].Value<int>("instanceId"));Assert.AreEqual(other.GetInstanceID(),fields["right.target"].Value<int>("instanceId"));
        }
        [Test] public void SelectedArrayElementsAndSizesKeepCompletePaths()
        {
            var paths=new JArray("left.values.Array.data[0]","left.values.Array.data[1]","right.values.Array.data[0]","left.values.Array.size","right.values.Array.size","refs.Array.data[0]","refs.Array.data[1]");
            var fields=(JObject)Read(paths)["fields"];Assert.AreEqual(7,fields.Count);
            var so=new SerializedObject(probe);
            foreach(var item in paths)
            {
                string path=item.ToString();var property=so.FindProperty(path);Assert.IsNotNull(property);
                if(property.propertyType==SerializedPropertyType.ObjectReference)Assert.AreEqual(property.objectReferenceInstanceIDValue,fields[path].Value<int>("instanceId"),path);
                else Assert.AreEqual(property.intValue,fields.Value<int>(path),path);
            }
        }
        [Test] public void AliasAndMissingFieldsUseCanonicalAndRequestedKeys()
        {
            var fields=(JObject)Read(new JArray("count","m_Count","left.absent"))["fields"];
            Assert.AreEqual(2,fields.Count);Assert.AreEqual(4,fields.Value<int>("m_Count"));Assert.IsNull(fields.Property("count"));Assert.AreEqual(JTokenType.Null,fields["left.absent"].Type);
        }
        [Test] public void WholeObjectReadKeepsRecursiveChildShape()
        {
            var fields=Read()["fields"];
            Assert.AreEqual(11,fields["left"].Value<int>("count"));Assert.AreEqual(22,fields["right"].Value<int>("count"));Assert.AreEqual(2,((JArray)fields["left"]["values"]).Count);Assert.AreEqual(4,fields.Value<int>("m_Count"));
        }
        [Test] public void WholeParentAndSelectedChildRemainSeparate()
        {
            var fields=(JObject)Read(new JArray("left","left.count"))["fields"];
            Assert.AreEqual(2,fields.Count);Assert.AreEqual(11,fields["left"].Value<int>("count"));Assert.AreEqual(11,fields.Value<int>("left.count"));
        }
        [Test] public void SelectedArraysRetainGlobalBudgetAndPathMetadata()
        {
            var r=Read(new JArray("left.values","right.values"),1);var fields=(JObject)r["fields"];
            Assert.AreEqual(2,fields.Count);Assert.AreEqual(1,((JArray)fields["left.values"]).Count);Assert.AreEqual(0,((JArray)fields["right.values"]).Count);
            Assert.IsTrue(r["arrayMetadata"]["left.values"].Value<bool>("truncated"));Assert.IsTrue(r["arrayMetadata"]["right.values"].Value<bool>("truncated"));
        }
    }
}
