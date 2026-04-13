using System.Collections.Generic;
using System.IO;
using System.Linq;
using McpUnity.Tools.Localization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace McpUnity.Tests.Localization
{
    /// <summary>
    /// Integration + unit tests for the Unity Localization MCP tools.
    /// Each test drives the tool's Execute(JObject) directly and asserts on the JObject response,
    /// matching the contract used by the Node-side wrapper.
    /// </summary>
    [TestFixture]
    public class LocTests
    {
        private const string TestDir = "Assets/Tests/LocalizationTests";
        private const string TestTableName = "McpLocToolTestTable";
        private const string TestLocaleCode = "zh-TW";

        private Locale _testLocale;
        private bool _ownsTestLocale;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
                AssetDatabase.CreateFolder("Assets", "Tests");
            if (!AssetDatabase.IsValidFolder(TestDir))
                AssetDatabase.CreateFolder("Assets/Tests", "LocalizationTests");

            DeleteExistingTestTableIfAny();

            var identifier = new LocaleIdentifier(TestLocaleCode);
            _testLocale = LocalizationEditorSettings.GetLocales()
                .FirstOrDefault(l => l.Identifier == identifier);

            if (_testLocale == null)
            {
                _testLocale = Locale.CreateLocale(identifier);
                AssetDatabase.CreateAsset(_testLocale, $"{TestDir}/Locale_{TestLocaleCode}.asset");
                LocalizationEditorSettings.AddLocale(_testLocale, createUndo: false);
                _ownsTestLocale = true;
            }

            AssetDatabase.SaveAssets();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            DeleteExistingTestTableIfAny();

            if (_ownsTestLocale && _testLocale != null)
            {
                LocalizationEditorSettings.RemoveLocale(_testLocale, createUndo: false);
                string path = AssetDatabase.GetAssetPath(_testLocale);
                if (!string.IsNullOrEmpty(path))
                    AssetDatabase.DeleteAsset(path);
            }

            if (AssetDatabase.IsValidFolder(TestDir))
                AssetDatabase.DeleteAsset(TestDir);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void DeleteExistingTestTableIfAny()
        {
            var existing = LocalizationEditorSettings.GetStringTableCollections()
                .FirstOrDefault(c => c.TableCollectionName == TestTableName);
            if (existing != null)
            {
                DeleteStringTableCollection(existing);
            }
        }

        private static void AssertSuccess(JObject result)
        {
            Assert.IsNotNull(result, "Result is null");
            Assert.IsNull(result["error"], $"Unexpected error: {result["error"]}");
            Assert.IsTrue(result.Value<bool>("success"), $"success != true. Result: {result}");
        }

        private static void AssertError(JObject result, string expectedErrorType = null)
        {
            Assert.IsNotNull(result, "Result is null");
            Assert.IsNotNull(result["error"], $"Expected error, got: {result}");
            if (expectedErrorType != null)
            {
                Assert.AreEqual(expectedErrorType, result["error"]?["type"]?.ToString());
            }
        }

        private static StringTableCollection FindTestCollection()
        {
            return LocalizationEditorSettings.GetStringTableCollections()
                .FirstOrDefault(c => c.TableCollectionName == TestTableName);
        }

        // ------------------------------------------------------------------------
        // Scenario: full lifecycle (ordered)
        // ------------------------------------------------------------------------

        [Test, Order(10)]
        public void CreateTable_CreatesCollectionOnDisk()
        {
            var tool = new LocCreateTableTool();
            var result = tool.Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["locales"] = new JArray(TestLocaleCode),
                ["directory"] = TestDir
            });

            AssertSuccess(result);
            Assert.AreEqual(true, result.Value<bool>("created"));
            Assert.AreEqual(TestTableName, result.Value<string>("name"));

            var collection = FindTestCollection();
            Assert.IsNotNull(collection, "Collection was not registered with LocalizationEditorSettings");
            Assert.AreEqual(TestTableName, collection.TableCollectionName);
        }

        [Test, Order(20)]
        public void ListTables_IncludesTestTable()
        {
            var result = new LocListTablesTool().Execute(new JObject());
            AssertSuccess(result);

            var tables = result["tables"] as JArray;
            Assert.IsNotNull(tables);

            var entry = tables.OfType<JObject>()
                .FirstOrDefault(t => t.Value<string>("name") == TestTableName);
            Assert.IsNotNull(entry, $"Test table not in list_tables output: {tables}");

            var locales = entry["locales"] as JArray;
            Assert.IsNotNull(locales);
            Assert.IsTrue(locales.Any(l => l.ToString() == TestLocaleCode),
                $"Test locale not in collection locales: {locales}");
        }

        [Test, Order(30)]
        public void SetEntry_CreatesNewKey()
        {
            var tool = new LocSetEntryTool();
            var result = tool.Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "greeting_hello",
                ["value"] = "你好"
            });

            AssertSuccess(result);
            Assert.AreEqual("created", result.Value<string>("action"));
            Assert.AreEqual("greeting_hello", result.Value<string>("key"));
            Assert.AreEqual("你好", result.Value<string>("value"));
        }

        [Test, Order(40)]
        public void SetEntry_UpdatesExistingKey()
        {
            // First call creates; second call should update.
            var tool = new LocSetEntryTool();

            var first = tool.Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "greeting_hello",
                ["value"] = "你好，世界"
            });
            AssertSuccess(first);
            Assert.AreEqual("updated", first.Value<string>("action"));
            Assert.AreEqual("你好，世界", first.Value<string>("value"));
        }

        [Test, Order(50)]
        public void SetEntry_PreservesRichTextMarkup()
        {
            const string richValue = "<color=#88CCFF>123</color> 層（差 <color=#FF6666>5</color>）";
            var tool = new LocSetEntryTool();
            var result = tool.Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "cond_progress",
                ["value"] = richValue
            });

            AssertSuccess(result);
            Assert.AreEqual(richValue, result.Value<string>("value"));

            // Confirm the asset actually holds the rich text markup verbatim.
            var collection = FindTestCollection();
            var table = collection.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
            Assert.IsNotNull(table);
            var entry = table.GetEntry("cond_progress");
            Assert.IsNotNull(entry);
            Assert.AreEqual(richValue, entry.Value);
        }

        [Test, Order(60)]
        public void SetEntries_BatchCreatesAndUpdates()
        {
            var tool = new LocSetEntriesTool();
            var entries = new JArray
            {
                new JObject { ["key"] = "cb_ext_item_1", ["value"] = "物品 1" },
                new JObject { ["key"] = "cb_ext_item_2", ["value"] = "物品 2" },
                new JObject { ["key"] = "cb_ext_item_3", ["value"] = "物品 3" },
                // One update of an existing key from a prior test:
                new JObject { ["key"] = "greeting_hello", ["value"] = "嗨" }
            };

            var result = tool.Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["entries"] = entries
            });

            AssertSuccess(result);
            Assert.AreEqual(3, result.Value<int>("created"));
            Assert.AreEqual(1, result.Value<int>("updated"));
            Assert.AreEqual(4, result.Value<int>("total"));
        }

        [Test, Order(70)]
        public void GetEntries_ReturnsAllKeys()
        {
            var result = new LocGetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName
            });

            AssertSuccess(result);
            var entries = result["entries"] as JArray;
            Assert.IsNotNull(entries);
            // At this point we expect: greeting_hello, cond_progress, cb_ext_item_1/2/3
            Assert.GreaterOrEqual(entries.Count, 5);

            var keys = entries.OfType<JObject>().Select(e => e.Value<string>("key")).ToList();
            CollectionAssert.Contains(keys, "greeting_hello");
            CollectionAssert.Contains(keys, "cond_progress");
            CollectionAssert.Contains(keys, "cb_ext_item_1");

            // Confirm the updated value from the batch ("嗨") is what comes back.
            var helloEntry = entries.OfType<JObject>()
                .First(e => e.Value<string>("key") == "greeting_hello");
            Assert.AreEqual("嗨", helloEntry.Value<string>("value"));
        }

        [Test, Order(80)]
        public void GetEntries_KeyPrefixFilter()
        {
            var result = new LocGetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["filter"] = "cb_ext_"
            });

            AssertSuccess(result);
            var entries = result["entries"] as JArray;
            Assert.IsNotNull(entries);
            Assert.AreEqual(3, entries.Count, "Filter should match exactly 3 cb_ext_* keys");
            Assert.IsTrue(entries.OfType<JObject>().All(e => e.Value<string>("key").StartsWith("cb_ext_")));
        }

        [Test, Order(90)]
        public void DeleteEntry_RemovesExistingKey()
        {
            var result = new LocDeleteEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "cb_ext_item_2"
            });

            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("deleted"));

            // Follow-up: get_entries should no longer return the deleted key.
            var after = new LocGetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName
            });
            AssertSuccess(after);
            var keys = (after["entries"] as JArray)
                .OfType<JObject>()
                .Select(e => e.Value<string>("key"))
                .ToList();
            CollectionAssert.DoesNotContain(keys, "cb_ext_item_2");
        }

        [Test, Order(100)]
        public void DeleteEntry_NonExistentKey_ReturnsDeletedFalse()
        {
            var result = new LocDeleteEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "this_key_does_not_exist_anywhere"
            });

            AssertSuccess(result);
            Assert.IsFalse(result.Value<bool>("deleted"));
        }

        // ------------------------------------------------------------------------
        // Error paths (independent of the ordered scenario)
        // ------------------------------------------------------------------------

        [Test]
        public void GetEntries_MissingTable_ReturnsTableNotFound()
        {
            var result = new LocGetEntriesTool().Execute(new JObject
            {
                ["table_name"] = "DoesNotExistTable_xyz"
            });

            AssertError(result, "table_not_found");
        }

        [Test]
        public void SetEntry_MissingTableName_ReturnsValidationError()
        {
            var result = new LocSetEntryTool().Execute(new JObject
            {
                ["key"] = "k",
                ["value"] = "v"
            });

            AssertError(result, "validation_error");
        }

        [Test]
        public void SetEntry_WhitespaceKey_ReturnsValidationError()
        {
            var result = new LocSetEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "   ",
                ["value"] = "v"
            });

            AssertError(result, "validation_error");
        }

        [Test]
        public void CreateTable_Duplicate_ReturnsDuplicateError()
        {
            // Ensure a collection exists for this assertion (Order(10) test creates it,
            // but this test runs unordered — make a dedicated table if needed).
            const string dupName = "McpLocToolDupTestTable";
            var locales = new[] { _testLocale }.ToList();

            var existing = LocalizationEditorSettings.GetStringTableCollections()
                .FirstOrDefault(c => c.TableCollectionName == dupName);
            if (existing == null)
            {
                LocalizationEditorSettings.CreateStringTableCollection(dupName, TestDir, locales);
            }

            try
            {
                var result = new LocCreateTableTool().Execute(new JObject
                {
                    ["table_name"] = dupName,
                    ["locales"] = new JArray(TestLocaleCode),
                    ["directory"] = TestDir
                });

                AssertError(result, "duplicate_table");
            }
            finally
            {
                var dup = LocalizationEditorSettings.GetStringTableCollections()
                    .FirstOrDefault(c => c.TableCollectionName == dupName);
                if (dup != null) DeleteStringTableCollection(dup);
            }
        }

        [Test]
        public void CreateTable_NoValidLocales_ReturnsError()
        {
            var result = new LocCreateTableTool().Execute(new JObject
            {
                ["table_name"] = "McpLocToolNoLocaleTable",
                ["locales"] = new JArray("xx-INVALID"),
                ["directory"] = TestDir
            });

            AssertError(result, "no_valid_locales");
        }

        // ========================================================================
        // Refactor coverage — v1.9.1
        // ========================================================================
        // Tests below exist specifically to lock in the v1.9.1 refactor:
        //   B1 - DeleteEntry orphan leak fix (per-locale entries must be removed)
        //   B2 - SetEntries inner error message preservation
        //   B3 - SetEntries pre-flight all-or-nothing semantic
        //   C1 - EnsureFolderExists creates folders via AssetDatabase
        //   C2 - ValidateAssetPath rejects paths outside Assets/
        //   C3 - AddLocale soft warning for unrecognised culture codes
        //   C4 - FindLocale consistency (covered indirectly)
        // ========================================================================

        // ---- B1: orphan entry leak ---------------------------------------------

        [Test, Order(110)]
        public void DeleteEntry_RemovesPerLocaleStringTableEntry_NoOrphan()
        {
            // The pre-fix bug only removed the SharedData key, leaving the per-locale
            // StringTableEntry as an orphan. loc_get_entries hides it (filters by
            // SharedData), so this test must inspect the StringTable directly.
            const string probeKey = "orphan_probe_b1";

            new LocSetEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = probeKey,
                ["value"] = "to be deleted"
            });

            var collection = FindTestCollection();
            var sharedEntry = collection.SharedData.GetEntry(probeKey);
            Assert.IsNotNull(sharedEntry, "Setup failed: probe key not in SharedData");
            long keyId = sharedEntry.Id;

            var table = collection.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
            Assert.IsNotNull(table.GetEntry(keyId), "Setup failed: probe entry not in StringTable");

            var result = new LocDeleteEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = probeKey
            });
            AssertSuccess(result);
            Assert.IsTrue(result.Value<bool>("deleted"));

            var after = FindTestCollection();
            var tableAfter = after.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
            Assert.IsNull(tableAfter.GetEntry(keyId),
                "B1 regression: orphan StringTableEntry still present in per-locale table after delete");
            Assert.IsNull(after.SharedData.GetEntry(probeKey),
                "SharedData entry should also be gone");
        }

        [Test]
        public void DeleteEntry_MultipleLocales_NoOrphanInAnyLocale()
        {
            // Strong variant of B1: with multiple locales, the orphan bug would leave
            // an orphan entry in EVERY locale's StringTable. This test creates its own
            // table with two locales, sets values in both, deletes, and verifies both.
            const string multiTableName = "McpLocToolMultiLocaleDeleteTable";
            const string secondLocaleCode = "en";

            var secondLocale = FindLocaleByCode(secondLocaleCode);
            bool ownsSecondLocale = false;
            if (secondLocale == null)
            {
                secondLocale = Locale.CreateLocale(new LocaleIdentifier(secondLocaleCode));
                AssetDatabase.CreateAsset(secondLocale, $"{TestDir}/Locale_{secondLocaleCode}.asset");
                LocalizationEditorSettings.AddLocale(secondLocale, createUndo: false);
                ownsSecondLocale = true;
                AssetDatabase.SaveAssets();
            }

            StringTableCollection multiCollection = null;
            try
            {
                var locales = new List<Locale> { _testLocale, secondLocale };
                multiCollection = LocalizationEditorSettings.CreateStringTableCollection(
                    multiTableName, TestDir, locales);
                Assert.IsNotNull(multiCollection);

                new LocSetEntryTool().Execute(new JObject
                {
                    ["table_name"] = multiTableName,
                    ["locale"] = TestLocaleCode,
                    ["key"] = "multi_probe",
                    ["value"] = "你好"
                });
                new LocSetEntryTool().Execute(new JObject
                {
                    ["table_name"] = multiTableName,
                    ["locale"] = secondLocaleCode,
                    ["key"] = "multi_probe",
                    ["value"] = "Hello"
                });

                var sharedEntry = multiCollection.SharedData.GetEntry("multi_probe");
                Assert.IsNotNull(sharedEntry);
                long keyId = sharedEntry.Id;

                var t1 = multiCollection.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
                var t2 = multiCollection.GetTable(new LocaleIdentifier(secondLocaleCode)) as StringTable;
                Assert.IsNotNull(t1.GetEntry(keyId), "Setup failed: probe missing in zh-TW table");
                Assert.IsNotNull(t2.GetEntry(keyId), "Setup failed: probe missing in en table");

                var result = new LocDeleteEntryTool().Execute(new JObject
                {
                    ["table_name"] = multiTableName,
                    ["key"] = "multi_probe"
                });
                AssertSuccess(result);

                var after = LocalizationEditorSettings.GetStringTableCollections()
                    .First(c => c.TableCollectionName == multiTableName);
                var t1After = after.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
                var t2After = after.GetTable(new LocaleIdentifier(secondLocaleCode)) as StringTable;
                Assert.IsNull(t1After.GetEntry(keyId), "B1 regression: orphan in zh-TW StringTable");
                Assert.IsNull(t2After.GetEntry(keyId), "B1 regression: orphan in en StringTable");
                Assert.IsNull(after.SharedData.GetEntry("multi_probe"));
            }
            finally
            {
                if (multiCollection != null)
                    DeleteStringTableCollection(multiCollection);
                if (ownsSecondLocale && secondLocale != null)
                {
                    LocalizationEditorSettings.RemoveLocale(secondLocale, createUndo: false);
                    var p = AssetDatabase.GetAssetPath(secondLocale);
                    if (!string.IsNullOrEmpty(p)) AssetDatabase.DeleteAsset(p);
                }
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void DeleteEntry_WhitespaceKey_ReturnsValidationError()
        {
            var result = new LocDeleteEntryTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["key"] = "   "
            });

            AssertError(result, "validation_error");
        }

        // ---- B2: inner error message preservation -------------------------------

        [Test, Order(120)]
        public void SetEntries_InvalidKey_PreservesInnerErrorMessage()
        {
            var result = new LocSetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["entries"] = new JArray
                {
                    new JObject { ["key"] = "valid_key_b2", ["value"] = "v1" },
                    new JObject { ["key"] = " key_with_whitespace ", ["value"] = "v2" }
                }
            });

            AssertError(result, "validation_error");

            string msg = result["error"]?["message"]?.ToString() ?? "";
            StringAssert.Contains("entries[1]", msg, "Message should include index prefix");
            StringAssert.Contains("leading/trailing whitespace", msg,
                "B2 regression: inner error detail must not be lost");
        }

        // ---- B3: pre-flight all-or-nothing -------------------------------------

        [Test, Order(130)]
        public void SetEntries_PartialFailure_NoEntryIsApplied()
        {
            // Probe key must not exist before, after, or anywhere on the way.
            // Old buggy behaviour: first valid entry would land in SharedData before
            // the second invalid entry caused the function to bail.
            const string probeKey = "preflight_probe_must_not_appear";

            var collection = FindTestCollection();
            Assert.IsNull(collection.SharedData.GetEntry(probeKey),
                "Pre-condition failed: probe key already exists");

            var result = new LocSetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["entries"] = new JArray
                {
                    new JObject { ["key"] = probeKey, ["value"] = "valid value" },
                    new JObject { ["key"] = "", ["value"] = "invalid - empty key" }
                }
            });

            AssertError(result, "validation_error");

            var collectionAfter = FindTestCollection();
            Assert.IsNull(collectionAfter.SharedData.GetEntry(probeKey),
                "B3 regression: valid entry was partially applied to SharedData before bailing");

            var tableAfter = collectionAfter.GetTable(new LocaleIdentifier(TestLocaleCode)) as StringTable;
            // Even if SharedData was clean, the StringTable should also have nothing for this key
            Assert.IsTrue(tableAfter.Values.All(e => e.Key != probeKey),
                "B3 regression: probe key found in per-locale StringTable");
        }

        [Test, Order(140)]
        public void SetEntries_FirstEntryInvalid_NoMutation()
        {
            // Edge case: failure is on the very first entry — nothing should mutate.
            const string probeKey = "preflight_first_invalid_probe";

            var result = new LocSetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["entries"] = new JArray
                {
                    new JObject { ["key"] = "  bad", ["value"] = "first invalid" },
                    new JObject { ["key"] = probeKey, ["value"] = "would-be-valid" }
                }
            });

            AssertError(result, "validation_error");

            var collection = FindTestCollection();
            Assert.IsNull(collection.SharedData.GetEntry(probeKey),
                "Second valid entry must not have been applied");
        }

        [Test, Order(150)]
        public void SetEntries_AllInvalid_NoMutation()
        {
            // All entries fail validation — should report on entry[0] and apply nothing.
            var result = new LocSetEntriesTool().Execute(new JObject
            {
                ["table_name"] = TestTableName,
                ["entries"] = new JArray
                {
                    new JObject { ["key"] = "", ["value"] = "v1" },
                    new JObject { ["key"] = "  ", ["value"] = "v2" },
                    new JObject { ["key"] = " spaced ", ["value"] = "v3" }
                }
            });

            AssertError(result, "validation_error");
            string msg = result["error"]?["message"]?.ToString() ?? "";
            StringAssert.Contains("entries[0]", msg,
                "Pre-flight should fail on the first invalid entry it encounters");
        }

        // ---- C1: EnsureFolderExists --------------------------------------------

        [Test]
        public void CreateTable_NestedDirectoryAutoCreated()
        {
            const string nestedDir = "Assets/Tests/LocalizationTests/NestedAuto/Sub";
            const string nestedTableName = "McpLocToolNestedDirTable";

            Assert.IsFalse(AssetDatabase.IsValidFolder(nestedDir),
                "Pre-condition failed: nested dir already exists");

            try
            {
                var result = new LocCreateTableTool().Execute(new JObject
                {
                    ["table_name"] = nestedTableName,
                    ["locales"] = new JArray(TestLocaleCode),
                    ["directory"] = nestedDir
                });

                AssertSuccess(result);
                Assert.IsTrue(AssetDatabase.IsValidFolder(nestedDir),
                    "Nested folder was not created");
                Assert.IsTrue(AssetDatabase.IsValidFolder("Assets/Tests/LocalizationTests/NestedAuto"),
                    "Intermediate folder was not created");

                // .meta files are written by AssetDatabase.CreateFolder — verify by checking
                // that the folder GUID is non-empty (raw Directory.CreateDirectory would fail this).
                string guid = AssetDatabase.AssetPathToGUID(nestedDir);
                Assert.IsFalse(string.IsNullOrEmpty(guid),
                    "C1 regression: folder has no GUID — was it created with Directory.CreateDirectory?");
            }
            finally
            {
                var col = LocalizationEditorSettings.GetStringTableCollections()
                    .FirstOrDefault(c => c.TableCollectionName == nestedTableName);
                if (col != null) DeleteStringTableCollection(col);
                if (AssetDatabase.IsValidFolder("Assets/Tests/LocalizationTests/NestedAuto"))
                    AssetDatabase.DeleteAsset("Assets/Tests/LocalizationTests/NestedAuto");
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void CreateTable_OmittedDirectory_UsesDefaultPath()
        {
            const string defaultProbeName = "McpLocToolDefaultDirProbeTable";
            const string defaultDir = "Assets/Localization/Tables";
            bool defaultDirPreExisted = AssetDatabase.IsValidFolder(defaultDir);

            try
            {
                var result = new LocCreateTableTool().Execute(new JObject
                {
                    ["table_name"] = defaultProbeName,
                    ["locales"] = new JArray(TestLocaleCode)
                    // no "directory" field
                });

                AssertSuccess(result);
                string path = result.Value<string>("path");
                Assert.IsNotNull(path);
                StringAssert.StartsWith(defaultDir, path,
                    "Omitted directory should fall back to default Assets/Localization/Tables");
                Assert.IsTrue(AssetDatabase.IsValidFolder(defaultDir));
            }
            finally
            {
                var col = LocalizationEditorSettings.GetStringTableCollections()
                    .FirstOrDefault(c => c.TableCollectionName == defaultProbeName);
                if (col != null) DeleteStringTableCollection(col);

                // Only delete the default dir if WE created it — never touch user's existing assets.
                if (!defaultDirPreExisted && AssetDatabase.IsValidFolder(defaultDir))
                {
                    AssetDatabase.DeleteAsset(defaultDir);
                    if (AssetDatabase.IsValidFolder("Assets/Localization"))
                        AssetDatabase.DeleteAsset("Assets/Localization");
                }
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void CreateTable_DirectoryWithTrailingSlash_Normalised()
        {
            const string slashDir = "Assets/Tests/LocalizationTests/SlashTest";
            const string slashTableName = "McpLocToolSlashTable";

            try
            {
                var result = new LocCreateTableTool().Execute(new JObject
                {
                    ["table_name"] = slashTableName,
                    ["locales"] = new JArray(TestLocaleCode),
                    ["directory"] = slashDir + "/" // trailing slash
                });

                AssertSuccess(result);
                string path = result.Value<string>("path");
                StringAssert.StartsWith(slashDir + "/", path);
                StringAssert.DoesNotContain("//", path, "Trailing slash should be trimmed, not duplicated");
            }
            finally
            {
                var col = LocalizationEditorSettings.GetStringTableCollections()
                    .FirstOrDefault(c => c.TableCollectionName == slashTableName);
                if (col != null) DeleteStringTableCollection(col);
                if (AssetDatabase.IsValidFolder(slashDir))
                    AssetDatabase.DeleteAsset(slashDir);
                AssetDatabase.SaveAssets();
            }
        }

        // ---- C2: ValidateAssetPath ---------------------------------------------

        [Test]
        public void CreateTable_DirectoryOutsideAssets_ReturnsValidationError()
        {
            var result = new LocCreateTableTool().Execute(new JObject
            {
                ["table_name"] = "McpLocToolBadPathTable",
                ["locales"] = new JArray(TestLocaleCode),
                ["directory"] = "/tmp/escape_attempt"
            });

            AssertError(result, "validation_error");
            StringAssert.Contains("Assets", result["error"]?["message"]?.ToString() ?? "");
        }

        [Test]
        public void CreateTable_RelativeEscapeDirectory_ReturnsValidationError()
        {
            var result = new LocCreateTableTool().Execute(new JObject
            {
                ["table_name"] = "McpLocToolRelEscape",
                ["locales"] = new JArray(TestLocaleCode),
                ["directory"] = "../OutsideAssets"
            });

            AssertError(result, "validation_error");
        }

        [Test]
        public void AddLocale_DirectoryOutsideAssets_ReturnsValidationError()
        {
            var result = new LocAddLocaleTool().Execute(new JObject
            {
                ["code"] = "fr",
                ["directory"] = "../OutsideProject"
            });

            AssertError(result, "validation_error");
        }

        // ---- C3: AddLocale soft-warning precheck -------------------------------

        [Test]
        public void AddLocale_AlreadyExists_ReturnsAlreadyExistsAction()
        {
            // _testLocale (zh-TW) is set up in OneTimeSetUp.
            var result = new LocAddLocaleTool().Execute(new JObject
            {
                ["code"] = TestLocaleCode,
                ["directory"] = TestDir
            });

            AssertSuccess(result);
            Assert.AreEqual("already_exists", result.Value<string>("action"));
            Assert.AreEqual(TestLocaleCode, result.Value<string>("code"));
        }

        [Test]
        public void AddLocale_MissingCode_ReturnsValidationError()
        {
            var result = new LocAddLocaleTool().Execute(new JObject());
            AssertError(result, "validation_error");
        }

        [Test]
        public void AddLocale_UnrecognisedCultureCode_StillCreatesWithWarning()
        {
            // The whole point of C3's "soft warning" approach: an unrecognised culture
            // code must NOT be hard-rejected. Unity Localization itself accepts arbitrary
            // identifiers (e.g. "zh-Hant"), so we warn rather than error.
            const string oddCode = "xx-NOSUCH";
            const string oddDir = "Assets/Tests/LocalizationTests/OddLocale";

            CleanupLocaleIfPresent(oddCode);

            try
            {
                var result = new LocAddLocaleTool().Execute(new JObject
                {
                    ["code"] = oddCode,
                    ["directory"] = oddDir
                });

                AssertSuccess(result);
                Assert.AreEqual("created", result.Value<string>("action"),
                    "C3 regression: unrecognised culture must still be created (soft warning, not hard error)");

                var warnings = result["warnings"] as JArray;
                Assert.IsNotNull(warnings, "Warnings array missing for unrecognised culture");
                Assert.IsTrue(warnings.Count > 0, "At least one warning expected");
                StringAssert.Contains("not recognised", warnings[0].ToString());
            }
            finally
            {
                CleanupLocaleIfPresent(oddCode);
                if (AssetDatabase.IsValidFolder(oddDir))
                    AssetDatabase.DeleteAsset(oddDir);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void AddLocale_RecognisedCulture_NoWarnings()
        {
            // Confirms warnings are *conditional* — recognised culture codes get no warning.
            // Use Icelandic ("is") as an unlikely-to-be-preconfigured-but-valid IETF code.
            const string code = "is";
            const string dir = "Assets/Tests/LocalizationTests/RecognisedLocale";

            if (FindLocaleByCode(code) != null)
            {
                Assert.Inconclusive($"Locale '{code}' already registered in this project — cannot test fresh-create path");
            }

            try
            {
                var result = new LocAddLocaleTool().Execute(new JObject
                {
                    ["code"] = code,
                    ["directory"] = dir
                });

                AssertSuccess(result);
                Assert.AreEqual("created", result.Value<string>("action"));
                Assert.IsNull(result["warnings"],
                    "C3 regression: recognised culture should produce NO warnings field");
            }
            finally
            {
                CleanupLocaleIfPresent(code);
                if (AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.DeleteAsset(dir);
                AssetDatabase.SaveAssets();
            }
        }

        // ========================================================================
        // D4 — loc_delete_table + loc_remove_locale
        // ========================================================================

        [Test]
        public void DeleteTable_RemovesCollectionAndAllAssets()
        {
            const string deleteTableName = "McpLocToolDeleteTableProbe";
            const string deleteDir = "Assets/Tests/LocalizationTests/DeleteProbe";

            // Setup: create the table directly via the production tool so we exercise
            // the same code path users would run.
            var createResult = new LocCreateTableTool().Execute(new JObject
            {
                ["table_name"] = deleteTableName,
                ["locales"] = new JArray(TestLocaleCode),
                ["directory"] = deleteDir
            });
            AssertSuccess(createResult);

            string sharedDataPath = createResult.Value<string>("path");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sharedDataPath),
                "Setup failed: SharedData asset not found");

            try
            {
                // Act
                var result = new LocDeleteTableTool().Execute(new JObject
                {
                    ["table_name"] = deleteTableName
                });

                // Assert: response shape
                AssertSuccess(result);
                Assert.IsTrue(result.Value<bool>("deleted"));
                Assert.AreEqual(deleteTableName, result.Value<string>("name"));

                // Collection no longer in LocalizationEditorSettings
                var stillThere = LocalizationEditorSettings.GetStringTableCollections()
                    .FirstOrDefault(c => c.TableCollectionName == deleteTableName);
                Assert.IsNull(stillThere, "Collection still registered after delete");

                // SharedData asset gone from disk
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sharedDataPath),
                    "SharedData asset still loadable after delete");
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(deleteDir))
                    AssetDatabase.DeleteAsset(deleteDir);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void DeleteTable_MissingTable_ReturnsTableNotFound()
        {
            var result = new LocDeleteTableTool().Execute(new JObject
            {
                ["table_name"] = "DoesNotExistTable_xyz_delete_probe"
            });
            AssertError(result, "table_not_found");
        }

        [Test]
        public void DeleteTable_MissingTableName_ReturnsValidationError()
        {
            var result = new LocDeleteTableTool().Execute(new JObject());
            AssertError(result, "validation_error");
        }

        [Test]
        public void RemoveLocale_RegisteredLocale_RemovesAndDeletesAsset()
        {
            const string code = "ko";
            const string dir = "Assets/Tests/LocalizationTests/RemoveLocaleProbe";

            if (FindLocaleByCode(code) != null)
            {
                Assert.Inconclusive($"Locale '{code}' already registered — cannot test the create-then-remove path");
            }

            // Setup: register the locale via the production tool
            var addResult = new LocAddLocaleTool().Execute(new JObject
            {
                ["code"] = code,
                ["directory"] = dir
            });
            AssertSuccess(addResult);
            string assetPath = addResult.Value<string>("path");
            Assert.IsNotNull(FindLocaleByCode(code), "Setup failed: locale not registered after add");

            try
            {
                // Act
                var result = new LocRemoveLocaleTool().Execute(new JObject
                {
                    ["code"] = code
                });

                // Assert
                AssertSuccess(result);
                Assert.AreEqual("removed", result.Value<string>("action"));
                Assert.AreEqual(code, result.Value<string>("code"));

                Assert.IsNull(FindLocaleByCode(code), "Locale still registered after remove");
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath),
                    "Locale asset still loadable after remove (delete_asset default true)");
            }
            finally
            {
                CleanupLocaleIfPresent(code);
                if (AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.DeleteAsset(dir);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public void RemoveLocale_NotRegistered_ReturnsNotRegisteredAction()
        {
            const string code = "xx-MISSING-FROM-PROJECT";
            CleanupLocaleIfPresent(code); // belt-and-braces

            var result = new LocRemoveLocaleTool().Execute(new JObject
            {
                ["code"] = code
            });

            AssertSuccess(result);
            Assert.AreEqual("not_registered", result.Value<string>("action"));
        }

        [Test]
        public void RemoveLocale_MissingCode_ReturnsValidationError()
        {
            var result = new LocRemoveLocaleTool().Execute(new JObject());
            AssertError(result, "validation_error");
        }

        [Test]
        public void RemoveLocale_DeleteAssetFalse_KeepsFileOnDisk()
        {
            const string code = "vi";
            const string dir = "Assets/Tests/LocalizationTests/KeepAssetProbe";

            if (FindLocaleByCode(code) != null)
            {
                Assert.Inconclusive($"Locale '{code}' already registered — cannot test the keep-asset path");
            }

            var addResult = new LocAddLocaleTool().Execute(new JObject
            {
                ["code"] = code,
                ["directory"] = dir
            });
            AssertSuccess(addResult);
            string assetPath = addResult.Value<string>("path");

            try
            {
                var result = new LocRemoveLocaleTool().Execute(new JObject
                {
                    ["code"] = code,
                    ["delete_asset"] = false
                });
                AssertSuccess(result);

                // Locale registration gone
                Assert.IsNull(FindLocaleByCode(code));
                // But the asset file is still on disk
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath),
                    "delete_asset=false should leave the .asset file on disk");
            }
            finally
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                    AssetDatabase.DeleteAsset(assetPath);
                if (AssetDatabase.IsValidFolder(dir))
                    AssetDatabase.DeleteAsset(dir);
                AssetDatabase.SaveAssets();
            }
        }

        // ---- one-off cleanup of dangling xx-NOSUCH from session probe -----------
        // (See conversation around v1.10.0 — a verification probe re-registered
        // xx-NOSUCH after the user manually cleaned it up. This ensures it's gone
        // for good without needing an MCP server restart.)
        [Test]
        public void Cleanup_RemoveDanglingXxNoSuchFromVerifyProbe()
        {
            const string code = "xx-NOSUCH";
            var existed = FindLocaleByCode(code) != null;
            CleanupLocaleIfPresent(code);
            Assert.IsNull(FindLocaleByCode(code));
            // Note for test runner: we don't fail when it wasn't there — the cleanup
            // is idempotent. existed flag is captured purely for log-value.
            TestContext.WriteLine($"Cleanup_RemoveDanglingXxNoSuch: existed={existed}");
        }

        // ---- helpers -----------------------------------------------------------

        private static Locale FindLocaleByCode(string code)
        {
            return LocalizationEditorSettings.GetLocales()
                .FirstOrDefault(l => l.Identifier.Code == code);
        }

        private static void CleanupLocaleIfPresent(string code)
        {
            var locale = FindLocaleByCode(code);
            if (locale == null) return;
            LocalizationEditorSettings.RemoveLocale(locale, createUndo: false);
            string path = AssetDatabase.GetAssetPath(locale);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
        }

        // Delegates to the production helper so cleanup uses the same code path
        // that loc_delete_table exposes to MCP clients.
        private static void DeleteStringTableCollection(StringTableCollection collection) =>
            LocTableHelper.DeleteStringTableCollection(collection);
    }
}
