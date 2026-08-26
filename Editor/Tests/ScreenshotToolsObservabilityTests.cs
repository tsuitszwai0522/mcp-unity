using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
                "_getGameViewWindow",
                "_resolveGameViewHost",
                "_resolveActualView",
                "_resolveRepaintImmediatelyMethod",
                "_invokeRepaintImmediately",
                "_subscribeBeginCameraRendering",
                "_unsubscribeBeginCameraRendering",
                "_subscribeCameraPreRender",
                "_unsubscribeCameraPreRender",
                "_findAllCameras",
                "_findLoadedSceneHandles",
                "_setCameraEnabled");
            SetGameViewSeam(
                "_findAllCameras",
                new Func<IEnumerable<Camera>>(() => Array.Empty<Camera>()));
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
            Camera renderedCamera = Track(new GameObject("RenderViewPathGameCamera"))
                .AddComponent<Camera>();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            ConfigureHandshakeWithRenderedCameras(window, renderedCamera);
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(() =>
                {
                    Assert.Fail("ScreenCapture fallback must not run after RenderView succeeds");
                    return null;
                }));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("verified", result["frameFresh"]?.ToString());
            Assert.AreEqual("camera_render_observed", result["frameFreshReason"]?.ToString());
        }

        [Test]
        public void GameView_FixtureDefaultsToControlledEmptyCameraEnumeration()
        {
            Scene externalPreviewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("S8ExternalPreviewCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, externalPreviewScene);
                cameraObject.AddComponent<Camera>();
                var findAllCameras = (Func<IEnumerable<Camera>>)GetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_findAllCameras");

                Assert.IsEmpty(
                    findAllCameras(),
                    "The fixture must never enumerate ambient Editor cameras by default");
            }
            finally
            {
                if (externalPreviewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(externalPreviewScene);
            }
        }

        [Test]
        public void GameView_DescriptionExplainsForceFocusRerenderRemediation()
        {
            var tool = new ScreenshotGameViewTool();

            Assert.That(tool.Description, Does.Contain("force_focus=true"));
            Assert.That(tool.Description, Does.Contain("active tab"));
            Assert.That(tool.Description, Does.Contain("rerenders before capture"));
            Assert.That(tool.Description, Does.Contain("only when isolatedCameraCount=0"));
            Assert.That(tool.Description, Does.Contain("no_camera_render"));
            Assert.That(tool.Description, Does.Contain("no force-focus remediation"));
            Assert.That(
                tool.Description,
                Does.Contain("Only frameFresh=verified means the pixels reflect the current scene"));
        }

        [Test]
        public void GameView_ForceFocus_WaitsTwoUpdatesThenUnsubscribesAndCompletes()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            Camera renderedCamera = Track(new GameObject("ForceFocusRenderedCamera"))
                .AddComponent<Camera>();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            ConfigureHandshakeWithRenderedCameras(window, renderedCamera);
            SetGameViewSeam(
                "_getGameViewWindow",
                new Func<Type, bool, EditorWindow>((_, focus) =>
                    focus ? null : window));
            Delegate beforeUpdate = GetStaticDelegate(typeof(EditorApplication), "update");
            var beforeHandlers = new HashSet<Delegate>(
                beforeUpdate?.GetInvocationList() ?? Array.Empty<Delegate>());
            var tcs = new TaskCompletionSource<JObject>();
            EditorApplication.CallbackFunction deferredHandler = null;

            try
            {
                new ScreenshotGameViewTool().ExecuteAsync(new JObject
                {
                    ["width"] = 8,
                    ["height"] = 8,
                    ["force_focus"] = true
                }, tcs);

                Delegate afterSubscribe = GetStaticDelegate(
                    typeof(EditorApplication), "update");
                deferredHandler = afterSubscribe.GetInvocationList()
                    .OfType<EditorApplication.CallbackFunction>()
                    .Single(handler => !beforeHandlers.Contains(handler));
                Assert.IsFalse(tcs.Task.IsCompleted);

                deferredHandler();

                Assert.IsFalse(
                    tcs.Task.IsCompleted,
                    "The first editor update must only decrement the two-frame counter");
                Assert.IsTrue(StaticDelegateContains(
                    typeof(EditorApplication), "update", deferredHandler));

                deferredHandler();

                Assert.IsFalse(StaticDelegateContains(
                    typeof(EditorApplication), "update", deferredHandler));
                Assert.IsTrue(tcs.Task.IsCompleted);
                AssertCapture(tcs.Task.Result, "render_view", false, null);
            }
            finally
            {
                if (deferredHandler != null)
                    EditorApplication.update -= deferredHandler;
            }
        }

        [Test]
        public void GameView_ProductionRenderCounterSeams_SubscribeAndUnsubscribeBothEvents()
        {
            var subscribeSrp =
                (Action<Action<ScriptableRenderContext, Camera>>)
                    _originalGameViewSeams["_subscribeBeginCameraRendering"];
            var unsubscribeSrp =
                (Action<Action<ScriptableRenderContext, Camera>>)
                    _originalGameViewSeams["_unsubscribeBeginCameraRendering"];
            var subscribeBuiltIn =
                (Action<Camera.CameraCallback>)
                    _originalGameViewSeams["_subscribeCameraPreRender"];
            var unsubscribeBuiltIn =
                (Action<Camera.CameraCallback>)
                    _originalGameViewSeams["_unsubscribeCameraPreRender"];
            Action<ScriptableRenderContext, Camera> srpHandler = (_, __) => { };
            Camera.CameraCallback builtInHandler = _ => { };

            try
            {
                subscribeSrp(srpHandler);
                Assert.IsTrue(StaticDelegateContains(
                    typeof(RenderPipelineManager),
                    "beginCameraRendering",
                    srpHandler));

                subscribeBuiltIn(builtInHandler);
                Assert.IsTrue(StaticDelegateContains(
                    typeof(Camera), "onPreRender", builtInHandler));

                unsubscribeBuiltIn(builtInHandler);
                Assert.IsFalse(StaticDelegateContains(
                    typeof(Camera), "onPreRender", builtInHandler));

                unsubscribeSrp(srpHandler);
                Assert.IsFalse(StaticDelegateContains(
                    typeof(RenderPipelineManager),
                    "beginCameraRendering",
                    srpHandler));
            }
            finally
            {
                unsubscribeBuiltIn(builtInHandler);
                unsubscribeSrp(srpHandler);
            }
        }

        [Test]
        public void GameView_FreshnessHandshake_CountsBothRenderCallbacksAndUnsubscribes()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            Camera renderedCamera = Track(new GameObject("FreshnessRenderedCamera"))
                .AddComponent<Camera>();
            object host = new object();
            Action<ScriptableRenderContext, Camera> subscribedSrp = null;
            Camera.CameraCallback subscribedBuiltIn = null;
            bool srpUnsubscribed = false;
            bool builtInUnsubscribed = false;
            bool repaintInvoked = false;
            bool repaintPrecededRenderView = false;
            ConfigureRenderView(window, DummyMethod, (_, __) =>
            {
                repaintPrecededRenderView = repaintInvoked;
                return source;
            });
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ => window));
            SetGameViewSeam(
                "_resolveRepaintImmediatelyMethod",
                new Func<object, MethodInfo>(_ => DummyMethod));
            SetGameViewSeam(
                "_subscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(
                    handler => subscribedSrp = handler));
            SetGameViewSeam(
                "_unsubscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(handler =>
                {
                    Assert.AreSame(subscribedSrp, handler);
                    srpUnsubscribed = true;
                }));
            SetGameViewSeam(
                "_subscribeCameraPreRender",
                new Action<Camera.CameraCallback>(handler => subscribedBuiltIn = handler));
            SetGameViewSeam(
                "_unsubscribeCameraPreRender",
                new Action<Camera.CameraCallback>(handler =>
                {
                    Assert.AreSame(subscribedBuiltIn, handler);
                    builtInUnsubscribed = true;
                }));
            SetGameViewSeam(
                "_invokeRepaintImmediately",
                new Action<MethodInfo, object>((method, resolvedHost) =>
                {
                    Assert.AreSame(DummyMethod, method);
                    Assert.AreSame(host, resolvedHost);
                    repaintInvoked = true;
                    subscribedSrp(default, renderedCamera);
                    subscribedBuiltIn(renderedCamera);
                }));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("verified", result["frameFresh"]?.ToString());
            Assert.AreEqual(2, result["cameraRenders"]?.ToObject<int>());
            Assert.IsTrue(repaintPrecededRenderView);
            Assert.IsTrue(srpUnsubscribed);
            Assert.IsTrue(builtInUnsubscribed);
            Assert.That(result["message"]?.ToString(), Does.Contain("frameFresh=verified"));
            Assert.That(result["message"]?.ToString(), Does.Contain("cameraRenders=2"));
        }

        [Test]
        public void GameView_CompletedHandshakeWithoutCameraRender_RemainsNonDegraded()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            ConfigureHandshakeInvocation(window, (_, __) => { });

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
            Assert.AreEqual("no_camera_render", result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Not.Contain("retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_RepaintExceptionReportsKnownAbsentFreshnessAndForceFocusHint()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            bool renderViewInvoked = false;
            ConfigureRenderView(window, DummyMethod, (_, __) =>
            {
                renderViewInvoked = true;
                return source;
            });
            ConfigureHandshakeInvocation(
                window,
                (_, __) => throw new InvalidOperationException("Injected repaint failure"));

            JObject result = ExecuteGameView();

            Assert.IsTrue(renderViewInvoked);
            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
            Assert.AreEqual(
                "repaint_immediately_unavailable:InvalidOperationException",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("repaint_immediately_unavailable:InvalidOperationException"));
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_FreshnessHandshake_CountsOnlyGameCameras()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            Camera gameCamera = Track(new GameObject("FreshnessGameCamera"))
                .AddComponent<Camera>();
            var previewUtility = new PreviewRenderUtility();
            SceneView sceneView = Track(ScriptableObject.CreateInstance<SceneView>());
            try
            {
                Camera previewCamera = previewUtility.camera;
                Camera sceneViewCamera = sceneView.camera;
                Assert.AreEqual(CameraType.Preview, previewCamera.cameraType);
                Assert.AreEqual(CameraType.SceneView, sceneViewCamera.cameraType);

                ConfigureRenderView(window, DummyMethod, (_, __) => source);
                Action<ScriptableRenderContext, Camera> srpHandler = null;
                Camera.CameraCallback builtInHandler = null;
                ConfigureHandshakeInvocation(window, (_, __) =>
                {
                    srpHandler(default, previewCamera);
                    builtInHandler(sceneViewCamera);
                    srpHandler(default, gameCamera);
                });
                SetGameViewSeam(
                    "_subscribeBeginCameraRendering",
                    new Action<Action<ScriptableRenderContext, Camera>>(
                        handler => srpHandler = handler));
                SetGameViewSeam(
                    "_subscribeCameraPreRender",
                    new Action<Camera.CameraCallback>(handler => builtInHandler = handler));

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.AreEqual("verified", result["frameFresh"]?.ToString());
                Assert.AreEqual(1, result["cameraRenders"]?.ToObject<int>());
            }
            finally
            {
                previewUtility.Cleanup();
            }
        }

        [Test]
        public void GameView_FreshnessHandshake_HostNullReportsReason()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => null));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "repaint_immediately_unavailable:host_null",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_FreshnessHandshake_MethodMissingReportsReason()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            object host = new object();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ => window));
            SetGameViewSeam(
                "_resolveRepaintImmediatelyMethod",
                new Func<object, MethodInfo>(_ => null));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "repaint_immediately_unavailable:method_missing",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_FreshnessHandshake_UnresolvedActualViewReportsReason()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            object host = new object();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ => null));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "actual_view_unresolved",
                result["frameFreshReason"]?.ToString());
        }

        [Test]
        public void GameView_FreshnessHandshake_ActualViewExceptionReportsUnresolvedReason()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            object host = new object();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ =>
                    throw new MissingMemberException("actualView")));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "actual_view_unresolved",
                result["frameFreshReason"]?.ToString());
        }

        [Test]
        public void GameView_InactiveTabWithIsolationFlagsFrameAsPredatingIsolation()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("S8InactiveTabOrphanCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));

                TestEditorWindow window = CreateTestWindow();
                TestEditorWindow activeTab = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                object host = new object();
                ConfigureRenderView(window, DummyMethod, (_, __) => source);
                SetGameViewSeam(
                    "_resolveGameViewHost",
                    new Func<EditorWindow, object>(_ => host));
                SetGameViewSeam(
                    "_resolveActualView",
                    new Func<object, EditorWindow>(_ => activeTab));

                JObject result = ExecuteGameView();

                AssertCapture(
                    result,
                    "render_view",
                    true,
                    "isolated_frame_predates_isolation");
                Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
                Assert.AreEqual(
                    "game_view_not_active_tab",
                    result["frameFreshReason"]?.ToString());
                Assert.AreEqual(1, result["isolatedCameraCount"]?.ToObject<int>());
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Contain("remediation=retry_with_force_focus=true"));
                Assert.IsTrue(orphanCamera.enabled);
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_ProductionFreshnessReflectionSeams_ResolveExpectedMembers()
        {
            var resolveGameViewType =
                (Func<Type>)_originalGameViewSeams["_resolveGameViewType"];
            var hasExistingWindow =
                (Func<Type, bool>)_originalGameViewSeams["_hasExistingEditorWindow"];
            var getGameViewWindow =
                (Func<Type, bool, EditorWindow>)_originalGameViewSeams["_getGameViewWindow"];
            var resolveHost =
                (Func<EditorWindow, object>)_originalGameViewSeams["_resolveGameViewHost"];
            var resolveActualView =
                (Func<object, EditorWindow>)_originalGameViewSeams["_resolveActualView"];
            var resolveRepaint =
                (Func<object, MethodInfo>)
                    _originalGameViewSeams["_resolveRepaintImmediatelyMethod"];
            var invokeRepaint =
                (Action<MethodInfo, object>)
                    _originalGameViewSeams["_invokeRepaintImmediately"];
            Type gameViewType = resolveGameViewType();
            Assert.IsNotNull(gameViewType);
            bool hadExistingWindow = hasExistingWindow(gameViewType);
            EditorWindow gameView = getGameViewWindow(gameViewType, false);
            try
            {
                Assert.AreSame(
                    _originalGameViewSeams["_resolveGameViewHost"],
                    GetPrivateStaticField(typeof(ScreenshotGameViewTool), "_resolveGameViewHost"));
                Assert.AreSame(
                    _originalGameViewSeams["_resolveActualView"],
                    GetPrivateStaticField(typeof(ScreenshotGameViewTool), "_resolveActualView"));
                Assert.AreSame(
                    _originalGameViewSeams["_resolveRepaintImmediatelyMethod"],
                    GetPrivateStaticField(
                        typeof(ScreenshotGameViewTool),
                        "_resolveRepaintImmediatelyMethod"));
                Assert.IsNotNull(gameView);
                gameView.Focus();

                FieldInfo parentField = typeof(EditorWindow).GetField(
                    "m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(parentField);
                object host = resolveHost(gameView);
                Assert.IsNotNull(host);
                Assert.AreEqual("UnityEditor.DockArea", host.GetType().FullName);
                Assert.AreEqual("UnityEditor.HostView", host.GetType().BaseType?.FullName);
                Assert.AreEqual("UnityEditor.GUIView", host.GetType().BaseType?.BaseType?.FullName);

                PropertyInfo actualViewProperty = host.GetType().GetProperty(
                    "actualView",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(actualViewProperty);
                Assert.IsNull(host.GetType().GetField(
                    "actualView",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsNull(host.GetType().GetField(
                    "m_ActualView",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.IsFalse(actualViewProperty.GetMethod?.IsStatic ?? true);
                EditorWindow resolvedActualView = resolveActualView(host);
                Assert.IsNotNull(resolvedActualView);
                Assert.AreSame(gameView, resolvedActualView);
                Assert.AreSame(
                    actualViewProperty.GetValue(host, null),
                    resolvedActualView);

                MethodInfo repaintImmediately = resolveRepaint(host);
                Assert.IsNotNull(repaintImmediately);
                Assert.AreEqual("RepaintImmediately", repaintImmediately.Name);
                Assert.AreEqual(0, repaintImmediately.GetParameters().Length);
                Assert.DoesNotThrow(() => invokeRepaint(repaintImmediately, host));
            }
            finally
            {
                if (!hadExistingWindow && gameView != null)
                    gameView.Close();
            }
        }

        [Test]
        public void GameView_ValidUnlistedNonPreviewGameCamera_IsNotDisabled()
        {
            Camera camera = Track(new GameObject("S8DontDestroyEquivalentCamera"))
                .AddComponent<Camera>();
            Assert.IsTrue(camera.gameObject.scene.IsValid());
            Assert.IsFalse(EditorSceneManager.IsPreviewScene(camera.gameObject.scene));
            SetGameViewSeam(
                "_findAllCameras",
                new Func<IEnumerable<Camera>>(() => new[] { camera }));
            SetGameViewSeam(
                "_findLoadedSceneHandles",
                new Func<HashSet<int>>(() => new HashSet<int>()));
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            bool stayedEnabledDuringRender = false;
            ConfigureRenderView(window, DummyMethod, (_, __) =>
            {
                stayedEnabledDuringRender = camera.enabled;
                return source;
            });

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.IsTrue(stayedEnabledDuringRender);
            Assert.IsTrue(camera.enabled);
            Assert.AreEqual(0, result["isolatedCameraCount"]?.ToObject<int>());
        }

        [Test]
        public void GameView_IsolationSkipsEnabledPreviewSceneCameraType()
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var previewCameraObject = new GameObject("S8PreviewCameraTypeFilter");
                SceneManager.MoveGameObjectToScene(previewCameraObject, previewScene);
                Camera previewCamera = previewCameraObject.AddComponent<Camera>();
                previewCamera.enabled = true;
                previewCamera.cameraType = CameraType.Preview;

                Assert.AreEqual(CameraType.Preview, previewCamera.cameraType);
                Assert.IsTrue(previewCamera.enabled);
                Assert.IsTrue(previewCamera.gameObject.activeInHierarchy);
                Assert.IsTrue(previewCamera.gameObject.scene.IsValid());
                Assert.IsTrue(EditorSceneManager.IsPreviewScene(previewCamera.gameObject.scene));
                var findLoadedSceneHandles =
                    (Func<HashSet<int>>)_originalGameViewSeams["_findLoadedSceneHandles"];
                Assert.IsFalse(
                    findLoadedSceneHandles().Contains(previewCamera.gameObject.scene.handle));
                bool disableAttempted = false;
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { previewCamera }));
                SetGameViewSeam(
                    "_setCameraEnabled",
                    new Action<Camera, bool>((camera, enabled) =>
                    {
                        if (ReferenceEquals(camera, previewCamera) && !enabled)
                            disableAttempted = true;
                        camera.enabled = enabled;
                    }));
                Camera renderedCamera = Track(new GameObject("S8PreviewFilterGameCamera"))
                    .AddComponent<Camera>();
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                bool previewStayedEnabledDuringRender = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    previewStayedEnabledDuringRender = previewCamera.enabled;
                    return source;
                });
                ConfigureHandshakeWithRenderedCameras(window, renderedCamera);

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.IsFalse(disableAttempted);
                Assert.IsTrue(previewStayedEnabledDuringRender);
                Assert.IsTrue(previewCamera.enabled);
                Assert.That(
                    ((JArray)result["isolatedCameras"])
                        .Any(item => item["name"]?.ToString() == previewCamera.name),
                    Is.False);
                Assert.AreEqual(0, result["isolatedCameraCount"]?.ToObject<int>());
            }
            finally
            {
                if (previewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [Test]
        public void GameView_IsolationDoesNotTouchActivePrefabStageCamera()
        {
            if (!AssetDatabase.IsValidFolder(TestDirectory))
                AssetDatabase.CreateFolder("Assets", "McpUnityScreenshotObservabilityTests");
            var sourceRoot = new GameObject("S8PrefabStageRoot");
            var sourceCameraObject = new GameObject("S8PrefabStageCamera");
            sourceCameraObject.transform.SetParent(sourceRoot.transform, false);
            sourceCameraObject.AddComponent<Camera>();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(sourceRoot, TestPrefabPath, out bool success);
                Assert.IsTrue(success, "Prefab Stage fixture asset must be created");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceRoot);
            }

            var prefabStage = PrefabStageUtility.OpenPrefab(TestPrefabPath);
            try
            {
                Assert.IsNotNull(prefabStage);
                Assert.AreSame(prefabStage, PrefabStageUtility.GetCurrentPrefabStage());
                Camera stageCamera =
                    prefabStage.prefabContentsRoot.GetComponentInChildren<Camera>(true);
                Assert.IsNotNull(stageCamera);
                Assert.AreEqual(CameraType.Game, stageCamera.cameraType);
                Assert.IsTrue(stageCamera.enabled);
                Assert.IsTrue(stageCamera.gameObject.activeInHierarchy);
                Assert.IsTrue(EditorSceneManager.IsPreviewScene(stageCamera.gameObject.scene));
                var findLoadedSceneHandles =
                    (Func<HashSet<int>>)_originalGameViewSeams["_findLoadedSceneHandles"];
                Assert.IsFalse(
                    findLoadedSceneHandles().Contains(stageCamera.gameObject.scene.handle));
                bool disableAttempted = false;
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { stageCamera }));
                SetGameViewSeam(
                    "_setCameraEnabled",
                    new Action<Camera, bool>((camera, enabled) =>
                    {
                        if (ReferenceEquals(camera, stageCamera) && !enabled)
                            disableAttempted = true;
                        camera.enabled = enabled;
                    }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                bool stageCameraStayedEnabledDuringRender = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    stageCameraStayedEnabledDuringRender = stageCamera.enabled;
                    return source;
                });
                ConfigureHandshakeWithRenderedCameras(window, stageCamera);

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.IsFalse(disableAttempted);
                Assert.IsTrue(stageCameraStayedEnabledDuringRender);
                Assert.IsTrue(stageCamera.enabled);
                Assert.AreEqual(0, result["isolatedCameraCount"]?.ToObject<int>());
                Assert.AreEqual(1, result["contextCameraCount"]?.ToObject<int>());
            }
            finally
            {
                StageUtility.GoToMainStage();
            }
        }

        [Test]
        public void GameView_IsolatesOnlyOrphanCameras_RestoresAndDisclosesBoundedLists()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanCameras = new List<Camera>();
                for (int index = 0; index < 10; index++)
                {
                    var cameraObject = new GameObject($"S8OrphanCamera{index}");
                    SceneManager.MoveGameObjectToScene(cameraObject, orphanScene);
                    orphanCameras.Add(cameraObject.AddComponent<Camera>());
                }

                Camera disabledOrphan = new GameObject("S8DisabledOrphanCamera")
                    .AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(disabledOrphan.gameObject, orphanScene);
                disabledOrphan.enabled = false;
                Camera inactiveOrphan = new GameObject("S8InactiveOrphanCamera")
                    .AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(inactiveOrphan.gameObject, orphanScene);
                inactiveOrphan.gameObject.SetActive(false);

                GameObject prefabRoot = OpenTestPrefabSession();
                GameObject contextCameraObject = Track(new GameObject("S8SessionContextCamera"));
                contextCameraObject.transform.SetParent(prefabRoot.transform, false);
                Camera contextCamera = contextCameraObject.AddComponent<Camera>();
                Camera loadedCamera = Track(new GameObject("S8LoadedSceneCamera"))
                    .AddComponent<Camera>();
                var candidates = new List<Camera>(orphanCameras)
                {
                    null,
                    disabledOrphan,
                    inactiveOrphan,
                    contextCamera,
                    loadedCamera
                };
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => candidates));

                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                bool orphansDisabledDuringRender = false;
                bool exclusionsStayedEnabledDuringRender = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    orphansDisabledDuringRender = orphanCameras.All(camera => !camera.enabled);
                    exclusionsStayedEnabledDuringRender =
                        contextCamera.enabled
                        && loadedCamera.enabled
                        && !disabledOrphan.enabled
                        && inactiveOrphan.enabled;
                    return source;
                });
                ConfigureHandshakeWithRenderedCameras(window, contextCamera);

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.IsTrue(orphansDisabledDuringRender);
                Assert.IsTrue(exclusionsStayedEnabledDuringRender);
                Assert.IsTrue(orphanCameras.All(camera => camera.enabled));
                Assert.IsFalse(disabledOrphan.enabled);
                Assert.IsTrue(inactiveOrphan.enabled);
                Assert.IsTrue(contextCamera.enabled);
                Assert.IsTrue(loadedCamera.enabled);

                var isolated = (JArray)result["isolatedCameras"];
                var context = (JArray)result["contextCameras"];
                int isolatedCount = result["isolatedCameraCount"].ToObject<int>();
                Assert.AreEqual(8, isolated.Count, "Isolated-camera disclosure must be capped");
                Assert.AreEqual(10, isolatedCount);
                Assert.That(
                    isolated.All(item => item["name"] != null && item["scenePath"] != null),
                    Is.True);
                Assert.That(
                    context.Any(item => item["name"]?.ToString() == contextCamera.name),
                    Is.True);
                Assert.That(
                    context.All(item => item["scenePath"] != null),
                    Is.True);
                Assert.That(result["message"]?.ToString(), Does.Contain("isolatedCameraCount=10"));
                Assert.That(result["message"]?.ToString(), Does.Contain("contextCameraCount=1"));
                Assert.That(result["message"]?.ToString(), Does.Not.Contain("isolatedCameras="));
                Assert.That(result["message"]?.ToString(), Does.Not.Contain("contextCameras="));
                Assert.That(result["message"]?.ToString(), Does.Not.Contain("S8OrphanCamera0"));
                Assert.That(result["message"]?.ToString(), Does.Not.Contain(contextCamera.name));
                Assert.AreEqual("unknown", result["frameFresh"]?.ToString());
                Assert.AreEqual(1, result["cameraRenders"]?.ToObject<int>());
                Assert.AreEqual(
                    "context_camera_may_compose;camera_render_observed",
                    result["frameFreshReason"]?.ToString());
                Assert.AreEqual(
                    1,
                    result["message"]?.ToString().Count(character => character == '['));
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_RenderViewFailure_StillRestoresIsolatedCamera()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("S8FailurePathOrphanCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, orphanScene);
                Camera orphanCamera = cameraObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));

                TestEditorWindow window = CreateTestWindow();
                bool disabledDuringRenderView = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    disabledDuringRenderView = !orphanCamera.enabled;
                    throw new InvalidOperationException("Injected RenderView failure");
                });
                ConfigureHandshakeInvocation(window, (_, __) => { });
                SetGameViewSeam(
                    "_captureScreenshotAsTexture",
                    new Func<Texture2D>(CreateTexture));

                JObject result = ExecuteGameView();

                Assert.IsTrue(disabledDuringRenderView);
                Assert.IsTrue(orphanCamera.enabled);
                AssertCapture(
                    result,
                    "screen_capture",
                    true,
                    "isolated_frame_predates_isolation;" +
                    "render_view_unavailable:exception:InvalidOperationException");
                Assert.AreEqual("not_applicable", result["frameFresh"]?.ToString());
                Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
                Assert.AreEqual(
                    "capture_path_not_render_view;no_camera_render",
                    result["frameFreshReason"]?.ToString());
                Assert.That(
                    ((JArray)result["isolatedCameras"])
                        .Any(item => item["name"]?.ToString() == orphanCamera.name),
                    Is.True);
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Not.Contain("retry_with_force_focus=true"));
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_RepaintUnavailableWithIsolation_IsNotFreshWithoutForceFocusHint()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("S8UnknownFreshnessOrphanCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, orphanScene);
                Camera orphanCamera = cameraObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSolidRenderTexture(Color.cyan);
                bool orphanDisabledDuringRenderView = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    orphanDisabledDuringRenderView = !orphanCamera.enabled;
                    return source;
                });
                SetGameViewSeam(
                    "_resolveGameViewHost",
                    new Func<EditorWindow, object>(_ => null));

                JObject result = ExecuteGameView();

                AssertCapture(
                    result,
                    "render_view",
                    true,
                    "isolated_frame_predates_isolation");
                Assert.IsTrue(orphanDisabledDuringRenderView);
                Assert.IsTrue(orphanCamera.enabled);
                Color capturedPixel = DecodeFirstPixel(result);
                Assert.That(capturedPixel.g, Is.GreaterThan(0.75f));
                Assert.That(capturedPixel.b, Is.GreaterThan(0.75f));
                Assert.That(capturedPixel.r, Is.LessThan(0.25f));
                Assert.AreEqual("not_fresh", result["frameFresh"]?.ToString());
                Assert.AreEqual(
                    "repaint_immediately_unavailable:host_null",
                    result["frameFreshReason"]?.ToString());
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Not.Contain("retry_with_force_focus=true"));
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_ContextCameraPreventsVerifiedFreshnessAfterGameRender()
        {
            GameObject prefabRoot = OpenTestPrefabSession();
            GameObject contextCameraObject = Track(new GameObject("S8CompositingContextCamera"));
            contextCameraObject.transform.SetParent(prefabRoot.transform, false);
            Camera contextCamera = contextCameraObject.AddComponent<Camera>();
            SetGameViewSeam(
                "_findAllCameras",
                new Func<IEnumerable<Camera>>(() => new[] { contextCamera }));
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            ConfigureHandshakeWithRenderedCameras(window, contextCamera);

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.IsTrue(contextCamera.enabled);
            Assert.AreEqual(1, result["contextCameraCount"]?.ToObject<int>());
            Assert.AreEqual(1, result["cameraRenders"]?.ToObject<int>());
            Assert.AreEqual("unknown", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "context_camera_may_compose;camera_render_observed",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Not.Contain("retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_ObservedRerenderSurvivesUnsubscribeOverrideForIsolationGate()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("S8UnsubscribeOrphanCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                Camera renderedCamera = Track(new GameObject("S8ObservedLoadedCamera"))
                    .AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                ConfigureRenderView(window, DummyMethod, (_, __) => source);
                ConfigureHandshakeWithRenderedCameras(window, renderedCamera);
                SetGameViewSeam(
                    "_unsubscribeBeginCameraRendering",
                    new Action<Action<ScriptableRenderContext, Camera>>(_ =>
                        throw new InvalidOperationException("Injected unsubscribe failure")));

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.AreEqual(1, result["cameraRenders"]?.ToObject<int>());
                Assert.AreEqual("unknown", result["frameFresh"]?.ToString());
                Assert.AreEqual(
                    "render_counter_cleanup_failed:InvalidOperationException;" +
                    "camera_render_observed",
                    result["frameFreshReason"]?.ToString());
                Assert.IsTrue(orphanCamera.enabled);
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_CounterCleanupFailure_PreservesUnderlyingHintInPublicReason()
        {
            TestEditorWindow window = CreateTestWindow();
            RenderTexture source = CreateSourceRenderTexture();
            ConfigureRenderView(window, DummyMethod, (_, __) => source);
            ConfigureHandshakeInvocation(
                window,
                (_, __) => throw new InvalidOperationException("Injected repaint failure"));
            SetGameViewSeam(
                "_unsubscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(_ =>
                    throw new MissingMethodException("Injected cleanup failure")));

            JObject result = ExecuteGameView();

            AssertCapture(result, "render_view", false, null);
            Assert.AreEqual("unknown", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "render_counter_cleanup_failed:MissingMethodException;" +
                "repaint_immediately_unavailable:InvalidOperationException",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("remediation=retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_CounterCleanupFailureWithNoRender_DoesNotClaimPredatingFrame()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("S8CleanupUnknownOrphanCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                ConfigureRenderView(window, DummyMethod, (_, __) => source);
                ConfigureHandshakeInvocation(window, (_, __) => { });
                SetGameViewSeam(
                    "_unsubscribeBeginCameraRendering",
                    new Action<Action<ScriptableRenderContext, Camera>>(_ =>
                        throw new InvalidOperationException("Injected cleanup failure")));

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.AreEqual("unknown", result["frameFresh"]?.ToString());
                Assert.AreEqual(
                    "render_counter_cleanup_failed:InvalidOperationException;" +
                    "no_camera_render",
                    result["frameFreshReason"]?.ToString());
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Not.Contain("isolated_frame_predates_isolation"));
                Assert.IsTrue(orphanCamera.enabled);
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_IsolatedCountTracksCameraWhenSetterMutatesThenThrows()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("S8ThrowingIsolationCamera");
                var secondOrphanObject = new GameObject("S8IsolationContinuesCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                SceneManager.MoveGameObjectToScene(secondOrphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                Camera secondOrphanCamera = secondOrphanObject.AddComponent<Camera>();
                Camera renderedCamera = Track(new GameObject("S8IsolationObservedCamera"))
                    .AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(
                        () => new[] { orphanCamera, secondOrphanCamera }));
                bool disableAttempted = false;
                SetGameViewSeam(
                    "_setCameraEnabled",
                    new Action<Camera, bool>((camera, enabled) =>
                    {
                        if (!enabled && ReferenceEquals(camera, orphanCamera))
                        {
                            disableAttempted = true;
                            camera.enabled = false;
                            throw new InvalidOperationException("Injected camera isolation failure");
                        }
                        camera.enabled = enabled;
                    }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSolidRenderTexture(Color.magenta);
                bool renderViewInvokedWithCameraEnabled = false;
                bool secondCameraDisabledDuringRenderView = false;
                ConfigureRenderView(window, DummyMethod, (_, __) =>
                {
                    renderViewInvokedWithCameraEnabled = orphanCamera.enabled;
                    secondCameraDisabledDuringRenderView = !secondOrphanCamera.enabled;
                    return source;
                });
                ConfigureHandshakeWithRenderedCameras(window, renderedCamera);

                JObject result = ExecuteGameView();

                AssertCapture(result, "render_view", false, null);
                Assert.IsTrue(disableAttempted);
                Assert.IsFalse(renderViewInvokedWithCameraEnabled);
                Assert.IsTrue(secondCameraDisabledDuringRenderView);
                Assert.IsTrue(orphanCamera.enabled);
                Assert.IsTrue(secondOrphanCamera.enabled);
                Color capturedPixel = DecodeFirstPixel(result);
                Assert.That(capturedPixel.r, Is.GreaterThan(0.75f));
                Assert.That(capturedPixel.b, Is.GreaterThan(0.75f));
                Assert.AreEqual(2, result["isolatedCameraCount"]?.ToObject<int>());
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_IsolationRestoreFailure_DoesNotReplaceResultOrSkipOtherCameras()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var firstObject = new GameObject("S8ThrowingRestoreCamera");
                var secondObject = new GameObject("S8RestoredAfterFailureCamera");
                SceneManager.MoveGameObjectToScene(firstObject, orphanScene);
                SceneManager.MoveGameObjectToScene(secondObject, orphanScene);
                Camera firstCamera = firstObject.AddComponent<Camera>();
                Camera secondCamera = secondObject.AddComponent<Camera>();
                Camera renderedCamera = Track(new GameObject("S8RestoreRenderedCamera"))
                    .AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(
                        () => new[] { firstCamera, secondCamera }));
                SetGameViewSeam(
                    "_setCameraEnabled",
                    new Action<Camera, bool>((camera, enabled) =>
                    {
                        if (enabled && ReferenceEquals(camera, firstCamera))
                            throw new InvalidOperationException("Injected camera restore failure");
                        camera.enabled = enabled;
                    }));
                TestEditorWindow window = CreateTestWindow();
                RenderTexture source = CreateSourceRenderTexture();
                ConfigureRenderView(window, DummyMethod, (_, __) => source);
                ConfigureHandshakeWithRenderedCameras(window, renderedCamera);

                JObject result = ExecuteGameView();

                AssertCapture(
                    result,
                    "render_view",
                    true,
                    "camera_restore_failed");
                Assert.IsFalse(firstCamera.enabled);
                Assert.IsTrue(secondCamera.enabled);
                Assert.AreEqual(2, result["isolatedCameraCount"]?.ToObject<int>());
                Assert.AreEqual("verified", result["frameFresh"]?.ToString());
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Contain("camera_restore_failed"));
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_IsolationCreateFailure_PreservesCountAndRestoreFailure()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("S8CreateFailureOrphanCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, orphanScene);
                Camera orphanCamera = cameraObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(
                        () => EnumerateCameraThenThrow(orphanCamera)));
                SetGameViewSeam(
                    "_setCameraEnabled",
                    new Action<Camera, bool>((camera, enabled) =>
                    {
                        if (enabled)
                            throw new InvalidOperationException("Injected restore failure");
                        camera.enabled = false;
                    }));

                JObject result = ExecuteGameView();
                string message = result["error"]?["message"]?.ToString();

                Assert.IsNotNull(result["error"]);
                Assert.IsFalse(orphanCamera.enabled);
                Assert.That(message, Does.Contain("capture_failed:InvalidOperationException"));
                Assert.That(message, Does.Contain("camera_restore_failed"));
                Assert.That(message, Does.Contain("isolatedCameraCount=1"));
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_ScreenCapturePreservesInactiveTabReasonAndRemediation()
        {
            TestEditorWindow window = CreateTestWindow();
            TestEditorWindow activeTab = CreateTestWindow();
            object host = new object();
            ConfigureRenderView(window, DummyMethod, (_, __) => null);
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ => activeTab));
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(CreateTexture));

            JObject result = ExecuteGameView();

            AssertCapture(
                result,
                "screen_capture",
                true,
                "render_view_unavailable:rendertexture_null");
            Assert.AreEqual("not_applicable", result["frameFresh"]?.ToString());
            Assert.AreEqual(
                "capture_path_not_render_view;game_view_not_active_tab",
                result["frameFreshReason"]?.ToString());
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("remediation=retry_with_force_focus=true"));
        }

        [Test]
        public void GameView_ScreenCapturePath_DisclosesProductionRenderViewReason()
        {
            TestEditorWindow window = CreateTestWindow();
            bool repaintInvoked = false;
            ConfigureRenderView(window, null, (_, __) => null);
            SetGameViewSeam(
                "_invokeRepaintImmediately",
                new Action<MethodInfo, object>((_, __) => repaintInvoked = true));
            SetGameViewSeam(
                "_captureScreenshotAsTexture",
                new Func<Texture2D>(CreateTexture));

            JObject result = ExecuteGameView();

            AssertCapture(
                result,
                "screen_capture",
                true,
                "render_view_unavailable:method_missing");
            Assert.AreEqual("not_applicable", result["frameFresh"]?.ToString());
            Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
            Assert.AreEqual(
                "capture_path_not_render_view;repaint_immediately_not_attempted",
                result["frameFreshReason"]?.ToString());
            Assert.IsFalse(repaintInvoked);
        }

        [Test]
        public void GameView_ScreenCaptureMethodMissingAfterIsolation_FlagsPredatingFrame()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("T1MethodMissingOrphanCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));

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
                    "isolated_frame_predates_isolation;" +
                    "render_view_unavailable:method_missing");
                Assert.AreEqual(1, result["isolatedCameraCount"]?.ToObject<int>());
                Assert.AreEqual("not_applicable", result["frameFresh"]?.ToString());
                Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
                Assert.AreEqual(
                    "capture_path_not_render_view;repaint_immediately_not_attempted",
                    result["frameFreshReason"]?.ToString());
                Assert.That(
                    result["degradedReason"]?.ToString(),
                    Does.Contain("isolated_frame_predates_isolation"));
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Contain("isolated_frame_predates_isolation"));
                Assert.IsTrue(orphanCamera.enabled);
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
        }

        [Test]
        public void GameView_MainCameraPath_AppendsScreenCaptureNullReason()
        {
            TestEditorWindow window = CreateTestWindow();
            GameObject cameraObject = Track(new GameObject("ScreenshotFallbackCamera"));
            Camera camera = cameraObject.AddComponent<Camera>();
            ConfigureRenderView(window, DummyMethod, (_, __) => null);
            ConfigureHandshakeInvocation(window, (_, __) => { });
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
            Assert.AreEqual("not_applicable", result["frameFresh"]?.ToString());
            Assert.AreEqual(0, result["cameraRenders"]?.ToObject<int>());
            Assert.AreEqual(
                "capture_path_not_render_view;no_camera_render",
                result["frameFreshReason"]?.ToString());
        }

        [Test]
        public void GameView_MainCameraFallbackAfterIsolation_DoesNotFlagPredatingFrame()
        {
            Scene orphanScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var orphanObject = new GameObject("S8MainFallbackOrphanCamera");
                SceneManager.MoveGameObjectToScene(orphanObject, orphanScene);
                Camera orphanCamera = orphanObject.AddComponent<Camera>();
                SetGameViewSeam(
                    "_findAllCameras",
                    new Func<IEnumerable<Camera>>(() => new[] { orphanCamera }));
                TestEditorWindow window = CreateTestWindow();
                GameObject cameraObject = Track(new GameObject("S8MainFallbackCamera"));
                Camera camera = cameraObject.AddComponent<Camera>();
                ConfigureRenderView(window, DummyMethod, (_, __) => null);
                ConfigureHandshakeInvocation(window, (_, __) => { });
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
                    "render_view_unavailable:rendertexture_null;" +
                    "screen_capture_returned_null");
                Assert.AreEqual(1, result["isolatedCameraCount"]?.ToObject<int>());
                Assert.AreEqual(
                    "capture_path_not_render_view;no_camera_render",
                    result["frameFreshReason"]?.ToString());
                Assert.That(
                    result["degradedReason"]?.ToString(),
                    Does.Not.Contain("isolated_frame_predates_isolation"));
                Assert.That(
                    result["message"]?.ToString(),
                    Does.Not.Contain("isolated_frame_predates_isolation"));
                Assert.IsTrue(orphanCamera.enabled);
            }
            finally
            {
                if (orphanScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(orphanScene);
            }
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
            Assert.That(message, Does.Contain("frameFresh=not_applicable"));
            Assert.That(message, Does.Contain("frameFreshReason=capture_path_not_render_view"));
            Assert.That(message, Does.Contain("isolatedCameraCount=0"));
            Assert.That(message, Does.Contain("contextCameraCount=0"));
            Assert.That(message, Does.Not.Contain("isolatedCameras="));
            Assert.That(message, Does.Not.Contain("contextCameras="));
            Assert.IsNull(result["isolatedCameras"]);
            Assert.IsNull(result["contextCameras"]);
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

        private static IEnumerable<Camera> EnumerateCameraThenThrow(Camera camera)
        {
            yield return camera;
            throw new InvalidOperationException("Injected camera enumeration failure");
        }

        private static Delegate GetStaticDelegate(Type ownerType, string fieldName)
        {
            FieldInfo field = ownerType.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(
                field,
                $"{ownerType.FullName} delegate field '{fieldName}' was not found");
            Assert.IsTrue(
                typeof(Delegate).IsAssignableFrom(field.FieldType),
                $"{ownerType.FullName}.{fieldName} must be a delegate field");
            return field.GetValue(null) as Delegate;
        }

        private static bool StaticDelegateContains(
            Type ownerType,
            string fieldName,
            Delegate expected)
        {
            Delegate current = GetStaticDelegate(ownerType, fieldName);
            return current != null
                && current.GetInvocationList().Contains(expected);
        }

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

        private void ConfigureHandshakeInvocation(
            EditorWindow gameView,
            Action<MethodInfo, object> invoke)
        {
            object host = new object();
            SetGameViewSeam(
                "_resolveGameViewHost",
                new Func<EditorWindow, object>(_ => host));
            SetGameViewSeam(
                "_resolveActualView",
                new Func<object, EditorWindow>(_ => gameView));
            SetGameViewSeam(
                "_resolveRepaintImmediatelyMethod",
                new Func<object, MethodInfo>(_ => DummyMethod));
            SetGameViewSeam(
                "_subscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(_ => { }));
            SetGameViewSeam(
                "_unsubscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(_ => { }));
            SetGameViewSeam(
                "_subscribeCameraPreRender",
                new Action<Camera.CameraCallback>(_ => { }));
            SetGameViewSeam(
                "_unsubscribeCameraPreRender",
                new Action<Camera.CameraCallback>(_ => { }));
            SetGameViewSeam("_invokeRepaintImmediately", invoke);
        }

        private void ConfigureHandshakeWithRenderedCameras(
            EditorWindow gameView,
            params Camera[] renderedCameras)
        {
            Action<ScriptableRenderContext, Camera> srpHandler = null;
            ConfigureHandshakeInvocation(gameView, (_, __) =>
            {
                foreach (Camera camera in renderedCameras)
                {
                    if (camera != null && camera.enabled)
                        srpHandler(default, camera);
                }
            });
            SetGameViewSeam(
                "_subscribeBeginCameraRendering",
                new Action<Action<ScriptableRenderContext, Camera>>(
                    handler => srpHandler = handler));
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

        private RenderTexture CreateSolidRenderTexture(Color color)
        {
            RenderTexture renderTexture = CreateSourceRenderTexture();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                GL.Clear(true, true, color);
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return renderTexture;
        }

        private static Color DecodeFirstPixel(JObject result)
        {
            byte[] png = Convert.FromBase64String(result["data"]?.ToString() ?? string.Empty);
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.IsTrue(texture.LoadImage(png));
                return texture.GetPixel(0, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
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
