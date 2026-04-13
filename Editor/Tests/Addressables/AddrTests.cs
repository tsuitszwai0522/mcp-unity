using System.Collections.Generic;
using System.Linq;
using McpUnity.Tools.Addressables;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace McpUnity.Tests.Addressables
{
    /// <summary>
    /// Minimal ScriptableObject type used as a test-owned dummy asset for
    /// Addressables tool tests. Lives in the test assembly only so it never
    /// leaks into runtime builds.
    /// </summary>
    internal class AddrTestDummySO : ScriptableObject
    {
    }

    /// <summary>
    /// Integration + unit tests for the Unity Addressables MCP tools.
    /// Each test drives the tool's <c>Execute(JObject)</c> directly and asserts on the JObject response,
    /// matching the contract used by the Node-side wrapper.
    /// </summary>
    [TestFixture]
    public class AddrTests
    {
        internal const string TestPrefix = "McpAddrTest_";
        internal const string TestGroupName = TestPrefix + "Group";
        internal const string TestGroupName2 = TestPrefix + "Group2";
        internal const string TestLabel = TestPrefix + "label";
        internal const string TestLabel2 = TestPrefix + "label2";

        // Dummy assets are created by the fixture inside the consumer project
        // so tests are self-contained — no dependency on any particular asset
        // living in the Unity project that happens to be running the tests.
        internal const string TestDir = "Assets/Tests/AddressablesTests";
        internal const string Asset1Path = TestDir + "/DummyA.asset";
        internal const string Asset2Path = TestDir + "/DummyB.asset";
        internal const string Asset3Path = TestDir + "/DummyC.asset";

        private static readonly string[] TestAssetPaths = new[]
        {
            Asset1Path,
            Asset2Path,
            Asset3Path
        };

        // Track what the fixture mutated so teardown can restore without
        // touching the consumer project's own Addressables state.
        private string _originalDefaultGroup;

        // ------------------------------------------------------------------------
        // Fixture lifecycle
        // ------------------------------------------------------------------------

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            EnsureTestDirectory();
            CreateDummyAssets();

            // Make sure Addressables is usable. If the consumer project has never
            // initialised Addressables, call the tool itself so we exercise the
            // real bootstrap path. We intentionally do NOT remember "we created
            // this" — the teardown never deletes settings because ripping out
            // AddressableAssetSettings has too much blast radius on user projects.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                new AddrInitSettingsTool().Execute(new JObject());
                settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
                Assert.IsNotNull(settings, "Fixture bootstrap failed: Addressables still uninitialized after addr_init_settings");
            }

            _originalDefaultGroup = settings.DefaultGroup?.Name;

            // Scrub any leftover artefacts from a previous failed run so tests
            // start with a clean slate.
            CleanupTestArtifacts(settings);
        }

        [TearDown]
        public void TearDown()
        {
            // Per-test cleanup. Restore default group FIRST (before deleting any
            // test group that might currently be acting as default), then scrub
            // all McpAddrTest_* artefacts so the next test starts clean.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                return;
            }

            RestoreDefaultGroup(settings);
            CleanupTestArtifacts(settings);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                return;
            }

            RestoreDefaultGroup(settings);
            CleanupTestArtifacts(settings);

            DeleteDummyAssets();
            DeleteTestDirectory();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------------
        // Fixture helpers
        // ------------------------------------------------------------------------

        private static void EnsureTestDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
            {
                AssetDatabase.CreateFolder("Assets", "Tests");
            }
            if (!AssetDatabase.IsValidFolder(TestDir))
            {
                AssetDatabase.CreateFolder("Assets/Tests", "AddressablesTests");
            }
        }

        private static void DeleteTestDirectory()
        {
            if (AssetDatabase.IsValidFolder(TestDir))
            {
                AssetDatabase.DeleteAsset(TestDir);
            }
        }

        private static void CreateDummyAssets()
        {
            foreach (var path in TestAssetPaths)
            {
                // If a previous failed run left the asset behind, reuse it.
                if (AssetDatabase.LoadAssetAtPath<AddrTestDummySO>(path) != null) continue;

                var so = ScriptableObject.CreateInstance<AddrTestDummySO>();
                AssetDatabase.CreateAsset(so, path);
            }
            AssetDatabase.SaveAssets();
        }

        private static void DeleteDummyAssets()
        {
            foreach (var path in TestAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<AddrTestDummySO>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        private void RestoreDefaultGroup(AddressableAssetSettings settings)
        {
            if (string.IsNullOrEmpty(_originalDefaultGroup)) return;

            var original = settings.FindGroup(_originalDefaultGroup);
            if (original != null && settings.DefaultGroup != original)
            {
                settings.DefaultGroup = original;
                EditorUtility.SetDirty(settings);
            }
        }

        private static void CleanupTestArtifacts(AddressableAssetSettings settings)
        {
            // Remove any addressable entries we may have created on the test sprites,
            // regardless of which group they ended up in (a test might add them to
            // the default group instead of a test group).
            foreach (var path in TestAssetPaths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                if (settings.FindAssetEntry(guid) != null)
                {
                    settings.RemoveAssetEntry(guid, false);
                }
            }

            // Remove test groups (entries fall out automatically).
            var testGroups = settings.groups
                .Where(g => g != null && g.Name != null && g.Name.StartsWith(TestPrefix))
                .ToList();
            foreach (var group in testGroups)
            {
                settings.RemoveGroup(group);
            }

            // Remove test labels.
            var testLabels = settings.GetLabels()
                .Where(l => l != null && l.StartsWith(TestPrefix))
                .ToList();
            foreach (var label in testLabels)
            {
                settings.RemoveLabel(label, false);
            }

            if (testGroups.Count > 0 || testLabels.Count > 0)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------------
        // Test-local helpers for creating entries (used by B15/B16/C8/C9/D/E tests)
        // ------------------------------------------------------------------------

        private static JObject AddSingleEntry(string groupName, string assetPath, string address = null, string label = null)
        {
            var asset = new JObject { ["asset_path"] = assetPath };
            if (!string.IsNullOrEmpty(address)) asset["address"] = address;
            if (!string.IsNullOrEmpty(label)) asset["labels"] = new JArray(label);

            return new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = groupName,
                ["assets"] = new JArray(asset)
            });
        }

        private static JObject AddThreeEntries(string groupName)
        {
            return new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = groupName,
                ["assets"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path },
                    new JObject { ["asset_path"] = Asset2Path },
                    new JObject { ["asset_path"] = Asset3Path }
                )
            });
        }

        private static AddressableAssetEntry FindEntry(string assetPath)
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? null : settings.FindAssetEntry(guid);
        }

        private static void CreateTestGroup(string name)
        {
            new AddrCreateGroupTool().Execute(new JObject { ["name"] = name });
        }

        private static void CreateTestLabel(string label)
        {
            new AddrCreateLabelTool().Execute(new JObject { ["label"] = label });
        }

        // ------------------------------------------------------------------------
        // Assert helpers
        // ------------------------------------------------------------------------
        //
        // Note: McpUnitySocketHandler.CreateErrorResponse returns { error: { type, message } }
        // with NO success field. Success responses return { success: true, ... }.

        internal static void AssertSuccess(JObject result)
        {
            Assert.IsNotNull(result, "Result is null");
            Assert.IsNull(result["error"], $"Unexpected error: {result["error"]}");
            Assert.IsTrue(result.Value<bool>("success"), $"success != true. Result: {result}");
        }

        internal static void AssertError(JObject result, string expectedErrorType = null)
        {
            Assert.IsNotNull(result, "Result is null");
            Assert.IsNotNull(result["error"], $"Expected error, got: {result}");
            if (expectedErrorType != null)
            {
                Assert.AreEqual(
                    expectedErrorType,
                    result["error"]?["type"]?.ToString(),
                    $"Error type mismatch. Full result: {result}");
            }
        }

        // ========================================================================
        // A. Settings tests
        // ========================================================================

        [Test]
        public void A0_Tools_WhenNotInitialized_ReturnNotInitializedError()
        {
            // Simulate "Addressables is not set up" by swapping AddrHelper's
            // SettingsProvider to return null. This avoids the blast radius of
            // actually ripping out AddressableAssetSettingsDefaultObject.Settings
            // on the consumer project.
            var originalProvider = AddrHelper.SettingsProvider;
            AddrHelper.SettingsProvider = () => null;
            try
            {
                // Pick a representative cross-section of tools that flow through
                // AddrHelper.TryGetSettings so we lock the error contract for the
                // whole family, not just one tool.
                AssertError(new AddrListGroupsTool().Execute(new JObject()), "not_initialized");
                AssertError(new AddrListLabelsTool().Execute(new JObject()), "not_initialized");
                AssertError(new AddrCreateLabelTool().Execute(new JObject { ["label"] = "any" }),
                    "not_initialized");
                AssertError(new AddrAddEntriesTool().Execute(new JObject
                {
                    ["group"] = "any",
                    ["assets"] = new JArray(new JObject { ["asset_path"] = Asset1Path })
                }), "not_initialized");
                AssertError(new AddrFindAssetTool().Execute(new JObject
                {
                    ["asset_path"] = Asset1Path
                }), "not_initialized");
            }
            finally
            {
                AddrHelper.SettingsProvider = originalProvider;
            }
        }

        [Test]
        public void A1_GetSettings_WhenInitialized_ReturnsExpectedFields()
        {
            var result = new AddrGetSettingsTool().Execute(new JObject());
            AssertSuccess(result);

            Assert.IsTrue(result.Value<bool>("initialized"), "initialized should be true");
            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("defaultGroup")),
                "defaultGroup should be non-empty when initialized");
            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("activeProfile")),
                "activeProfile should be non-empty when initialized");

            var profileVariables = result["profileVariables"] as JObject;
            Assert.IsNotNull(profileVariables, "profileVariables should be a JObject");
            Assert.Greater(profileVariables.Count, 0,
                "profileVariables should contain at least one entry (e.g. BuildPath / LoadPath)");

            Assert.GreaterOrEqual(result.Value<int>("groupCount"), 1,
                "groupCount should be at least 1 (default group exists)");
            Assert.GreaterOrEqual(result.Value<int>("entryCount"), 0,
                "entryCount should be non-negative");

            var labels = result["labels"] as JArray;
            Assert.IsNotNull(labels, "labels should be a JArray");

            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("version")),
                "version should be populated from the Addressables package info");
        }

        [Test]
        public void A2_InitSettings_WhenAlreadyInitialized_IsIdempotent()
        {
            // Fixture guarantees Addressables is initialized before this runs.
            var result = new AddrInitSettingsTool().Execute(new JObject());
            AssertSuccess(result);

            Assert.IsFalse(result.Value<bool>("created"),
                "created should be false when Addressables is already initialized");
            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("settingsPath")),
                "settingsPath should be populated even on idempotent path");
            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("defaultGroup")),
                "defaultGroup should be populated even on idempotent path");
        }

        [Test]
        public void A3_InitSettings_WithCustomFolder_WhenAlreadyInit_IgnoresFolder()
        {
            // When already initialized, the folder parameter must be ignored — we
            // must NOT create a second settings asset at a different location.
            var result = new AddrInitSettingsTool().Execute(new JObject
            {
                ["folder"] = "Assets/NonexistentAddrFolder"
            });
            AssertSuccess(result);

            Assert.IsFalse(result.Value<bool>("created"),
                "created should stay false regardless of folder param when already initialized");
            Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/NonexistentAddrFolder"),
                "Idempotent path must not create the folder passed in the params");
        }

        [Test]
        public void A3b_InitSettings_FolderOutsideAssets_ReturnsValidationError()
        {
            // Agents can pass arbitrary strings — we must refuse anything that
            // isn't under Assets/ before any IO happens.
            foreach (var bad in new[] { "/tmp/evil", "C:/Windows/System32", "Packages/com.unity.addressables" })
            {
                var result = new AddrInitSettingsTool().Execute(new JObject
                {
                    ["folder"] = bad
                });
                AssertError(result, "validation_error");
            }
        }

        [Test]
        public void A3c_InitSettings_FolderWithParentTraversal_ReturnsValidationError()
        {
            // Reject `..` in any position — even "Assets/../evil" would escape.
            foreach (var bad in new[] { "Assets/../evil", "Assets/foo/../../bar" })
            {
                var result = new AddrInitSettingsTool().Execute(new JObject
                {
                    ["folder"] = bad
                });
                AssertError(result, "validation_error");
            }

            Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/evil"),
                "Rejected folder must not have been created");
            Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/bar"),
                "Rejected folder must not have been created");
        }

        [Test]
        public void A4_GetSettings_LabelsArray_IsIterable()
        {
            // Smoke test: the labels field must be an array we can iterate without
            // errors, even if the project currently has zero labels.
            var result = new AddrGetSettingsTool().Execute(new JObject());
            AssertSuccess(result);

            var labels = result["labels"] as JArray;
            Assert.IsNotNull(labels);

            // Just iterate — any cast failure would blow up here.
            var labelList = new List<string>();
            foreach (var token in labels)
            {
                labelList.Add(token.ToString());
            }
            // No assertion on content — the project may or may not have labels.
        }

        // ========================================================================
        // B. Group tests
        // ========================================================================

        [Test]
        public void B1_CreateGroup_CreatesWithDefaultSchemas()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("created"));
            Assert.AreEqual(TestGroupName, result.Value<string>("name"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var group = settings.FindGroup(TestGroupName);
            Assert.IsNotNull(group, "Group was not registered with settings");
            Assert.IsNotNull(group.GetSchema<BundledAssetGroupSchema>(),
                "BundledAssetGroupSchema missing");
            Assert.IsNotNull(group.GetSchema<ContentUpdateGroupSchema>(),
                "ContentUpdateGroupSchema missing");
        }

        [Test]
        public void B2_CreateGroup_PackedModePackSeparately_AppliesToSchema()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["packed_mode"] = "PackSeparately"
            });
            AssertSuccess(result);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var schema = settings.FindGroup(TestGroupName).GetSchema<BundledAssetGroupSchema>();
            Assert.AreEqual(BundledAssetGroupSchema.BundlePackingMode.PackSeparately, schema.BundleMode);
        }

        [Test]
        public void B3_CreateGroup_PackedModePackTogetherByLabel_AppliesToSchema()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["packed_mode"] = "PackTogetherByLabel"
            });
            AssertSuccess(result);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var schema = settings.FindGroup(TestGroupName).GetSchema<BundledAssetGroupSchema>();
            Assert.AreEqual(BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel, schema.BundleMode);
        }

        [Test]
        public void B4_CreateGroup_IncludeInBuildFalse_AppliesToSchema()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["include_in_build"] = false
            });
            AssertSuccess(result);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var schema = settings.FindGroup(TestGroupName).GetSchema<BundledAssetGroupSchema>();
            Assert.IsFalse(schema.IncludeInBuild);
        }

        [Test]
        public void B5_CreateGroup_SetAsDefaultTrue_ChangesDefault()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["set_as_default"] = true
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("isDefault"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.AreEqual(TestGroupName, settings.DefaultGroup?.Name);
            // [TearDown] restores _originalDefaultGroup.
        }

        [Test]
        public void B6_CreateGroup_Duplicate_ReturnsDuplicateError()
        {
            var tool = new AddrCreateGroupTool();
            AssertSuccess(tool.Execute(new JObject { ["name"] = TestGroupName }));

            var second = tool.Execute(new JObject { ["name"] = TestGroupName });
            AssertError(second, "duplicate");
        }

        [Test]
        public void B7_CreateGroup_InvalidPackedMode_ReturnsValidationError()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["packed_mode"] = "NotARealMode"
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void B8_CreateGroup_EmptyName_ReturnsValidationError()
        {
            var result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = ""
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void B9_ListGroups_IncludesCreatedGroupWithCorrectSchema()
        {
            new AddrCreateGroupTool().Execute(new JObject { ["name"] = TestGroupName });

            var result = new AddrListGroupsTool().Execute(new JObject());
            AssertSuccess(result);

            var groups = result["groups"] as JArray;
            Assert.IsNotNull(groups);

            var entry = groups.OfType<JObject>()
                .FirstOrDefault(g => g.Value<string>("name") == TestGroupName);
            Assert.IsNotNull(entry, $"Test group not in list_groups output");

            var schemas = entry["schemas"] as JArray;
            Assert.IsNotNull(schemas);
            Assert.IsTrue(schemas.Any(s => s.ToString() == "BundledAssetGroupSchema"),
                $"BundledAssetGroupSchema missing from schemas: {schemas}");
            Assert.IsTrue(schemas.Any(s => s.ToString() == "ContentUpdateGroupSchema"),
                $"ContentUpdateGroupSchema missing from schemas: {schemas}");
        }

        [Test]
        public void B10_ListGroups_MarksDefaultGroup()
        {
            var result = new AddrListGroupsTool().Execute(new JObject());
            AssertSuccess(result);

            var groups = result["groups"] as JArray;
            var defaultEntries = groups.OfType<JObject>()
                .Where(g => g.Value<bool>("isDefault"))
                .ToList();

            Assert.AreEqual(1, defaultEntries.Count,
                $"Exactly one group should be marked default, got {defaultEntries.Count}");
            Assert.AreEqual(_originalDefaultGroup, defaultEntries[0].Value<string>("name"));
        }

        [Test]
        public void B11_SetDefaultGroup_ChangesDefaultAndReportsPrevious()
        {
            new AddrCreateGroupTool().Execute(new JObject { ["name"] = TestGroupName });

            var result = new AddrSetDefaultGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName
            });
            AssertSuccess(result);

            Assert.AreEqual(TestGroupName, result.Value<string>("defaultGroup"));
            Assert.AreEqual(_originalDefaultGroup, result.Value<string>("previousDefault"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.AreEqual(TestGroupName, settings.DefaultGroup?.Name);
            // [TearDown] restores.
        }

        [Test]
        public void B12_SetDefaultGroup_SameGroup_ReturnsNoOpMessage()
        {
            var result = new AddrSetDefaultGroupTool().Execute(new JObject
            {
                ["name"] = _originalDefaultGroup
            });
            AssertSuccess(result);

            string message = (result.Value<string>("message") ?? string.Empty).ToLower();
            Assert.IsTrue(message.Contains("already"),
                $"Expected 'already' in no-op message, got: {message}");
        }

        [Test]
        public void B13_SetDefaultGroup_NonExistent_ReturnsNotFound()
        {
            var result = new AddrSetDefaultGroupTool().Execute(new JObject
            {
                ["name"] = TestPrefix + "DoesNotExist"
            });
            AssertError(result, "not_found");
        }

        [Test]
        public void B14_RemoveGroup_Empty_Succeeds()
        {
            new AddrCreateGroupTool().Execute(new JObject { ["name"] = TestGroupName });

            var result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("deleted"));
            Assert.AreEqual(0, result.Value<int>("removedEntryCount"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.IsNull(settings.FindGroup(TestGroupName),
                "Group should be removed from settings");
        }

        [Test]
        public void B17_RemoveGroup_DefaultGroup_ReturnsValidationError()
        {
            var result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = _originalDefaultGroup
            });
            AssertError(result, "validation_error");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.IsNotNull(settings.FindGroup(_originalDefaultGroup),
                "Default group must survive the rejection");
        }

        [Test]
        public void B18_RemoveGroup_NonExistent_ReturnsNotFound()
        {
            var result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestPrefix + "DoesNotExist"
            });
            AssertError(result, "not_found");
        }

        // ========================================================================
        // C. Label tests
        // ========================================================================

        [Test]
        public void C1_CreateLabel_New_ReturnsCreatedTrue()
        {
            var result = new AddrCreateLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("created"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.Contains(settings.GetLabels(), TestLabel);
        }

        [Test]
        public void C2_CreateLabel_Existing_IsIdempotent()
        {
            var tool = new AddrCreateLabelTool();
            AssertSuccess(tool.Execute(new JObject { ["label"] = TestLabel }));

            var second = tool.Execute(new JObject { ["label"] = TestLabel });
            AssertSuccess(second);
            Assert.IsFalse(second.Value<bool>("created"),
                "Second create should be idempotent (created:false)");
        }

        [Test]
        public void C3_CreateLabel_WithSpace_ReturnsValidationError()
        {
            var result = new AddrCreateLabelTool().Execute(new JObject
            {
                ["label"] = "bad label"
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void C4_CreateLabel_WithBracket_ReturnsValidationError()
        {
            var result = new AddrCreateLabelTool().Execute(new JObject
            {
                ["label"] = "[bad]"
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void C5_CreateLabel_Empty_ReturnsValidationError()
        {
            var result = new AddrCreateLabelTool().Execute(new JObject
            {
                ["label"] = ""
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void C6_ListLabels_IncludesCreatedLabel()
        {
            new AddrCreateLabelTool().Execute(new JObject { ["label"] = TestLabel });

            var result = new AddrListLabelsTool().Execute(new JObject());
            AssertSuccess(result);

            var labels = result["labels"] as JArray;
            Assert.IsNotNull(labels);
            Assert.IsTrue(labels.Any(l => l.ToString() == TestLabel),
                $"Test label not in list_labels output");
        }

        [Test]
        public void C7_RemoveLabel_Unused_Succeeds()
        {
            new AddrCreateLabelTool().Execute(new JObject { ["label"] = TestLabel });

            var result = new AddrRemoveLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("deleted"));
            Assert.AreEqual(0, result.Value<int>("affectedEntries"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.DoesNotContain(settings.GetLabels(), TestLabel);
        }

        [Test]
        public void C10_RemoveLabel_NonExistent_ReturnsNotFound()
        {
            var result = new AddrRemoveLabelTool().Execute(new JObject
            {
                ["label"] = TestPrefix + "DoesNotExist"
            });
            AssertError(result, "not_found");
        }

        // ========================================================================
        // Deferred from Stage 2 (need entries)
        // ========================================================================

        [Test]
        public void B15_RemoveGroup_NonEmpty_WithoutForce_ReturnsInUse()
        {
            CreateTestGroup(TestGroupName);
            AssertSuccess(AddSingleEntry(TestGroupName, Asset1Path));

            var result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName
            });
            AssertError(result, "in_use");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.IsNotNull(settings.FindGroup(TestGroupName),
                "Group should survive the rejected removal");
        }

        [Test]
        public void B16_RemoveGroup_NonEmpty_WithForce_RemovesEntries()
        {
            CreateTestGroup(TestGroupName);
            AssertSuccess(AddSingleEntry(TestGroupName, Asset1Path));

            var result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["force"] = true
            });
            AssertSuccess(result);
            Assert.AreEqual(1, result.Value<int>("removedEntryCount"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            Assert.IsNull(settings.FindGroup(TestGroupName));
            Assert.IsNull(FindEntry(Asset1Path), "Entry should be gone after force-remove");
        }

        [Test]
        public void C8_RemoveLabel_InUse_WithoutForce_ReturnsInUse()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            AssertSuccess(AddSingleEntry(TestGroupName, Asset1Path, label: TestLabel));

            var result = new AddrRemoveLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel
            });
            AssertError(result, "in_use");

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.Contains(settings.GetLabels(), TestLabel,
                "Label should survive the rejected removal");
        }

        [Test]
        public void C9_RemoveLabel_InUse_WithForce_StripsFromEntriesAndDeletes()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            AssertSuccess(AddSingleEntry(TestGroupName, Asset1Path, label: TestLabel));

            var result = new AddrRemoveLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel,
                ["force"] = true
            });
            AssertSuccess(result);
            Assert.AreEqual(1, result.Value<int>("affectedEntries"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.DoesNotContain(settings.GetLabels(), TestLabel);

            var entry = FindEntry(Asset1Path);
            Assert.IsNotNull(entry);
            Assert.IsFalse(entry.labels.Contains(TestLabel),
                "Label should be stripped from entry");
        }

        // ========================================================================
        // D. Entry tests
        // ========================================================================

        [Test]
        public void D1_AddEntries_SingleAsset_NoAddress_UsesAssetPathAsDefault()
        {
            CreateTestGroup(TestGroupName);
            var result = AddSingleEntry(TestGroupName, Asset1Path);
            AssertSuccess(result);

            Assert.AreEqual(1, result.Value<int>("added"));
            Assert.AreEqual(0, result.Value<int>("skipped"));

            var entry = FindEntry(Asset1Path);
            Assert.IsNotNull(entry);
            Assert.AreEqual(Asset1Path, entry.address,
                "Default address should equal asset path when no override supplied");
        }

        [Test]
        public void D2_AddEntries_WithCustomAddress_UsesProvidedAddress()
        {
            CreateTestGroup(TestGroupName);
            var result = AddSingleEntry(TestGroupName, Asset1Path, address: "custom/ui/main");
            AssertSuccess(result);

            var entry = FindEntry(Asset1Path);
            Assert.AreEqual("custom/ui/main", entry.address);
        }

        [Test]
        public void D3_AddEntries_WithNewLabel_AutoCreatesLabelWithWarning()
        {
            CreateTestGroup(TestGroupName);
            var newLabel = TestPrefix + "auto";
            var result = AddSingleEntry(TestGroupName, Asset1Path, label: newLabel);
            AssertSuccess(result);

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.Contains(settings.GetLabels(), newLabel);

            var warnings = result["warnings"] as JArray;
            Assert.IsNotNull(warnings, "Auto-created labels should produce warnings");
            Assert.IsTrue(warnings.Any(w => w.ToString().Contains("created automatically")),
                $"Expected 'created automatically' warning, got: {warnings}");
        }

        [Test]
        public void D4_AddEntries_WithExistingLabel_AppliesWithoutAutoCreateWarning()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);

            var result = AddSingleEntry(TestGroupName, Asset1Path, label: TestLabel);
            AssertSuccess(result);

            var warnings = result["warnings"] as JArray;
            if (warnings != null)
            {
                Assert.IsFalse(warnings.Any(w => w.ToString().Contains("created automatically")),
                    "Pre-existing label should not trigger 'created automatically' warning");
            }

            var entry = FindEntry(Asset1Path);
            Assert.IsTrue(entry.labels.Contains(TestLabel));
        }

        [Test]
        public void D5_AddEntries_InvalidAssetPath_StrictDefault_ReturnsNotFound()
        {
            // Default contract: a missing asset_path aborts the whole batch with
            // not_found so callers can't silently drop content.
            CreateTestGroup(TestGroupName);
            var result = new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName,
                ["assets"] = new JArray(
                    new JObject { ["asset_path"] = "Assets/Does/Not/Exist.png" }
                )
            });
            AssertError(result, "not_found");
        }

        [Test]
        public void D5b_AddEntries_InvalidAssetPath_LenientMode_SkippedWithWarning()
        {
            // Opt-in lenient mode: missing assets become skip+warning and are
            // surfaced in a missingAssets array for the caller to inspect.
            CreateTestGroup(TestGroupName);
            var result = new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName,
                ["fail_on_missing_asset"] = false,
                ["assets"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path },
                    new JObject { ["asset_path"] = "Assets/Does/Not/Exist.png" }
                )
            });
            AssertSuccess(result);

            Assert.AreEqual(1, result.Value<int>("added"));
            Assert.AreEqual(1, result.Value<int>("skipped"));

            var warnings = result["warnings"] as JArray;
            Assert.IsNotNull(warnings);
            Assert.IsTrue(warnings.Any(w => w.ToString().Contains("not found")),
                $"Expected 'not found' warning, got: {warnings}");

            var missing = result["missingAssets"] as JArray;
            Assert.IsNotNull(missing, "missingAssets array should be present in lenient mode");
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual("Assets/Does/Not/Exist.png", missing[0].ToString());
        }

        [Test]
        public void D6_AddEntries_EmptyArray_ReturnsValidationError()
        {
            CreateTestGroup(TestGroupName);
            var result = new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName,
                ["assets"] = new JArray()
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void D7_AddEntries_NonExistentGroup_ReturnsNotFound()
        {
            var result = AddSingleEntry(TestPrefix + "NoSuchGroup", Asset1Path);
            AssertError(result, "not_found");
        }

        [Test]
        public void D8_AddEntries_BatchThreeAssets_AllRegistered()
        {
            CreateTestGroup(TestGroupName);
            var result = AddThreeEntries(TestGroupName);
            AssertSuccess(result);

            Assert.AreEqual(3, result.Value<int>("added"));
            Assert.AreEqual(0, result.Value<int>("skipped"));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            var group = settings.FindGroup(TestGroupName);
            Assert.AreEqual(3, group.entries.Count);
        }

        [Test]
        public void D9_ListEntries_NoFilter_ReturnsAllTestEntries()
        {
            CreateTestGroup(TestGroupName);
            AddThreeEntries(TestGroupName);

            var result = new AddrListEntriesTool().Execute(new JObject());
            AssertSuccess(result);

            var entries = result["entries"] as JArray;
            Assert.IsNotNull(entries);

            int matched = entries.OfType<JObject>()
                .Count(e => TestAssetPaths.Contains(e.Value<string>("assetPath")));
            Assert.AreEqual(3, matched, "All 3 test entries should appear in unfiltered list");
        }

        [Test]
        public void D10_ListEntries_GroupFilter_OnlyReturnsThatGroup()
        {
            CreateTestGroup(TestGroupName);
            AddThreeEntries(TestGroupName);

            var result = new AddrListEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName
            });
            AssertSuccess(result);

            var entries = result["entries"] as JArray;
            foreach (var entry in entries.OfType<JObject>())
            {
                Assert.AreEqual(TestGroupName, entry.Value<string>("group"));
            }
            Assert.AreEqual(3, entries.Count);
        }

        [Test]
        public void D11_ListEntries_LabelFilter_OnlyReturnsEntriesWithLabel()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            AddSingleEntry(TestGroupName, Asset1Path, label: TestLabel);
            AddSingleEntry(TestGroupName, Asset2Path); // no label

            var result = new AddrListEntriesTool().Execute(new JObject
            {
                ["label_filter"] = TestLabel
            });
            AssertSuccess(result);

            var entries = result["entries"] as JArray;
            var matched = entries.OfType<JObject>()
                .Where(e => TestAssetPaths.Contains(e.Value<string>("assetPath")))
                .ToList();
            Assert.AreEqual(1, matched.Count);
            Assert.AreEqual(Asset1Path, matched[0].Value<string>("assetPath"));
        }

        [Test]
        public void D12_ListEntries_AddressPatternGlob_MatchesStar()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path, address: "ui/main");
            AddSingleEntry(TestGroupName, Asset2Path, address: "ui/side");
            AddSingleEntry(TestGroupName, Asset3Path, address: "audio/bgm");

            var result = new AddrListEntriesTool().Execute(new JObject
            {
                ["address_pattern"] = "ui/*"
            });
            AssertSuccess(result);

            var entries = result["entries"] as JArray;
            var matched = entries.OfType<JObject>()
                .Where(e => TestAssetPaths.Contains(e.Value<string>("assetPath")))
                .ToList();
            Assert.AreEqual(2, matched.Count, "ui/* should match ui/main and ui/side but not audio/bgm");
        }

        [Test]
        public void D13_ListEntries_AssetPathPrefix_MatchesPrefix()
        {
            CreateTestGroup(TestGroupName);
            AddThreeEntries(TestGroupName);

            var result = new AddrListEntriesTool().Execute(new JObject
            {
                ["asset_path_prefix"] = TestDir + "/"
            });
            AssertSuccess(result);

            var entries = result["entries"] as JArray;
            var matched = entries.OfType<JObject>()
                .Where(e => TestAssetPaths.Contains(e.Value<string>("assetPath")))
                .ToList();
            Assert.AreEqual(3, matched.Count);
        }

        [Test]
        public void D14_ListEntries_Limit1_SetsTruncatedTrue()
        {
            CreateTestGroup(TestGroupName);
            AddThreeEntries(TestGroupName);

            var result = new AddrListEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName,
                ["limit"] = 1
            });
            AssertSuccess(result);

            Assert.AreEqual(1, (result["entries"] as JArray).Count);
            Assert.AreEqual(3, result.Value<int>("total"));
            Assert.IsTrue(result.Value<bool>("truncated"));
        }

        [Test]
        public void D15_SetEntry_ChangeAddress_UpdatesAddress()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path);

            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["new_address"] = "ui/updated"
            });
            AssertSuccess(result);

            Assert.AreEqual("ui/updated", FindEntry(Asset1Path).address);
        }

        [Test]
        public void D16_SetEntry_AddLabels_AppliesAllAndAutoCreatesMissing()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path);

            var newLabel = TestPrefix + "added";
            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["add_labels"] = new JArray(newLabel)
            });
            AssertSuccess(result);

            var entry = FindEntry(Asset1Path);
            Assert.IsTrue(entry.labels.Contains(newLabel));

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            CollectionAssert.Contains(settings.GetLabels(), newLabel);

            var warnings = result["warnings"] as JArray;
            Assert.IsNotNull(warnings);
            Assert.IsTrue(warnings.Any(w => w.ToString().Contains("created automatically")));
        }

        [Test]
        public void D17_SetEntry_RemoveLabels_StripsSpecified()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            CreateTestLabel(TestLabel2);
            AddSingleEntry(TestGroupName, Asset1Path, label: TestLabel);

            // Add a second label first via set_entry (so both exist).
            new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["add_labels"] = new JArray(TestLabel2)
            });

            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["remove_labels"] = new JArray(TestLabel)
            });
            AssertSuccess(result);

            var entry = FindEntry(Asset1Path);
            Assert.IsFalse(entry.labels.Contains(TestLabel), "TestLabel should be stripped");
            Assert.IsTrue(entry.labels.Contains(TestLabel2), "TestLabel2 should remain");
        }

        [Test]
        public void D18_SetEntry_PartialUpdateOnlyAddress_DoesNotTouchLabels()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            AddSingleEntry(TestGroupName, Asset1Path, address: "original", label: TestLabel);

            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["new_address"] = "updated"
            });
            AssertSuccess(result);

            var entry = FindEntry(Asset1Path);
            Assert.AreEqual("updated", entry.address);
            Assert.IsTrue(entry.labels.Contains(TestLabel),
                "Labels should remain untouched when only address changes");
        }

        [Test]
        public void D19_SetEntry_ByAssetPathNotGuid_Resolves()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path);

            // Only pass asset_path, no guid
            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["new_address"] = "via/path"
            });
            AssertSuccess(result);

            Assert.AreEqual("via/path", FindEntry(Asset1Path).address);
        }

        [Test]
        public void D20_SetEntry_NeitherGuidNorAssetPath_ReturnsValidationError()
        {
            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["new_address"] = "no/identifier"
            });
            AssertError(result, "validation_error");
        }

        [Test]
        public void D21_SetEntry_NonExistent_ReturnsNotFound()
        {
            var result = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = "Assets/Does/Not/Exist.png",
                ["new_address"] = "nothing"
            });
            AssertError(result, "not_found");
        }

        [Test]
        public void D22_MoveEntries_AcrossGroups_UpdatesParent()
        {
            CreateTestGroup(TestGroupName);
            CreateTestGroup(TestGroupName2);
            AddThreeEntries(TestGroupName);

            var result = new AddrMoveEntriesTool().Execute(new JObject
            {
                ["target_group"] = TestGroupName2,
                ["entries"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path },
                    new JObject { ["asset_path"] = Asset2Path }
                )
            });
            AssertSuccess(result);
            Assert.AreEqual(2, result.Value<int>("moved"));
            Assert.AreEqual(0, result.Value<int>("notFound"));

            Assert.AreEqual(TestGroupName2, FindEntry(Asset1Path).parentGroup.Name);
            Assert.AreEqual(TestGroupName2, FindEntry(Asset2Path).parentGroup.Name);
            Assert.AreEqual(TestGroupName, FindEntry(Asset3Path).parentGroup.Name);
        }

        [Test]
        public void D23_MoveEntries_NonExistentTarget_ReturnsNotFound()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path);

            var result = new AddrMoveEntriesTool().Execute(new JObject
            {
                ["target_group"] = TestPrefix + "NoSuchTarget",
                ["entries"] = new JArray(new JObject { ["asset_path"] = Asset1Path })
            });
            AssertError(result, "not_found");
        }

        [Test]
        public void D24_MoveEntries_EntryByAssetPath_Resolves()
        {
            CreateTestGroup(TestGroupName);
            CreateTestGroup(TestGroupName2);
            AddSingleEntry(TestGroupName, Asset1Path);

            var result = new AddrMoveEntriesTool().Execute(new JObject
            {
                ["target_group"] = TestGroupName2,
                ["entries"] = new JArray(new JObject { ["asset_path"] = Asset1Path })
            });
            AssertSuccess(result);
            Assert.AreEqual(1, result.Value<int>("moved"));
            Assert.AreEqual(TestGroupName2, FindEntry(Asset1Path).parentGroup.Name);
        }

        [Test]
        public void D25_RemoveEntries_BatchThree_RemovesAll()
        {
            CreateTestGroup(TestGroupName);
            AddThreeEntries(TestGroupName);

            var result = new AddrRemoveEntriesTool().Execute(new JObject
            {
                ["entries"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path },
                    new JObject { ["asset_path"] = Asset2Path },
                    new JObject { ["asset_path"] = Asset3Path }
                )
            });
            AssertSuccess(result);
            Assert.AreEqual(3, result.Value<int>("removed"));
            Assert.AreEqual(0, result.Value<int>("notFound"));

            Assert.IsNull(FindEntry(Asset1Path));
            Assert.IsNull(FindEntry(Asset2Path));
            Assert.IsNull(FindEntry(Asset3Path));
        }

        [Test]
        public void D26_RemoveEntries_MixedValidAndInvalid_ReportsBoth()
        {
            CreateTestGroup(TestGroupName);
            AddSingleEntry(TestGroupName, Asset1Path);

            var result = new AddrRemoveEntriesTool().Execute(new JObject
            {
                ["entries"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path },
                    new JObject { ["asset_path"] = "Assets/Does/Not/Exist.png" }
                )
            });
            AssertSuccess(result);
            Assert.AreEqual(1, result.Value<int>("removed"));
            Assert.AreEqual(1, result.Value<int>("notFound"));
        }

        // ========================================================================
        // E. Query tests
        // ========================================================================

        [Test]
        public void E1_FindAsset_Addressable_ReturnsFullEntryInfo()
        {
            CreateTestGroup(TestGroupName);
            CreateTestLabel(TestLabel);
            AddSingleEntry(TestGroupName, Asset1Path, address: "ui/found", label: TestLabel);

            var result = new AddrFindAssetTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path
            });
            AssertSuccess(result);

            Assert.IsTrue(result.Value<bool>("found"));
            var entry = result["entry"] as JObject;
            Assert.IsNotNull(entry);
            Assert.AreEqual(Asset1Path, entry.Value<string>("assetPath"));
            Assert.AreEqual("ui/found", entry.Value<string>("address"));
            Assert.AreEqual(TestGroupName, entry.Value<string>("group"));

            var labels = entry["labels"] as JArray;
            Assert.IsNotNull(labels);
            Assert.IsTrue(labels.Any(l => l.ToString() == TestLabel));
        }

        [Test]
        public void E2_FindAsset_ExistsButNotAddressable_ReturnsFoundFalse()
        {
            // Asset1 exists in project but the fixture's CleanupTestArtifacts
            // ensures it is NOT currently addressable at test start.
            var result = new AddrFindAssetTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path
            });
            AssertSuccess(result);

            Assert.IsFalse(result.Value<bool>("found"),
                "Non-addressable asset should return found:false");
            Assert.IsFalse(string.IsNullOrEmpty(result.Value<string>("guid")),
                "guid should still be returned for existing but non-addressable assets");
        }

        [Test]
        public void E3_FindAsset_NonExistentPath_ReturnsNotFound()
        {
            var result = new AddrFindAssetTool().Execute(new JObject
            {
                ["asset_path"] = "Assets/Does/Not/Exist.png"
            });
            AssertError(result, "not_found");
        }

        // ========================================================================
        // F. Golden path scenario (ordered integration test)
        // ========================================================================
        //
        // Single test that walks through a realistic agent workflow end-to-end,
        // calling each tool in the order an agent would. Catches cross-tool
        // bugs that per-tool unit tests can miss.

        [Test, Order(999)]
        public void F1_FullLifecycle_InitToCleanup_SucceedsAtEveryStep()
        {
            int step = 0;

            // ---- Step 1: init is idempotent ----
            step = 1;
            var initResult = new AddrInitSettingsTool().Execute(new JObject());
            AssertSuccess(initResult);
            Assert.IsFalse(initResult.Value<bool>("created"),
                $"step {step}: addr_init_settings should be idempotent");

            // ---- Step 2: get_settings shows project state ----
            step = 2;
            var settingsResult = new AddrGetSettingsTool().Execute(new JObject());
            AssertSuccess(settingsResult);
            Assert.IsTrue(settingsResult.Value<bool>("initialized"),
                $"step {step}: settings should be initialized");
            string defaultGroupBefore = settingsResult.Value<string>("defaultGroup");
            Assert.IsFalse(string.IsNullOrEmpty(defaultGroupBefore),
                $"step {step}: defaultGroup should be non-empty");

            // ---- Step 3: create primary group with non-default schema settings ----
            step = 3;
            var createGroupResult = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["packed_mode"] = "PackSeparately",
                ["include_in_build"] = false
            });
            AssertSuccess(createGroupResult);
            Assert.IsTrue(createGroupResult.Value<bool>("created"),
                $"step {step}: group should be created");

            // ---- Step 4: register a label ----
            step = 4;
            var createLabelResult = new AddrCreateLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel
            });
            AssertSuccess(createLabelResult);

            // ---- Step 5: batch add 3 entries with mixed addresses/labels ----
            step = 5;
            var addResult = new AddrAddEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName,
                ["assets"] = new JArray(
                    new JObject
                    {
                        ["asset_path"] = Asset1Path,
                        ["labels"] = new JArray(TestLabel)
                    },
                    new JObject
                    {
                        ["asset_path"] = Asset2Path,
                        ["address"] = "ui/main",
                        ["labels"] = new JArray(TestLabel)
                    },
                    new JObject
                    {
                        ["asset_path"] = Asset3Path
                    }
                )
            });
            AssertSuccess(addResult);
            Assert.AreEqual(3, addResult.Value<int>("added"),
                $"step {step}: all 3 entries should be added");
            Assert.AreEqual(0, addResult.Value<int>("skipped"),
                $"step {step}: nothing should be skipped");

            // ---- Step 6: list_entries on the group returns 3 ----
            step = 6;
            var listResult = new AddrListEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName
            });
            AssertSuccess(listResult);
            Assert.AreEqual(3, listResult.Value<int>("total"),
                $"step {step}: group should have 3 entries");

            // ---- Step 7: find_asset on the addressed entry ----
            step = 7;
            var findResult = new AddrFindAssetTool().Execute(new JObject
            {
                ["asset_path"] = Asset2Path
            });
            AssertSuccess(findResult);
            Assert.IsTrue(findResult.Value<bool>("found"),
                $"step {step}: asset2 should be found");
            var foundEntry = findResult["entry"] as JObject;
            Assert.AreEqual("ui/main", foundEntry.Value<string>("address"),
                $"step {step}: address should match what was set in step 5");
            Assert.AreEqual(TestGroupName, foundEntry.Value<string>("group"),
                $"step {step}: group should match");

            // ---- Step 8: set_entry — change address + add a new label (auto-create) ----
            step = 8;
            var setResult = new AddrSetEntryTool().Execute(new JObject
            {
                ["asset_path"] = Asset1Path,
                ["new_address"] = "ui/alt",
                ["add_labels"] = new JArray(TestLabel2)
            });
            AssertSuccess(setResult);
            Assert.AreEqual("ui/alt", setResult.Value<string>("address"),
                $"step {step}: new address should be applied");

            // ---- Step 9: list_labels shows both labels ----
            step = 9;
            var labelsResult = new AddrListLabelsTool().Execute(new JObject());
            AssertSuccess(labelsResult);
            var allLabels = (labelsResult["labels"] as JArray).Select(l => l.ToString()).ToList();
            CollectionAssert.Contains(allLabels, TestLabel,
                $"step {step}: TestLabel should still exist");
            CollectionAssert.Contains(allLabels, TestLabel2,
                $"step {step}: TestLabel2 should have been auto-created");

            // ---- Step 10: create a second group ----
            step = 10;
            var createGroup2Result = new AddrCreateGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName2
            });
            AssertSuccess(createGroup2Result);

            // ---- Step 11: move asset1 to the second group ----
            step = 11;
            var moveResult = new AddrMoveEntriesTool().Execute(new JObject
            {
                ["target_group"] = TestGroupName2,
                ["entries"] = new JArray(
                    new JObject { ["asset_path"] = Asset1Path }
                )
            });
            AssertSuccess(moveResult);
            Assert.AreEqual(1, moveResult.Value<int>("moved"),
                $"step {step}: 1 entry should be moved");

            // ---- Step 12: confirm asset1 now lives in the second group ----
            step = 12;
            var listGroup2Result = new AddrListEntriesTool().Execute(new JObject
            {
                ["group"] = TestGroupName2
            });
            AssertSuccess(listGroup2Result);
            Assert.AreEqual(1, listGroup2Result.Value<int>("total"),
                $"step {step}: TestGroupName2 should have 1 entry");
            var movedEntry = (listGroup2Result["entries"] as JArray).OfType<JObject>().First();
            Assert.AreEqual(Asset1Path, movedEntry.Value<string>("assetPath"),
                $"step {step}: moved entry should be asset1");

            // ---- Step 13: remove the auto-created label with force, asset1 stripped ----
            step = 13;
            var removeLabelResult = new AddrRemoveLabelTool().Execute(new JObject
            {
                ["label"] = TestLabel2,
                ["force"] = true
            });
            AssertSuccess(removeLabelResult);
            Assert.AreEqual(1, removeLabelResult.Value<int>("affectedEntries"),
                $"step {step}: 1 entry should have been stripped of the label");

            // ---- Step 14: remove_entries with mixed valid/invalid identifiers ----
            step = 14;
            var removeEntriesResult = new AddrRemoveEntriesTool().Execute(new JObject
            {
                ["entries"] = new JArray(
                    new JObject { ["asset_path"] = Asset2Path },
                    new JObject { ["asset_path"] = Asset3Path },
                    new JObject { ["asset_path"] = "Assets/Does/Not/Exist.png" }
                )
            });
            AssertSuccess(removeEntriesResult);
            Assert.AreEqual(2, removeEntriesResult.Value<int>("removed"),
                $"step {step}: 2 entries should be removed");
            Assert.AreEqual(1, removeEntriesResult.Value<int>("notFound"),
                $"step {step}: 1 entry should be reported notFound");

            // ---- Step 15: remove first group (now empty after step 14) ----
            step = 15;
            var removeGroup1Result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName,
                ["force"] = true
            });
            AssertSuccess(removeGroup1Result);

            // ---- Step 16: remove second group (still has asset1, force needed) ----
            step = 16;
            var removeGroup2Result = new AddrRemoveGroupTool().Execute(new JObject
            {
                ["name"] = TestGroupName2,
                ["force"] = true
            });
            AssertSuccess(removeGroup2Result);
            Assert.AreEqual(1, removeGroup2Result.Value<int>("removedEntryCount"),
                $"step {step}: 1 entry should be removed with the group");

            // ---- Step 17: list_groups confirms both test groups gone ----
            step = 17;
            var finalListResult = new AddrListGroupsTool().Execute(new JObject());
            AssertSuccess(finalListResult);
            var finalGroups = (finalListResult["groups"] as JArray)
                .OfType<JObject>()
                .Select(g => g.Value<string>("name"))
                .ToList();
            CollectionAssert.DoesNotContain(finalGroups, TestGroupName,
                $"step {step}: TestGroupName should no longer exist");
            CollectionAssert.DoesNotContain(finalGroups, TestGroupName2,
                $"step {step}: TestGroupName2 should no longer exist");

            // ---- Step 18: default group untouched throughout the lifecycle ----
            step = 18;
            var settingsAfter = new AddrGetSettingsTool().Execute(new JObject());
            AssertSuccess(settingsAfter);
            Assert.AreEqual(defaultGroupBefore, settingsAfter.Value<string>("defaultGroup"),
                $"step {step}: default group must not have drifted across the lifecycle");
        }
    }
}
