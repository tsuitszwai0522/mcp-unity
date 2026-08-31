using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using McpUnity.Tools;
using McpUnity.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tests
{
    /// <summary>
    /// Tests for BatchExecuteTool functionality
    /// </summary>
    public class BatchExecuteToolTests
    {
        private BatchExecuteTool _batchTool;
        private GameObject _testObject;
        private string _testAssetFolder;
        private const string MalformedResultToolName = "batch_test_malformed_result";

        private sealed class MalformedResultTool : McpToolBase
        {
            internal MalformedResultTool()
            {
                Name = MalformedResultToolName;
            }

            public override JObject Execute(JObject parameters)
            {
                return new JObject
                {
                    ["success"] = new JObject()
                };
            }
        }

        private sealed class BatchTestEditorWindow : EditorWindow
        {
        }

        [SetUp]
        public void SetUp()
        {
            GetServerTools().Remove(MalformedResultToolName);
            ResetAtomicAssetWriteTracker();
            // Get the server instance to access registered tools
            _batchTool = new BatchExecuteTool(McpUnityServer.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            GetServerTools().Remove(MalformedResultToolName);
            ResetAtomicAssetWriteTracker();

            // Clean up any test objects
            if (_testObject != null)
            {
                Object.DestroyImmediate(_testObject);
                _testObject = null;
            }

            if (!string.IsNullOrEmpty(_testAssetFolder))
            {
                AssetDatabase.DeleteAsset(_testAssetFolder);
                _testAssetFolder = null;
            }
        }

        #region Basic Properties Tests

        [Test]
        public void BatchExecuteTool_HasCorrectName()
        {
            Assert.AreEqual("batch_execute", _batchTool.Name);
        }

        [Test]
        public void BatchExecuteTool_IsAsync()
        {
            Assert.IsTrue(_batchTool.IsAsync, "BatchExecuteTool should be async");
        }

        [Test]
        public void BatchExecuteTool_HasDescription()
        {
            Assert.IsNotNull(_batchTool.Description);
            Assert.IsTrue(_batchTool.Description.Contains("batch"), "Description should mention batch");
            Assert.That(_batchTool.Description, Does.Contain("Undo-tracked in-memory state"));
            Assert.That(_batchTool.Description, Does.Contain("other editor activity"));
            Assert.That(_batchTool.Description, Does.Contain("disk-reversion guarantee"));
        }

        #endregion

        #region Validation Tests

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithEmptyOperations_ReturnsError()
        {
            // Arrange
            JObject parameters = new JObject
            {
                ["operations"] = new JArray()
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsNotNull(result["error"], "Should return error for empty operations");
            Assert.IsTrue(result["error"]["message"].ToString().Contains("operations"),
                "Error should mention operations");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithNullOperations_ReturnsError()
        {
            // Arrange
            JObject parameters = new JObject();

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsNotNull(result["error"], "Should return error for null operations");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithTooManyOperations_ReturnsError()
        {
            // Arrange
            JArray operations = new JArray();
            for (int i = 0; i < 101; i++)
            {
                operations.Add(new JObject
                {
                    ["tool"] = "get_scene_info",
                    ["params"] = new JObject()
                });
            }

            JObject parameters = new JObject
            {
                ["operations"] = operations
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsNotNull(result["error"], "Should return error for too many operations");
            Assert.IsTrue(result["error"]["message"].ToString().Contains("100"),
                "Error should mention limit");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithNestedBatchExecute_ReturnsError()
        {
            // Arrange
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "batch_execute",
                        ["params"] = new JObject
                        {
                            ["operations"] = new JArray()
                        }
                    }
                }
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert - Should fail because of nested batch_execute
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, "Should fail for nested batch");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithUnknownTool_ReturnsError()
        {
            // Arrange
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "nonexistent_tool_12345",
                        ["params"] = new JObject()
                    }
                },
                ["stopOnError"] = true
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, "Should fail for unknown tool");
            Assert.IsNotNull(result["results"], "Should have results array");
            JArray results = result["results"] as JArray;
            Assert.AreEqual(1, results.Count);
            Assert.IsFalse(results[0]["success"]?.ToObject<bool>() ?? true);
            Assert.IsTrue(results[0]["error"]?.ToString().Contains("Unknown tool"));
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithImageResult_FailsWithoutBase64Payload()
        {
            _testObject = new GameObject("BatchScreenshotCamera");
            _testObject.AddComponent<Camera>();
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "screenshot_camera",
                        ["id"] = "capture",
                        ["params"] = new JObject
                        {
                            ["cameraInstanceId"] = _testObject.GetInstanceID(),
                            ["width"] = 8,
                            ["height"] = 8
                        }
                    }
                }
            };
            var tcs = new TaskCompletionSource<JObject>();

            _batchTool.ExecuteAsync(parameters, tcs);
            while (!tcs.Task.IsCompleted)
                yield return null;

            JObject result = tcs.Task.Result;
            JObject operationResult = (JObject)((JArray)result["results"])[0];
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true);
            Assert.AreEqual(
                "IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH",
                operationResult["errorCode"]?.ToString());
            Assert.That(operationResult["error"]?.ToString(), Does.Contain("directly"));
            Assert.IsNull(operationResult["result"], "Image result payload must be stripped");
            Assert.That(result.ToString(), Does.Not.Contain("\"data\""));
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_ImageSideEffect_IsDisclosedBeforePayloadIsDiscarded()
        {
            string[] seamNames =
            {
                "_resolveGameViewType",
                "_resolveRenderViewMethod",
                "_invokeRenderView",
                "_hasExistingEditorWindow",
                "_getGameViewWindow"
            };
            var originals = new System.Collections.Generic.Dictionary<string, object>();
            foreach (string seamName in seamNames)
            {
                originals[seamName] = GetPrivateStaticField(
                    typeof(ScreenshotGameViewTool), seamName);
            }

            BatchTestEditorWindow window =
                ScriptableObject.CreateInstance<BatchTestEditorWindow>();
            var source = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32);
            source.Create();
            try
            {
                System.Reflection.MethodInfo dummyMethod = typeof(object).GetMethod(
                    nameof(ToString), System.Type.EmptyTypes);
                SetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_resolveGameViewType",
                    new System.Func<System.Type>(() => typeof(BatchTestEditorWindow)));
                SetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_resolveRenderViewMethod",
                    new System.Func<System.Type, System.Reflection.MethodInfo>(_ => dummyMethod));
                SetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_invokeRenderView",
                    new System.Func<System.Reflection.MethodInfo, EditorWindow, RenderTexture>(
                        (_, __) => source));
                SetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_hasExistingEditorWindow",
                    new System.Func<System.Type, bool>(_ => false));
                SetPrivateStaticField(
                    typeof(ScreenshotGameViewTool),
                    "_getGameViewWindow",
                    new System.Func<System.Type, bool, EditorWindow>((_, __) => window));

                var tcs = new TaskCompletionSource<JObject>();
                _batchTool.ExecuteAsync(new JObject
                {
                    ["operations"] = new JArray
                    {
                        new JObject
                        {
                            ["tool"] = "screenshot_game_view",
                            ["params"] = new JObject
                            {
                                ["width"] = 8,
                                ["height"] = 8
                            }
                        }
                    }
                }, tcs);
                while (!tcs.Task.IsCompleted)
                    yield return null;

                JObject operationResult =
                    (JObject)((JArray)tcs.Task.Result["results"])[0];
                Assert.That(
                    operationResult["error"]?.ToString(),
                    Does.Contain("gameViewWindowCreated=true"));
                Assert.IsNull(operationResult["result"]);
            }
            finally
            {
                foreach (System.Collections.Generic.KeyValuePair<string, object> original in originals)
                {
                    SetPrivateStaticField(
                        typeof(ScreenshotGameViewTool), original.Key, original.Value);
                }
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(source);
            }
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_ImageWithoutWindowCreation_OmitsSideEffectDisclosure()
        {
            _testObject = new GameObject("BatchScreenshotCameraWithoutWindowSideEffect");
            _testObject.AddComponent<Camera>();
            var tcs = new TaskCompletionSource<JObject>();

            _batchTool.ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "screenshot_camera",
                        ["params"] = new JObject
                        {
                            ["cameraInstanceId"] = _testObject.GetInstanceID(),
                            ["width"] = 8,
                            ["height"] = 8
                        }
                    }
                }
            }, tcs);
            while (!tcs.Task.IsCompleted)
                yield return null;

            JObject operationResult =
                (JObject)((JArray)tcs.Task.Result["results"])[0];
            Assert.AreEqual(
                "IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH",
                operationResult["errorCode"]?.ToString());
            Assert.That(
                operationResult["error"]?.ToString(),
                Does.Not.Contain("gameViewWindowCreated="));
        }

        #endregion

        #region Atomic Rollback Honesty Tests

        [Test]
        public void BatchExecuteTool_UnexpectedCoroutineException_EndsAssetCollectionInFinally()
        {
            GetServerTools().Add(MalformedResultToolName, new MalformedResultTool());
            var tcs = new TaskCompletionSource<JObject>();
            var parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = MalformedResultToolName,
                        ["params"] = new JObject()
                    }
                },
                ["atomic"] = true,
                ["stopOnError"] = true
            };
            System.Reflection.MethodInfo method = typeof(BatchExecuteTool).GetMethod(
                "ExecuteBatchCoroutine",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method);
            var coroutine = (IEnumerator)method.Invoke(
                _batchTool,
                new object[] { parameters, tcs });

            bool threw = false;
            try
            {
                while (coroutine.MoveNext())
                {
                }
            }
            catch (System.Exception)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "The malformed result must exercise the outer finally path.");
            Assert.IsFalse(IsAtomicAssetWriteTrackerCollecting());
        }

        [Test]
        public void AtomicAssetWriteTracker_ResetAllClearsActiveCollection()
        {
            System.Type trackerType = GetAtomicAssetWriteTrackerType();
            System.Reflection.MethodInfo begin = trackerType.GetMethod(
                "Begin",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            System.Reflection.MethodInfo resetAll = trackerType.GetMethod(
                "ResetAll",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(begin);
            Assert.IsNotNull(resetAll);

            begin.Invoke(null, null);
            Assert.IsTrue(IsAtomicAssetWriteTrackerCollecting());

            resetAll.Invoke(null, null);

            Assert.IsFalse(IsAtomicAssetWriteTrackerCollecting());
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_AtomicFailureWithoutAssetWrites_ReportsEmptyEvidence()
        {
            var tcs = new TaskCompletionSource<JObject>();
            _batchTool.ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "nonexistent_tool_atomic_no_write",
                        ["params"] = new JObject()
                    }
                },
                ["atomic"] = true,
                ["stopOnError"] = true
            }, tcs);

            while (!tcs.Task.IsCompleted)
                yield return null;

            JObject result = tcs.Task.Result;
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, result.ToString());
            Assert.AreEqual(0, ((JArray)result["unrevertedAssetWrites"]).Count);
            Assert.That(result["message"]?.ToString(), Does.Contain("Undo-tracked in-memory state"));
            Assert.That(
                result["message"]?.ToString(),
                Does.Contain("No asset save/postprocess callbacks were observed"));
            Assert.That(result["message"]?.ToString(), Does.Not.Contain("failed and rolled back"));
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_AtomicFailureReportsObservedDiskAssetWrites()
        {
            _testAssetFolder =
                $"Assets/McpUnityBatchAtomicWrite_{System.Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<JObject>();
            _batchTool.ExecuteAsync(new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "manage_asset",
                        ["params"] = new JObject
                        {
                            ["action"] = "create_folder",
                            ["assetPath"] = _testAssetFolder
                        }
                    },
                    new JObject
                    {
                        ["tool"] = "nonexistent_tool_after_atomic_write",
                        ["params"] = new JObject()
                    }
                },
                ["atomic"] = true,
                ["stopOnError"] = true
            }, tcs);

            while (!tcs.Task.IsCompleted)
                yield return null;

            JObject result = tcs.Task.Result;
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, result.ToString());
            var writes = ((JArray)result["unrevertedAssetWrites"])
                .Values<string>()
                .ToArray();
            CollectionAssert.Contains(writes, _testAssetFolder);
            CollectionAssert.AreEqual(
                writes
                    .Distinct(System.StringComparer.Ordinal)
                    .OrderBy(path => path, System.StringComparer.Ordinal)
                    .ToArray(),
                writes,
                "Asset write evidence must be deduplicated and sorted.");
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(_testAssetFolder),
                "Unity Undo must not be reported as reverting the persisted folder.");
            Assert.That(result["message"]?.ToString(), Does.Contain("were observed"));
            Assert.That(result["message"]?.ToString(), Does.Contain("other editor activity"));
            Assert.That(
                result["message"]?.ToString(),
                Does.Not.Contain("asset path(s) were written to disk"));
            Assert.That(result["message"]?.ToString(), Does.Contain("unrevertedAssetWrites"));
        }

        #endregion

        #region Successful Execution Tests

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithSingleOperation_Succeeds()
        {
            // Arrange - get_scene_info requires no parameters and always succeeds
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "op1"
                    }
                }
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, "Should succeed");
            Assert.IsNotNull(result["results"], "Should have results array");
            Assert.IsNotNull(result["summary"], "Should have summary");

            JObject summary = result["summary"] as JObject;
            Assert.AreEqual(1, summary["total"]?.ToObject<int>());
            Assert.AreEqual(1, summary["succeeded"]?.ToObject<int>());
            Assert.AreEqual(0, summary["failed"]?.ToObject<int>());
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_WithMultipleOperations_Succeeds()
        {
            // Arrange - Use operations that don't require external dependencies
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "op1"
                    },
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "op2"
                    }
                }
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, "Should succeed");

            JObject summary = result["summary"] as JObject;
            Assert.AreEqual(2, summary["total"]?.ToObject<int>());
            Assert.AreEqual(2, summary["succeeded"]?.ToObject<int>());
            Assert.AreEqual(0, summary["failed"]?.ToObject<int>());

            JArray results = result["results"] as JArray;
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("op1", results[0]["id"]?.ToString());
            Assert.AreEqual("op2", results[1]["id"]?.ToString());
        }

        #endregion

        #region StopOnError Tests

        [UnityTest]
        public IEnumerator BatchExecuteTool_StopOnErrorTrue_StopsAtFirstError()
        {
            // Arrange - First operation fails, second should not execute
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "nonexistent_tool",
                        ["params"] = new JObject(),
                        ["id"] = "fail1"
                    },
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "should_not_run"
                    }
                },
                ["stopOnError"] = true
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, "Should fail");

            JObject summary = result["summary"] as JObject;
            Assert.AreEqual(2, summary["total"]?.ToObject<int>());
            Assert.AreEqual(0, summary["succeeded"]?.ToObject<int>());
            Assert.AreEqual(1, summary["failed"]?.ToObject<int>());
            Assert.AreEqual(1, summary["executed"]?.ToObject<int>(), "Should only execute 1 operation");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_StopOnErrorFalse_ContinuesAfterError()
        {
            // Arrange - First operation fails, but should continue
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "nonexistent_tool",
                        ["params"] = new JObject(),
                        ["id"] = "fail1"
                    },
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "should_run"
                    }
                },
                ["stopOnError"] = false
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert - Should have partial success
            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, "Overall should fail");

            JObject summary = result["summary"] as JObject;
            Assert.AreEqual(2, summary["total"]?.ToObject<int>());
            Assert.AreEqual(1, summary["succeeded"]?.ToObject<int>(), "Second operation should succeed");
            Assert.AreEqual(1, summary["failed"]?.ToObject<int>());
            Assert.AreEqual(2, summary["executed"]?.ToObject<int>(), "Should execute both operations");
        }

        #endregion

        #region Response Format Tests

        [UnityTest]
        public IEnumerator BatchExecuteTool_ResponseContainsRequiredFields()
        {
            // Arrange
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject()
                    }
                }
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert - Check all required fields are present
            Assert.IsNotNull(result["success"], "Response should have 'success' field");
            Assert.IsNotNull(result["type"], "Response should have 'type' field");
            Assert.IsNotNull(result["message"], "Response should have 'message' field");
            Assert.IsNotNull(result["results"], "Response should have 'results' field");
            Assert.IsNotNull(result["summary"], "Response should have 'summary' field");

            JObject summary = result["summary"] as JObject;
            Assert.IsNotNull(summary["total"], "Summary should have 'total' field");
            Assert.IsNotNull(summary["succeeded"], "Summary should have 'succeeded' field");
            Assert.IsNotNull(summary["failed"], "Summary should have 'failed' field");
            Assert.IsNotNull(summary["executed"], "Summary should have 'executed' field");
        }

        [UnityTest]
        public IEnumerator BatchExecuteTool_OperationResultsHaveCorrectFormat()
        {
            // Arrange
            JObject parameters = new JObject
            {
                ["operations"] = new JArray
                {
                    new JObject
                    {
                        ["tool"] = "get_scene_info",
                        ["params"] = new JObject(),
                        ["id"] = "custom_id"
                    }
                }
            };

            var tcs = new TaskCompletionSource<JObject>();

            // Act
            _batchTool.ExecuteAsync(parameters, tcs);

            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            JObject result = tcs.Task.Result;

            // Assert
            JArray results = result["results"] as JArray;
            Assert.AreEqual(1, results.Count);

            JObject opResult = results[0] as JObject;
            Assert.IsNotNull(opResult["index"], "Operation result should have 'index' field");
            Assert.IsNotNull(opResult["id"], "Operation result should have 'id' field");
            Assert.IsNotNull(opResult["success"], "Operation result should have 'success' field");
            Assert.AreEqual("custom_id", opResult["id"]?.ToString());
            Assert.AreEqual(0, opResult["index"]?.ToObject<int>());
        }

        #endregion

        private static object GetPrivateStaticField(System.Type ownerType, string name)
        {
            System.Reflection.FieldInfo field = ownerType.GetField(
                name,
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (field == null)
                throw new System.MissingFieldException(ownerType.FullName, name);
            return field.GetValue(null);
        }

        private static void SetPrivateStaticField(
            System.Type ownerType,
            string name,
            object value)
        {
            System.Reflection.FieldInfo field = ownerType.GetField(
                name,
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (field == null)
                Assert.Fail($"{ownerType.Name} private field '{name}' was not found");
            field.SetValue(null, value);
        }

        private static Dictionary<string, McpToolBase> GetServerTools()
        {
            System.Reflection.FieldInfo field = typeof(McpUnityServer).GetField(
                "_tools",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance);
            if (field == null)
                throw new System.MissingFieldException(typeof(McpUnityServer).FullName, "_tools");
            return (Dictionary<string, McpToolBase>)field.GetValue(McpUnityServer.Instance);
        }

        private static System.Type GetAtomicAssetWriteTrackerType()
        {
            return typeof(BatchExecuteTool).Assembly.GetType(
                "McpUnity.Services.AtomicBatchAssetWriteTracker",
                true);
        }

        private static bool IsAtomicAssetWriteTrackerCollecting()
        {
            return (bool)GetPrivateStaticField(
                GetAtomicAssetWriteTrackerType(),
                "_isCollecting");
        }

        private static void ResetAtomicAssetWriteTracker()
        {
            System.Type trackerType = GetAtomicAssetWriteTrackerType();
            System.Reflection.MethodInfo method = trackerType.GetMethod(
                "ResetAll",
                System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Static);
            if (method == null)
                throw new System.MissingMethodException(trackerType.FullName, "ResetAll");
            method.Invoke(null, null);
        }
    }
}
