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
                LocalizationEditorSettings.RemoveCollection(existing);
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
                if (dup != null) LocalizationEditorSettings.RemoveCollection(dup);
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
    }
}
