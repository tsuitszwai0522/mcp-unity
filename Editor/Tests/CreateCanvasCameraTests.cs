using System.Collections.Generic;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public class CreateCanvasCameraTests
    {
        private const string ObjectPrefix = "CreateCanvasCameraT_";

        private sealed class ExistingCameraState
        {
            public Camera Camera;
            public bool Enabled;
            public string Tag;
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<ExistingCameraState> _existingCameraStates =
            new List<ExistingCameraState>();
        private CreateCanvasTool _tool;

        [SetUp]
        public void SetUp()
        {
            _tool = new CreateCanvasTool();
            _spawned.Clear();
            _existingCameraStates.Clear();

            foreach (Camera camera in Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                _existingCameraStates.Add(new ExistingCameraState
                {
                    Camera = camera,
                    Enabled = camera.enabled,
                    Tag = camera.gameObject.tag
                });
                camera.enabled = false;
                if (camera.gameObject.CompareTag("MainCamera"))
                    camera.gameObject.tag = "Untagged";
            }

            Assert.IsNull(Camera.main,
                "Camera fallback tests require existing Main Cameras to be temporarily neutralized.");
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                    Object.DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();

            foreach (ExistingCameraState state in _existingCameraStates)
            {
                if (state.Camera == null)
                    continue;
                state.Camera.gameObject.tag = state.Tag;
                state.Camera.enabled = state.Enabled;
            }
            _existingCameraStates.Clear();
        }

        [Test]
        public void Execute_ExplicitMissingCameraPathReturnsValidationErrorWithoutFallback()
        {
            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "MissingCanvas",
                "ScreenSpaceCamera",
                ObjectPrefix + "BogusCameraPath");

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("not found"));
        }

        [Test]
        public void Execute_ExplicitObjectWithoutCameraReturnsDistinctValidationError()
        {
            GameObject notCamera = Spawn(ObjectPrefix + "NotACamera");

            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "NoComponentCanvas",
                "WorldSpace",
                GameObjectPathUtils.GetCanonicalPath(notCamera));

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("has no Camera component"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Not.Contain("not found"));
        }

        [Test]
        public void Execute_ExplicitCameraReportsSourceAndCanonicalPath()
        {
            GameObject cameraObject = Spawn(ObjectPrefix + "ExplicitCamera");
            Camera expectedCamera = cameraObject.AddComponent<Camera>();

            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "ExplicitCanvas",
                "ScreenSpaceCamera",
                GameObjectPathUtils.GetCanonicalPath(cameraObject));

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Canvas canvas = TrackCreatedCanvas(result);
            Assert.AreEqual("explicit", result["cameraSource"]?.ToString());
            Assert.AreEqual(GameObjectPathUtils.GetCanonicalPath(cameraObject),
                result["cameraPath"]?.ToString());
            Assert.AreSame(expectedCamera, canvas.worldCamera);
        }

        [Test]
        public void Execute_ImplicitMainCameraReportsSourceAndBindsCamera()
        {
            GameObject cameraObject = Spawn(ObjectPrefix + "ImplicitMainCamera");
            Camera expectedCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";

            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "ImplicitMainCameraCanvas",
                "ScreenSpaceCamera");

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Canvas canvas = TrackCreatedCanvas(result);
            Assert.AreEqual("cameraMain", result["cameraSource"]?.ToString());
            Assert.AreSame(expectedCamera, canvas.worldCamera);
        }

        [Test]
        public void Execute_WorldSpaceWithoutCameraSucceedsWithNoneAndWarning()
        {
            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "UnboundWorldCanvas",
                "WorldSpace");

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Canvas canvas = TrackCreatedCanvas(result);
            Assert.AreEqual("none", result["cameraSource"]?.ToString());
            Assert.AreEqual(JTokenType.Null, result["cameraPath"]?.Type);
            Assert.That(result["warnings"]?[0]?.ToString(), Does.Contain("worldCamera"));
            Assert.That(result["message"]?.ToString(), Does.Contain("worldCamera"));
            Assert.IsNull(canvas.worldCamera);
        }

        [Test]
        public void Execute_ScreenSpaceCameraWithoutCameraKeepsExistingError()
        {
            JObject result = ExecuteCameraCanvas(
                ObjectPrefix + "MissingScreenCameraCanvas",
                "ScreenSpaceCamera");

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("no enabled Main Camera"));
            Assert.That(result["error"]?["message"]?.ToString(),
                Does.Contain("cameraPath"));
        }

        private JObject ExecuteCameraCanvas(
            string objectPath,
            string renderMode,
            string cameraPath = null)
        {
            var parameters = new JObject
            {
                ["objectPath"] = objectPath,
                ["renderMode"] = renderMode,
                ["createEventSystem"] = false
            };
            if (cameraPath != null)
                parameters["cameraPath"] = cameraPath;
            return _tool.Execute(parameters);
        }

        private GameObject Spawn(string name)
        {
            var gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject;
        }

        private Canvas TrackCreatedCanvas(JObject result)
        {
            int instanceId = result["instanceId"]?.ToObject<int>() ?? 0;
            var canvasObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            Assert.IsNotNull(canvasObject, result.ToString());
            _spawned.Add(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, result.ToString());
            return canvas;
        }
    }
}
