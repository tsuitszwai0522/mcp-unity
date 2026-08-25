using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public class ScreenshotToolsObservabilityTests
    {
        private const string TestDirectory = "Assets/McpUnityScreenshotObservabilityTests";
        private const string TestPrefabPath = TestDirectory + "/ScreenshotObservability.prefab";

        private readonly Dictionary<string, object> _originalGameViewSeams =
            new Dictionary<string, object>();
        private readonly Dictionary<string, object> _originalSceneViewSeams =
            new Dictionary<string, object>();
        private readonly List<UnityEngine.Object> _createdObjects =
            new List<UnityEngine.Object>();
        private Action _discardPrefabSession;
        private bool _seamsCaptured;
        private bool _ownsPrefabSession;

        private sealed class TestEditorWindow : EditorWindow
        {
            private RenderTexture ThrowingRenderView(Vector2 mousePosition, bool clearTexture)
            {
                throw new InvalidOperationException("Production reflection invocation failure");
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (PrefabEditingService.Status != PrefabEditingSessionStatus.None)
            {
                Assert.Ignore(
                    "ScreenshotToolsObservabilityTests will not erase an existing Prefab session.");
            }

            _discardPrefabSession = () =>
            {
                if (PrefabEditingService.Status != PrefabEditingSessionStatus.None)
                    PrefabEditingService.Discard();
            };

            CaptureSeams(
                typeof(ScreenshotGameViewTool),
                _originalGameViewSeams,
                "_resolveGameViewType",
                "_resolveRenderViewMethod",
                "_invokeRenderView",
                "_captureScreenshotAsTexture",
                "_findMainCamera",
                "_resolvePrefabRoot",
                "_hasExistingEditorWindow",
                "_getGameViewWindow");
            CaptureSeams(
                typeof(ScreenshotSceneViewTool),
                _originalSceneViewSeams,
                "_getLastActiveSceneView",
                "_getSceneViewCamera",
                "_frameSelected",
                "_repaintSceneView",
                "_subscribeToEditorUpdate",
                "_unsubscribeFromEditorUpdate");
            _seamsCaptured = true;
        }

        [TearDown]
        public void TearDown()
        {
            Exception cleanupFailure = null;
            if (_seamsCaptured)
            {
                TryCleanup(
                    () => RestoreSeams(
                        typeof(ScreenshotGameViewTool), _originalGameViewSeams),
                    ref cleanupFailure);
                TryCleanup(
                    () => RestoreSeams(
                        typeof(ScreenshotSceneViewTool), _originalSceneViewSeams),
                    ref cleanupFailure);
            }
            _seamsCaptured = false;

            TryCleanup(() => Selection.activeGameObject = null, ref cleanupFailure);
            ResetPrefabOwnership(_discardPrefabSession, ref cleanupFailure);
            _discardPrefabSession = null;

            TryCleanup(
                () => DestroyObjects(
                    UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>()),
                ref cleanupFailure);

            TryCleanup(() => DestroyObjects(_createdObjects), ref cleanupFailure);
            _createdObjects.Clear();

            TryCleanup(() =>
            {
                if (AssetDatabase.IsValidFolder(TestDirectory))
                {
                    AssetDatabase.DeleteAsset(TestDirectory);
                    AssetDatabase.Refresh();
                }
            }, ref cleanupFailure);

            if (cleanupFailure != null)
                throw cleanupFailure;
        }

        [Test]
        public void GameView_RenderViewPath_DisclosesNonDegradedCapture()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(() =>
                {
                    Assert.Fail("ScreenCapture fallback must not run after RenderView succeeds");
                    return null;
                }));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
        }

        [Test]
        public void GameView_ScreenCapturePath_DisclosesProductionRenderViewReason()
        {
            TestEditorWindow window = CreateTestWindow();
            ConfigureRenderView(window, null, (_, __) => null);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(CreateTexture));

            JObject result = ExecuteGameView();

            AssertCapture(
                result,
                "screen_capture",
                true,
                "render_view_unavailable:method_missing");
        }

        [Test]
        public void GameView_MainCameraPath_AppendsScreenCaptureNullReason()
        {
            TestEditorWindow window = CreateTestWindow();
            GameObject cameraObject = Track(new GameObject("ScreenshotFallbackCamera"));
            Camera camera = cameraObject.AddComponent<Camera>();
            ConfigureRenderView(window, DummyMethod, (_, __) => null);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(() => null));
            SetGameViewSeam(
                "_resolvePrefabRoot",
                new Func<Tuple<JObject, GameObject>>(
                    () => Tuple.Create<JObject, GameObject>(null, null)));
            SetGameViewSeam("_findMainCamera", new Func<Camera>(() => camera));

            JObject result = ExecuteGameView();

            AssertCapture(
                result,
                "main_camera_fallback",
                true,
                "render_view_unavailable:rendertexture_null;screen_capture_returned_null");
        }

        [TestCase(
            "gameview_type_missing",
            "render_view_unavailable:gameview_type_missing")]
        [TestCase("window_null", "render_view_unavailable:window_null")]
        [TestCase("method_missing", "render_view_unavailable:method_missing")]
        [TestCase(
            "rendertexture_null",
            "render_view_unavailable:rendertexture_null")]
        [TestCase(
            "exception",
            "render_view_unavailable:exception:InvalidOperationException")]
        public void GameView_RenderViewFailureTaxonomy_ComesFromProductionCode(
            string scenario,
            string expectedReason)
        {
            ConfigureRenderViewFailure(scenario);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(CreateTexture));

            JObject result = ExecuteGameView();

            AssertCapture(result, "screen_capture", true, expectedReason);
        }

        [Test]
        public void GameView_RenderViewInvocation_UnwrapsProductionReflectionException()
        {
            TestEditorWindow window = CreateTestWindow();
            MethodInfo throwingMethod = typeof(TestEditorWindow).GetMethod(
                "ThrowingRenderView", BindingFlags.Instance | BindingFlags.NonPublic);
            SetGameViewSeam(
                "_resolveGameViewType",
                new Func<Type>(() => typeof(TestEditorWindow)));
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, __) => window));
            SetGameViewSeam(
                "_resolveRenderViewMethod",
                new Func<Type, MethodInfo>(_ => throwingMethod));
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(CreateTexture));
            Assert.AreSame(
                _originalGameViewSeams["_invokeRenderView"],
                GetPrivateStaticField(typeof(ScreenshotGameViewTool), "_invokeRenderView"),
                "This test must exercise the production MethodInfo.Invoke seam");

            JObject result = ExecuteGameView();

            AssertCapture(
                result,
                "screen_capture",
                true,
                "render_view_unavailable:exception:InvalidOperationException");
        }

        [Test]
        public void GameView_CaptureException_PreservesAccumulatedDiagnosticsInErrorMessage()
        {
            ConfigureMethodMissing(true);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(() => null));
            SetGameViewSeam(
                "_resolvePrefabRoot",
                new Func<Tuple<JObject, GameObject>>(
                    () => throw new InvalidOperationException("Injected fallback failure")));

            JObject result = ExecuteGameView();
            string message = result["error"]?["message"]?.ToString();

            Assert.That(
                message,
                Does.Contain(
                    "degradedReason=render_view_unavailable:method_missing;" +
                    "screen_capture_returned_null"));
            Assert.That(message, Does.Contain("gameViewWindowCreated=true"));
        }

        [TestCase("scope_error")]
        [TestCase("prefab_block")]
        [TestCase("missing_main_camera")]
        public void GameView_TotalFailure_PreservesDiagnosticsInsideWireVisibleErrorMessage(
            string failureExit)
        {
            ConfigureMethodMissing(true);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(() => null));

            if (failureExit == "scope_error")
            {
                var scopeError = new JObject
                {
                    ["error"] = new JObject
                    {
                        ["type"] = "prefab_session_lost_error",
                        ["message"] = "Prefab scope unavailable."
                    }
                };
                SetGameViewSeam(
                    "_resolvePrefabRoot",
                    new Func<Tuple<JObject, GameObject>>(
                        () => Tuple.Create<JObject, GameObject>(scopeError, null)));
            }
            else if (failureExit == "prefab_block")
            {
                GameObject prefabRoot = Track(new GameObject("BlockedPrefabRoot"));
                SetGameViewSeam(
                    "_resolvePrefabRoot",
                    new Func<Tuple<JObject, GameObject>>(
                        () => Tuple.Create<JObject, GameObject>(null, prefabRoot)));
            }
            else
            {
                SetGameViewSeam(
                    "_resolvePrefabRoot",
                    new Func<Tuple<JObject, GameObject>>(
                        () => Tuple.Create<JObject, GameObject>(null, null)));
                SetGameViewSeam("_findMainCamera", new Func<Camera>(() => null));
            }

            JObject result = ExecuteGameView();
            string message = result["error"]?["message"]?.ToString();

            Assert.That(message, Does.Contain("render_view_unavailable:method_missing"));
            Assert.That(message, Does.Contain("screen_capture_returned_null"));
            Assert.That(message, Does.Contain("gameViewWindowCreated=true"));
        }

        [Test]
        public void GameView_NewWindow_UsesProductionDetectorAndDisclosesCreation()
        {
            Assert.AreEqual(
                0,
                UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>().Length,
                "The detector test must start without its private window type");
            RenderTexture source = CreateSourceRenderTexture();
            SetGameViewSeam("_resolveGameViewType", new Func<Type>(() => typeof(TestEditorWindow)));
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, __) =>
                {
                    TestEditorWindow existing =
                        UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>()
                            .Length > 0
                            ? UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>()[0]
                            : null;
                    return existing ?? CreateTestWindow();
                }));
            SetGameViewSeam(
                "_resolveRenderViewMethod",
                new Func<Type, MethodInfo>(_ => DummyMethod));
            SetGameViewSeam(
                "_invokeRenderView",
                new Func<MethodInfo, EditorWindow, RenderTexture>((_, __) => source));

            JObject result = ExecuteGameView();

            Assert.AreEqual(
                1,
                UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>().Length,
                "The request should create exactly one Game View stand-in");
            Assert.IsTrue(result["gameViewWindowCreated"]?.ToObject<bool>() ?? false);
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("gameViewWindowCreated=true"));
        }

        [Test]
        public void SceneView_PrefabCallSite_UsesEditorUpdateAndDisclosesCapturePath()
        {
            OpenTestPrefabSession();
            SceneView sceneView = Track(ScriptableObject.CreateInstance<SceneView>());
            GameObject cameraObject = Track(new GameObject("SceneViewCaptureCamera"));
            Camera camera = cameraObject.AddComponent<Camera>();
            EditorApplication.CallbackFunction subscribed = null;
            EditorApplication.CallbackFunction unsubscribed = null;
            SetSceneViewSeam("_getLastActiveSceneView", new Func<SceneView>(() => sceneView));
            SetSceneViewSeam(
                "_getSceneViewCamera",
                new Func<SceneView, Camera>(_ => camera));
            SetSceneViewSeam("_frameSelected", new Action<SceneView>(_ => { }));
            SetSceneViewSeam("_repaintSceneView", new Action<SceneView>(_ => { }));
            SetSceneViewSeam(
                "_subscribeToEditorUpdate",
                new Action<EditorApplication.CallbackFunction>(handler => subscribed = handler));
            SetSceneViewSeam(
                "_unsubscribeFromEditorUpdate",
                new Action<EditorApplication.CallbackFunction>(handler => unsubscribed = handler));
            var tcs = new TaskCompletionSource<JObject>();

            new ScreenshotSceneViewTool().ExecuteAsync(new JObject
            {
                ["width"] = 8,
                ["height"] = 8
            }, tcs);

            Assert.IsNotNull(
                subscribed,
                "The active-Prefab call site must subscribe through EditorApplication.update");
            Assert.IsFalse(tcs.Task.IsCompleted);
            subscribed();
            Assert.AreSame(subscribed, unsubscribed);
            Assert.IsTrue(tcs.Task.IsCompleted);
            AssertCapture(tcs.Task.Result, "scene_view_camera", false, null);
        }

        [Test]
        public void Camera_WithoutLocator_DisclosesCameraMainPath()
        {
            GameObject cameraObject = Track(new GameObject("DefaultScreenshotCamera"));
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();

            JObject result = new ScreenshotCameraTool().Execute(new JObject
            {
                ["width"] = 8,
                ["height"] = 8
            });

            AssertCapture(result, "camera_main", false, null);
        }

        [Test]
        public void Camera_PrefabSessionWithoutLocator_DisclosesPrefabMainCameraPath()
        {
            GameObject prefabRoot = OpenTestPrefabSession();
            GameObject cameraObject = Track(new GameObject("PrefabScreenshotCamera"));
            Assert.That(_createdObjects, Does.Contain(cameraObject));
            cameraObject.transform.SetParent(prefabRoot.transform, false);
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();

            JObject result = new ScreenshotCameraTool().Execute(new JObject
            {
                ["width"] = 8,
                ["height"] = 8
            });

            AssertCapture(result, "prefab_main_camera", false, null);
        }

        [Test]
        public void TearDown_ContinuesAndResetsOwnershipWhenPrefabDiscardThrows()
        {
            GameObject cleanupProbe = Track(new GameObject("TearDownCleanupProbe"));
            _ownsPrefabSession = true;
            _discardPrefabSession =
                () => throw new InvalidOperationException("Injected discard failure");

            InvalidOperationException cleanupFailure =
                Assert.Throws<InvalidOperationException>(() => TearDown());

            Assert.IsFalse(_ownsPrefabSession);
            Assert.IsTrue(cleanupProbe == null, "Tracked objects must be cleaned after Discard fails");
            Assert.That(cleanupFailure.Message, Does.Contain("Injected discard failure"));
        }

        [TestCase("game_view", 0, 64)]
        [TestCase("game_view", 4097, 64)]
        [TestCase("scene_view", 64, 0)]
        [TestCase("scene_view", 64, 4097)]
        [TestCase("camera", 0, 64)]
        [TestCase("camera", 64, 4097)]
        public void ScreenshotDimensions_OutsideBounds_ReturnValidationError(
            string tool,
            int width,
            int height)
        {
            JObject parameters = new JObject
            {
                ["width"] = width,
                ["height"] = height
            };
            JObject result;
            if (tool == "camera")
            {
                result = new ScreenshotCameraTool().Execute(parameters);
            }
            else
            {
                var tcs = new TaskCompletionSource<JObject>();
                if (tool == "game_view")
                    new ScreenshotGameViewTool().ExecuteAsync(parameters, tcs);
                else
                    new ScreenshotSceneViewTool().ExecuteAsync(parameters, tcs);
                Assert.IsTrue(tcs.Task.IsCompleted);
                result = tcs.Task.Result;
            }

            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("4096"));
        }

        [Test]
        public void Camera_ExplicitLocator_DisclosesExplicitCameraPath()
        {
            GameObject cameraObject = Track(new GameObject("ExplicitScreenshotCamera"));
            cameraObject.AddComponent<Camera>();

            JObject result = new ScreenshotCameraTool().Execute(new JObject
            {
                ["cameraInstanceId"] = cameraObject.GetInstanceID(),
                ["width"] = 8,
                ["height"] = 8
            });

            AssertCapture(result, "explicit_camera", false, null);
        }

        private static MethodInfo DummyMethod =>
            typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes);

        private void ConfigureRenderView(
            EditorWindow window,
            MethodInfo method,
            Func<MethodInfo, EditorWindow, RenderTexture> invoke)
        {
            SetGameViewSeam("_resolveGameViewType", new Func<Type>(() => typeof(TestEditorWindow)));
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, __) => window));
            SetGameViewSeam(
                "_resolveRenderViewMethod",
                new Func<Type, MethodInfo>(_ => method));
            SetGameViewSeam("_invokeRenderView", invoke);
        }

        private void ConfigureRenderViewFailure(string scenario)
        {
            if (scenario == "gameview_type_missing")
            {
                SetGameViewSeam("_resolveGameViewType", new Func<Type>(() => null));
                return;
            }

            SetGameViewSeam("_resolveGameViewType", new Func<Type>(() => typeof(TestEditorWindow)));
            if (scenario == "window_null")
            {
                SetGameViewSeam(
                    "_getGameViewWindow",
                    new Func<Type, bool, EditorWindow>((_, __) => null));
                return;
            }

            TestEditorWindow window = CreateTestWindow();
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, __) => window));
            if (scenario == "method_missing")
            {
                SetGameViewSeam(
                    "_resolveRenderViewMethod",
                    new Func<Type, MethodInfo>(_ => null));
                return;
            }

            SetGameViewSeam(
                "_resolveRenderViewMethod",
                new Func<Type, MethodInfo>(_ => DummyMethod));
            if (scenario == "rendertexture_null")
            {
                SetGameViewSeam(
                    "_invokeRenderView",
                    new Func<MethodInfo, EditorWindow, RenderTexture>((_, __) => null));
            }
            else
            {
                SetGameViewSeam(
                    "_invokeRenderView",
                    new Func<MethodInfo, EditorWindow, RenderTexture>((_, __) =>
                        throw new InvalidOperationException("Injected RenderView failure")));
            }
        }

        private void ConfigureMethodMissing(bool createWindowDuringRequest)
        {
            SetGameViewSeam("_resolveGameViewType", new Func<Type>(() => typeof(TestEditorWindow)));
            TestEditorWindow existing = createWindowDuringRequest ? null : CreateTestWindow();
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, __) =>
                {
                    if (existing != null)
                        return existing;

                    TestEditorWindow[] windows =
                        UnityEngine.Resources.FindObjectsOfTypeAll<TestEditorWindow>();
                    return windows.Length > 0 ? windows[0] : CreateTestWindow();
                }));
            SetGameViewSeam(
                "_resolveRenderViewMethod",
                new Func<Type, MethodInfo>(_ => null));
        }

        private GameObject OpenTestPrefabSession()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "McpUnityScreenshotObservabilityTests");

            GameObject source = Track(new GameObject("ScreenshotObservability"));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, TestPrefabPath, out bool success);
                Assert.IsTrue(success, "Test Prefab setup must succeed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            GameObject root = PrefabEditingService.Open(TestPrefabPath);
            _ownsPrefabSession = true;
            return Track(root);
        }

        private JObject ExecuteGameView()
        {
            var tcs = new TaskCompletionSource<JObject>();
            new ScreenshotGameViewTool().ExecuteAsync(new JObject
            {
                ["width"] = 8,
                ["height"] = 8
            }, tcs);
            Assert.IsTrue(
                tcs.Task.IsCompleted,
                "Non-focused Game View capture should complete synchronously");
            return tcs.Task.Result;
        }

        private TestEditorWindow CreateTestWindow()
        {
            return Track(ScriptableObject.CreateInstance<TestEditorWindow>());
        }

        private RenderTexture CreateSourceRenderTexture()
        {
            var renderTexture = Track(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32));
            renderTexture.Create();
            return renderTexture;
        }

        private static Texture2D CreateTexture()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGB24, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }

        private static void AssertCapture(
            JObject result,
            string capturePath,
            bool degraded,
            string degradedReason)
        {
            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual("image", result["type"]?.ToString());
            Assert.That(result["data"]?.ToString(), Is.Not.Null.And.Not.Empty);
            Assert.AreEqual(capturePath, result["capturePath"]?.ToString());
            Assert.AreEqual(degraded, result["degraded"]?.ToObject<bool>() ?? !degraded);
            if (degraded)
                Assert.AreEqual(degradedReason, result["degradedReason"]?.ToString());
            else
                Assert.IsNull(result["degradedReason"]);
            Assert.That(result["message"]?.ToString(), Does.Contain(capturePath));
        }

        private void SetGameViewSeam(string name, object value)
        {
            SetPrivateStaticField(typeof(ScreenshotGameViewTool), name, value);
        }

        private void SetSceneViewSeam(string name, object value)
        {
            SetPrivateStaticField(typeof(ScreenshotSceneViewTool), name, value);
        }

        private T Track<T>(T createdObject) where T : UnityEngine.Object
        {
            _createdObjects.Add(createdObject);
            return createdObject;
        }

        private void ResetPrefabOwnership(Action discard, ref Exception firstFailure)
        {
            try
            {
                if (_ownsPrefabSession)
                    TryCleanup(discard, ref firstFailure);
            }
            finally
            {
                _ownsPrefabSession = false;
            }
        }

        private static void DestroyObjects<T>(IEnumerable<T> objects)
            where T : UnityEngine.Object
        {
            Exception firstFailure = null;
            foreach (T createdObject in objects)
            {
                if (createdObject != null)
                {
                    TryCleanup(
                        () => UnityEngine.Object.DestroyImmediate(createdObject),
                        ref firstFailure);
                }
            }

            if (firstFailure != null)
                throw firstFailure;
        }

        private static void TryCleanup(Action cleanup, ref Exception firstFailure)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex)
            {
                if (firstFailure == null)
                    firstFailure = ex;
            }
        }

        private static void CaptureSeams(
            Type ownerType,
            IDictionary<string, object> target,
            params string[] names)
        {
            foreach (string name in names)
                target[name] = GetPrivateStaticField(ownerType, name);
        }

        private static void RestoreSeams(Type ownerType, IDictionary<string, object> originals)
        {
            foreach (KeyValuePair<string, object> original in originals)
                SetPrivateStaticField(ownerType, original.Key, original.Value);
        }

        private static object GetPrivateStaticField(Type ownerType, string name)
        {
            FieldInfo field = ownerType.GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new MissingFieldException(ownerType.FullName, name);
            return field.GetValue(null);
        }

        private static void SetPrivateStaticField(Type ownerType, string name, object value)
        {
            FieldInfo field = ownerType.GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                Assert.Fail($"{ownerType.Name} private field '{name}' was not found");
            field.SetValue(null, value);
        }
    }
}
