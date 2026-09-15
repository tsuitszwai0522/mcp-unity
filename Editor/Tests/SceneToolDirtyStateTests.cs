using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// Unsaved-change handling of the scene tools and the dirty-scene reader used by run_tests.
    /// Scenes are owned copies opened additively, so the runner scene is never saved, replaced or closed.
    /// A non-additive load_scene would replace the runner scene and is verified in an isolated Editor instead.
    /// </summary>
    public class SceneToolDirtyStateTests
    {
        private const string SceneFolderName = "McpUnitySceneDirtyStateTests";
        private const string SceneFolder = "Assets/" + SceneFolderName;

        private readonly List<Scene> _createdScenes = new List<Scene>();
        private Func<int> _originalLoadedSceneCount;
        private Func<Scene, bool> _originalCloseLoadedScene;
        private Func<IReadOnlyList<DirtySceneInfo>> _originalDescribeDirtyScenes;
        private Func<bool> _originalSaveOpenScenes;
        private Action<string, OpenSceneMode> _originalOpenScene;
        private Scene _originalActiveScene;

        [SetUp]
        public void SetUp()
        {
            _originalLoadedSceneCount = DeleteSceneTool.LoadedSceneCount;
            _originalCloseLoadedScene = DeleteSceneTool.CloseLoadedScene;
            _originalDescribeDirtyScenes = LoadSceneTool.DescribeDirtyScenes;
            _originalSaveOpenScenes = LoadSceneTool.SaveOpenScenes;
            _originalOpenScene = LoadSceneTool.OpenScene;
            _originalActiveScene = SceneManager.GetActiveScene();
            // Reclaim residue from a run killed mid-test; the folder name is constant.
            if (CloseScenesUnder(SceneFolder) && AssetDatabase.IsValidFolder(SceneFolder))
            {
                AssetDatabase.DeleteAsset(SceneFolder);
                AssetDatabase.Refresh();
            }
        }

        [TearDown]
        public void TearDown()
        {
            DeleteSceneTool.LoadedSceneCount = _originalLoadedSceneCount;
            DeleteSceneTool.CloseLoadedScene = _originalCloseLoadedScene;
            LoadSceneTool.DescribeDirtyScenes = _originalDescribeDirtyScenes;
            LoadSceneTool.SaveOpenScenes = _originalSaveOpenScenes;
            LoadSceneTool.OpenScene = _originalOpenScene;
            if (_originalActiveScene.IsValid() && _originalActiveScene.isLoaded
                && SceneManager.GetActiveScene() != _originalActiveScene)
            {
                SceneManager.SetActiveScene(_originalActiveScene);
            }

            _createdScenes.Clear();
            if (CloseScenesUnder(SceneFolder) && AssetDatabase.IsValidFolder(SceneFolder))
            {
                AssetDatabase.DeleteAsset(SceneFolder);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void DirtySceneReader_ReportsOnlyScenesWithUnsavedChanges()
        {
            Scene scene = CreateSavedScene("Reader");
            Assert.IsFalse(Describe().Any(info => info.Path == scene.path), "A freshly saved scene is clean.");

            AddMarker(scene, "ReaderWip");
            DirtySceneInfo dirty = Describe().Single(info => info.Path == scene.path);
            Assert.IsFalse(dirty.IsUntitled);
            Assert.AreEqual("Reader", dirty.Name);
            JObject json = dirty.ToJson();
            Assert.AreEqual(scene.path, json.Value<string>("path"));
            Assert.IsFalse(json.Value<bool>("untitled"));

            Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            Assert.IsFalse(Describe().Any(info => info.Path == scene.path), "Saving clears the dirty state.");
        }

        [Test]
        public void DirtySceneInfo_UntitledSceneHasNullPathInJson()
        {
            var info = new DirtySceneInfo { Name = string.Empty, Path = string.Empty };
            JObject json = info.ToJson();
            Assert.AreEqual(JTokenType.Null, json["path"].Type);
            Assert.IsTrue(json.Value<bool>("untitled"));
            Assert.AreEqual("'Untitled' (untitled)", info.Describe());
        }

        [Test]
        public void UnloadScene_DefaultSavesDirtySceneAndReportsIt()
        {
            Scene scene = CreateSavedScene("UnloadSave");
            string marker = AddMarker(scene, "UnloadSaveWip");

            JObject response = new UnloadSceneTool().Execute(new JObject { ["scenePath"] = scene.path });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.IsTrue(response.Value<bool>("wasDirty"));
            Assert.IsTrue(response.Value<bool>("saved"));
            Assert.IsFalse(response.Value<bool>("discardedUnsavedChanges"));
            StringAssert.Contains("saved first", response.Value<string>("message"));
            Assert.IsTrue(SceneFileContains(SceneFolder + "/UnloadSave.unity", marker));
        }

        [Test]
        public void UnloadScene_SaveIfDirtyFalseDiscardsAndReportsIt()
        {
            Scene scene = CreateSavedScene("UnloadDiscard");
            string path = scene.path;
            string marker = AddMarker(scene, "UnloadDiscardWip");

            JObject response = new UnloadSceneTool().Execute(new JObject { ["scenePath"] = path, ["saveIfDirty"] = false });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.IsTrue(response.Value<bool>("wasDirty"));
            Assert.IsFalse(response.Value<bool>("saved"));
            Assert.IsTrue(response.Value<bool>("discardedUnsavedChanges"));
            StringAssert.Contains("discarded", response.Value<string>("message"));
            Assert.IsFalse(SceneFileContains(path, marker));
            Assert.IsFalse(SceneManager.GetSceneByPath(path).isLoaded);
        }

        [Test]
        public void UnloadScene_CleanSceneReportsNeitherSavedNorDiscarded()
        {
            Scene scene = CreateSavedScene("UnloadClean");

            JObject response = new UnloadSceneTool().Execute(new JObject { ["scenePath"] = scene.path });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.IsFalse(response.Value<bool>("wasDirty"));
            Assert.IsFalse(response.Value<bool>("saved"));
            Assert.IsFalse(response.Value<bool>("discardedUnsavedChanges"));
        }

        [Test]
        public void DeleteScene_LoadedAdditiveDirtySceneIsClosedWithoutSavingAndReported()
        {
            Scene scene = CreateSavedScene("DeleteLoaded");
            string path = scene.path;
            AddMarker(scene, "DeleteLoadedWip");

            JObject response = new DeleteSceneTool().Execute(new JObject { ["scenePath"] = path });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.IsTrue(response.Value<bool>("closedLoadedScene"));
            Assert.IsTrue(response.Value<bool>("discardedUnsavedChanges"));
            Assert.IsFalse(SceneManager.GetSceneByPath(path).IsValid() && SceneManager.GetSceneByPath(path).isLoaded);
            Assert.IsFalse(File.Exists(FullPath(path)));
        }

        [Test]
        public void DeleteScene_OnlyLoadedSceneIsRefusedWithoutDeleting()
        {
            Scene scene = CreateSavedScene("DeleteOnly");
            string path = scene.path;
            bool closeCalled = false;
            DeleteSceneTool.LoadedSceneCount = () => 1;
            DeleteSceneTool.CloseLoadedScene = _ => { closeCalled = true; return true; };

            JObject response = new DeleteSceneTool().Execute(new JObject { ["scenePath"] = path });

            Assert.AreEqual("validation_error", response["error"]?["type"]?.ToString(), response.ToString());
            StringAssert.Contains("only loaded scene", response["error"]?["message"]?.ToString());
            Assert.IsFalse(closeCalled);
            Assert.IsTrue(File.Exists(FullPath(path)));
            Assert.IsTrue(SceneManager.GetSceneByPath(path).isLoaded);
        }

        [Test]
        public void DeleteScene_CloseFailureIsRefusedWithoutDeleting()
        {
            Scene scene = CreateSavedScene("DeleteCloseFails");
            string path = scene.path;
            DeleteSceneTool.CloseLoadedScene = _ => false;

            JObject response = new DeleteSceneTool().Execute(new JObject { ["scenePath"] = path });

            Assert.AreEqual("scene_close_error", response["error"]?["type"]?.ToString(), response.ToString());
            Assert.IsTrue(File.Exists(FullPath(path)));
            Assert.IsTrue(SceneManager.GetSceneByPath(path).isLoaded);
        }

        [Test]
        public void LoadScene_AdditiveDoesNotSaveDirtyScenesAndReportsEmptyLists()
        {
            Scene dirty = CreateSavedScene("LoadDirty");
            string marker = AddMarker(dirty, "LoadDirtyWip");
            Scene other = CreateSavedScene("LoadOther");
            string otherPath = other.path;
            Assert.IsTrue(EditorSceneManager.CloseScene(other, true));

            JObject response = new LoadSceneTool().Execute(new JObject { ["scenePath"] = otherPath, ["additive"] = true });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.AreEqual(0, ((JArray)response["savedScenes"]).Count);
            Assert.AreEqual(0, ((JArray)response["discardedScenes"]).Count);
            Assert.IsTrue(dirty.isDirty);
            Assert.IsFalse(SceneFileContains(dirty.path, marker));
        }

        [Test]
        public void LoadScene_NonAdditiveWithUntitledDirtySceneIsRefusedWithoutSavingOrOpening()
        {
            bool saved = false, opened = false;
            LoadSceneTool.DescribeDirtyScenes = () => new List<DirtySceneInfo>
            {
                new DirtySceneInfo { Name = "Level", Path = "Assets/Level.unity" },
                new DirtySceneInfo { Name = string.Empty, Path = string.Empty }
            };
            LoadSceneTool.SaveOpenScenes = () => { saved = true; return true; };
            LoadSceneTool.OpenScene = (path, mode) => opened = true;

            JObject response = new LoadSceneTool().Execute(new JObject { ["scenePath"] = "Assets/Other.unity" });

            Assert.AreEqual("untitled_dirty_scene", response["error"]?["type"]?.ToString(), response.ToString());
            StringAssert.Contains("'Untitled' (untitled)", response["error"]?["message"]?.ToString());
            Assert.IsFalse(saved, "Saving an untitled scene would open a blocking Save Scene dialog.");
            Assert.IsFalse(opened);
        }

        [Test]
        public void LoadScene_NonAdditiveReportsSavedAndDiscardedScenes()
        {
            int describeCalls = 0, saveCalls = 0;
            string openedPath = null;
            OpenSceneMode? openedMode = null;
            var a = new DirtySceneInfo { Name = "A", Path = "Assets/A.unity" };
            var b = new DirtySceneInfo { Name = "B", Path = "Assets/B.unity" };
            LoadSceneTool.DescribeDirtyScenes = () => ++describeCalls == 1
                ? new List<DirtySceneInfo> { a, b }
                : new List<DirtySceneInfo> { b };
            LoadSceneTool.SaveOpenScenes = () => { saveCalls++; return false; };
            LoadSceneTool.OpenScene = (path, mode) => { openedPath = path; openedMode = mode; };

            JObject response = new LoadSceneTool().Execute(new JObject { ["scenePath"] = "Assets/C.unity" });

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            Assert.AreEqual(1, saveCalls);
            Assert.AreEqual("Assets/C.unity", openedPath);
            Assert.AreEqual(OpenSceneMode.Single, openedMode);
            Assert.AreEqual(new[] { "Assets/A.unity" }, ((JArray)response["savedScenes"]).Select(s => s.Value<string>("path")).ToArray());
            Assert.AreEqual(new[] { "Assets/B.unity" }, ((JArray)response["discardedScenes"]).Select(s => s.Value<string>("path")).ToArray());
            StringAssert.Contains("saved 1 dirty scene(s)", response.Value<string>("message"));
            StringAssert.Contains("replaced without saving", response.Value<string>("message"));
        }

        private static List<DirtySceneInfo> Describe() => SceneDirtyState.DescribeDirtyLoadedScenes().ToList();

        private Scene CreateSavedScene(string sceneName)
        {
            // UTF keeps an untitled runner scene, so NewScene(Additive) is rejected. Save a copy of the
            // runner scene (saveAsCopy leaves the runner untouched), open it additively, clear the copied
            // roots and save, giving a clean owned scene with a path.
            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", SceneFolderName)));
                AssetDatabase.Refresh();
            }

            string scenePath = SceneFolder + "/" + sceneName + ".unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath, true));
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            _createdScenes.Add(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Assert.IsTrue(EditorSceneManager.SaveScene(scene));
            Assert.IsFalse(scene.isDirty);
            return scene;
        }

        private static string AddMarker(Scene scene, string prefix)
        {
            string name = prefix + "_" + Guid.NewGuid().ToString("N");
            var marker = new GameObject(name);
            SceneManager.MoveGameObjectToScene(marker, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.IsTrue(scene.isDirty);
            return name;
        }

        private static string FullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static bool SceneFileContains(string assetPath, string marker)
        {
            string full = FullPath(assetPath);
            return File.Exists(full) && File.ReadAllText(full).Contains("m_Name: " + marker);
        }

        private static bool CloseScenesUnder(string folder)
        {
            bool allClosed = true;
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isLoaded && scene.path.StartsWith(folder + "/", StringComparison.Ordinal))
                {
                    allClosed &= EditorSceneManager.CloseScene(scene, true);
                }
            }
            return allClosed;
        }
    }
}
