using System;
using System.IO;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace McpUnity.Tests
{
    public class ManageAssetToolTests
    {
        private const string TestRoot = "Assets/ManageAssetToolTests_Temp";
        private const string SourcePath = TestRoot + "/Source.txt";
        private const string ExistingPath = TestRoot + "/Existing.txt";
        private const string MovedPath = TestRoot + "/Moved.txt";
        private const string RenamedPath = TestRoot + "/B.txt";
        private const string DottedRenamePath = TestRoot + "/foo..bar.txt";
        private const string CopyPath = TestRoot + "/Copied.txt";
        private const string CaseCopyPath = TestRoot + "/CaseCopied.txt";
        private const string CreatedFolderPath = TestRoot + "/CreatedFolder";
        private const string MissingParentPath = TestRoot + "/MissingParent";
        private const string SourceFolderPath = TestRoot + "/SourceFolder";
        private const string SourceFolderChildPath = SourceFolderPath + "/Child.txt";
        private const string SourceFolderCopyPath = SourceFolderPath + "/Backup";
        private const string CaseVariantSourceFolderCopyPath =
            TestRoot + "/sourcefolder/CaseBackup";
        private const string SourceFolderMovePath = SourceFolderPath + "/MovedInside";

        private string _testRootFullPath;
        private string _sourceGuid;
        private string _existingGuid;
        private string _sourceFolderGuid;
        private string _sourceFolderChildGuid;
        private bool _ownsTestRoot;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Assert.IsTrue(
                AssetPathUtils.TryNormalizeAssetPath(
                    TestRoot,
                    out _,
                    out _testRootFullPath,
                    out string pathError),
                pathError);
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(TestRoot) || Directory.Exists(_testRootFullPath),
                $"Refusing to claim pre-existing test folder '{TestRoot}'.");

            string rootGuid = AssetDatabase.CreateFolder("Assets", "ManageAssetToolTests_Temp");
            Assert.IsFalse(string.IsNullOrEmpty(rootGuid));
            _ownsTestRoot = true;

            CreateTextAsset(SourcePath, "source fixture");
            CreateTextAsset(ExistingPath, "existing destination fixture");
            string sourceFolderGuid =
                AssetDatabase.CreateFolder(TestRoot, "SourceFolder");
            Assert.IsFalse(string.IsNullOrEmpty(sourceFolderGuid));
            CreateTextAsset(SourceFolderChildPath, "source folder child fixture");
            _sourceGuid = AssetDatabase.AssetPathToGUID(SourcePath);
            _existingGuid = AssetDatabase.AssetPathToGUID(ExistingPath);
            _sourceFolderGuid = AssetDatabase.AssetPathToGUID(SourceFolderPath);
            _sourceFolderChildGuid = AssetDatabase.AssetPathToGUID(SourceFolderChildPath);
            Assert.IsFalse(string.IsNullOrEmpty(_sourceGuid));
            Assert.IsFalse(string.IsNullOrEmpty(_existingGuid));
            Assert.IsFalse(string.IsNullOrEmpty(_sourceFolderGuid));
            Assert.IsFalse(string.IsNullOrEmpty(_sourceFolderChildGuid));
        }

        [SetUp]
        public void SetUp()
        {
            ResetMutableFixtureState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetMutableFixtureState();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (!_ownsTestRoot)
                return;

            ClearReadOnlyAttributes();
            bool deleted = AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
            Assert.IsTrue(deleted, $"Failed to delete owned test folder '{TestRoot}'.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot));
            Assert.IsFalse(Directory.Exists(_testRootFullPath));
            Assert.IsFalse(File.Exists(_testRootFullPath + ".meta"));
            _ownsTestRoot = false;
        }

        [Test]
        public void Move_PreservesGuidMovesMetaAndReturnsReadBackPath()
        {
            string guidBefore = AssetDatabase.AssetPathToGUID(SourcePath);
            string oldMetaPath = GetFullPath(SourcePath) + ".meta";
            string newMetaPath = GetFullPath(MovedPath) + ".meta";

            JObject result = Execute("move", SourcePath, MovedPath);

            Assert.AreEqual(guidBefore, result["guid"]?.ToString(), result.ToString());
            Assert.AreEqual(MovedPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(MovedPath));
            Assert.AreEqual(MovedPath, AssetDatabase.GUIDToAssetPath(guidBefore));
            Assert.IsFalse(File.Exists(oldMetaPath));
            Assert.IsTrue(File.Exists(newMetaPath));
        }

        [Test]
        public void Rename_NewNameWithoutExtensionPreservesTxtExtensionAndGuid()
        {
            string guidBefore = AssetDatabase.AssetPathToGUID(SourcePath);

            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "rename",
                ["assetPath"] = SourcePath,
                ["newName"] = "B"
            });

            Assert.AreEqual(guidBefore, result["guid"]?.ToString(), result.ToString());
            Assert.AreEqual(RenamedPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(RenamedPath));
            Assert.AreEqual(RenamedPath, AssetDatabase.GUIDToAssetPath(guidBefore));
        }

        [Test]
        public void Copy_CreatesNewGuidAndDisclosesUnchangedSourceGuid()
        {
            string sourceGuidBefore = AssetDatabase.AssetPathToGUID(SourcePath);

            JObject result = Execute("copy", SourcePath, CopyPath);

            string copyGuid = result["guid"]?.ToString();
            Assert.AreEqual(CopyPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.AreEqual(SourcePath, result["sourcePath"]?.ToString(), result.ToString());
            Assert.AreEqual(sourceGuidBefore, result["sourceGuid"]?.ToString(), result.ToString());
            Assert.AreEqual(sourceGuidBefore, AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.IsFalse(string.IsNullOrEmpty(copyGuid));
            Assert.AreNotEqual(sourceGuidBefore, copyGuid);
            Assert.AreEqual(copyGuid, AssetDatabase.AssetPathToGUID(CopyPath));
        }

        [Test]
        public void Copy_DeletedDestinationWithStaleGuidCanBeRecreated()
        {
            CreateTextAsset(CopyPath, "stale destination fixture");
            Assert.IsTrue(AssetDatabase.DeleteAsset(CopyPath));
            Assert.IsFalse(File.Exists(GetFullPath(CopyPath)));
            Assert.IsFalse(
                string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CopyPath)),
                "Unity no longer reports stale GUIDs after delete; this regression pin needs redesign");
            Assert.IsTrue(
                string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(
                    CopyPath,
                    AssetPathToGUIDOptions.OnlyExistingAssets)),
                "OnlyExistingAssets must exclude the deleted destination before copy.");

            JObject result = Execute("copy", SourcePath, CopyPath);

            string copyGuid = result["guid"]?.ToString();
            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(CopyPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(copyGuid), result.ToString());
            Assert.AreEqual(
                copyGuid,
                AssetDatabase.AssetPathToGUID(
                    CopyPath,
                    AssetPathToGUIDOptions.OnlyExistingAssets));
        }

        [Test]
        public void Copy_FolderIntoDescendantReturnsValidationErrorWithoutCreatingArtifacts()
        {
            string folderGuidBefore = AssetDatabase.AssetPathToGUID(SourceFolderPath);
            string childGuidBefore = AssetDatabase.AssetPathToGUID(SourceFolderChildPath);

            JObject result = Execute("copy", SourceFolderPath, SourceFolderCopyPath);

            AssertErrorType(result, "validation_error");
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("inside the source folder"));
            Assert.AreEqual(folderGuidBefore, AssetDatabase.AssetPathToGUID(SourceFolderPath));
            Assert.AreEqual(childGuidBefore, AssetDatabase.AssetPathToGUID(SourceFolderChildPath));
            Assert.That(
                AssetDatabase.GetSubFolders(SourceFolderPath),
                Has.No.Member(SourceFolderCopyPath));
            Assert.IsFalse(Directory.Exists(GetFullPath(SourceFolderCopyPath)));
        }

        [Test]
        public void Copy_FolderIntoCaseVariantDescendantReturnsValidationError()
        {
            JObject result = Execute(
                "copy",
                SourceFolderPath,
                CaseVariantSourceFolderCopyPath);

            AssertErrorType(result, "validation_error");
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("case-insensitively inside the source folder"));
            Assert.That(
                AssetDatabase.GetSubFolders(SourceFolderPath),
                Has.No.Member(CaseVariantSourceFolderCopyPath));
            Assert.IsFalse(Directory.Exists(GetFullPath(CaseVariantSourceFolderCopyPath)));
        }

        [Test]
        public void Move_FolderIntoDescendantReturnsValidationErrorWithoutCreatingArtifacts()
        {
            string folderGuidBefore = AssetDatabase.AssetPathToGUID(SourceFolderPath);
            string childGuidBefore = AssetDatabase.AssetPathToGUID(SourceFolderChildPath);

            JObject result = Execute("move", SourceFolderPath, SourceFolderMovePath);

            AssertErrorType(result, "validation_error");
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("inside the source folder"));
            Assert.AreEqual(folderGuidBefore, AssetDatabase.AssetPathToGUID(SourceFolderPath));
            Assert.AreEqual(childGuidBefore, AssetDatabase.AssetPathToGUID(SourceFolderChildPath));
            Assert.That(
                AssetDatabase.GetSubFolders(SourceFolderPath),
                Has.No.Member(SourceFolderMovePath));
            Assert.IsFalse(Directory.Exists(GetFullPath(SourceFolderMovePath)));
        }

        [Test]
        public void Copy_OrphanDestinationMetaReturnsValidationErrorAndPreservesMetaBytes()
        {
            string destinationMetaPath = GetFullPath(CopyPath) + ".meta";
            byte[] orphanMeta = { 0x6f, 0x72, 0x70, 0x68, 0x61, 0x6e };
            File.WriteAllBytes(destinationMetaPath, orphanMeta);
            try
            {
                JObject result = Execute("copy", SourcePath, CopyPath);

                AssertErrorType(result, "validation_error");
                Assert.That(
                    result["error"]?["message"]?.ToString(),
                    Does.Contain("a .meta file already exists at destination"));
                CollectionAssert.AreEqual(orphanMeta, File.ReadAllBytes(destinationMetaPath));
                Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
                Assert.IsFalse(File.Exists(GetFullPath(CopyPath)));
            }
            finally
            {
                if (File.Exists(destinationMetaPath))
                    File.Delete(destinationMetaPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Copy_ReadOnlySourceSucceedsWithNewGuidAndUnchangedSource()
        {
            string sourceFullPath = GetFullPath(SourcePath);
            var sourceInfo = new FileInfo(sourceFullPath);
            bool wasReadOnly = sourceInfo.IsReadOnly;
            try
            {
                sourceInfo.IsReadOnly = true;

                JObject result = Execute("copy", SourcePath, CopyPath);

                string copyGuid = result["guid"]?.ToString();
                Assert.AreEqual(CopyPath, result["assetPath"]?.ToString(), result.ToString());
                Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
                Assert.IsFalse(string.IsNullOrEmpty(copyGuid));
                Assert.AreNotEqual(_sourceGuid, copyGuid);
                Assert.AreEqual(copyGuid, AssetDatabase.AssetPathToGUID(CopyPath));
            }
            finally
            {
                sourceInfo.Refresh();
                sourceInfo.IsReadOnly = wasReadOnly;
            }
        }

        [Test]
        public void Copy_CaseVariantExistingParentReturnsCanonicalPathOrMissingParentError()
        {
            const string caseVariantParent = "Assets/manageassettooltests_temp";
            string requestedDestination = caseVariantParent + "/CaseCopied.txt";
            bool caseVariantParentExists = AssetDatabase.IsValidFolder(caseVariantParent);

            JObject result = Execute("copy", SourcePath, requestedDestination);

            if (!caseVariantParentExists)
            {
                AssertErrorType(result, "validation_error");
                Assert.That(
                    result["error"]?["message"]?.ToString(),
                    Does.Contain("does not exist"));
                Assert.IsFalse(File.Exists(GetFullPath(CaseCopyPath)));
                return;
            }

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(CaseCopyPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.AreEqual(
                result["guid"]?.ToString(),
                AssetDatabase.AssetPathToGUID(CaseCopyPath));
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
        }

        [Test]
        public void Move_MissingParentReturnsValidationErrorWithoutMutationOrDirectoryCreation()
        {
            string destination = MissingParentPath + "/Moved.txt";
            string sourceGuidBefore = AssetDatabase.AssetPathToGUID(SourcePath);

            JObject result = Execute("move", SourcePath, destination);

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("create_folder"));
            Assert.AreEqual(sourceGuidBefore, AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.IsFalse(Directory.Exists(GetFullPath(MissingParentPath)));
            Assert.IsFalse(File.Exists(GetFullPath(destination)));
        }

        [Test]
        public void Move_ExistingDestinationReturnsValidationErrorWithoutMutation()
        {
            string sourceGuidBefore = AssetDatabase.AssetPathToGUID(SourcePath);
            string existingGuidBefore = AssetDatabase.AssetPathToGUID(ExistingPath);

            JObject result = Execute("move", SourcePath, ExistingPath);

            AssertErrorType(result, "validation_error");
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("destination already exists; overwrite is not supported"));
            Assert.AreEqual(sourceGuidBefore, AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.AreEqual(existingGuidBefore, AssetDatabase.AssetPathToGUID(ExistingPath));
        }

        [Test]
        public void MissingAndUnknownActionReturnValidationErrorAndListLegalValues()
        {
            JObject missing = new ManageAssetTool().Execute(new JObject
            {
                ["assetPath"] = SourcePath
            });
            JObject unknown = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "merge",
                ["assetPath"] = SourcePath
            });

            foreach (JObject result in new[] { missing, unknown })
            {
                AssertErrorType(result, "validation_error");
                string message = result["error"]?["message"]?.ToString();
                Assert.That(message, Does.Contain("move"));
                Assert.That(message, Does.Contain("copy"));
                Assert.That(message, Does.Contain("rename"));
                Assert.That(message, Does.Contain("create_folder"));
            }
        }

        [Test]
        public void BareRelativeAndEscapingPathsReturnValidationError()
        {
            JObject bare = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "move",
                ["assetPath"] = "Source.txt",
                ["destinationPath"] = MovedPath
            });
            JObject escaping = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "move",
                ["assetPath"] = "Assets/../../Source.txt",
                ["destinationPath"] = MovedPath
            });

            AssertErrorType(bare, "validation_error");
            AssertErrorType(escaping, "validation_error");
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
        }

        [Test]
        public void Rename_NewNameContainingSlashReturnsValidationError()
        {
            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "rename",
                ["assetPath"] = SourcePath,
                ["newName"] = "Nested/B"
            });

            AssertErrorType(result, "validation_error");
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
        }

        [Test]
        public void Rename_NewNameContainingDoubleDotSubstringSucceeds()
        {
            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "rename",
                ["assetPath"] = SourcePath,
                ["newName"] = "foo..bar"
            });

            Assert.AreEqual(_sourceGuid, result["guid"]?.ToString(), result.ToString());
            Assert.AreEqual(DottedRenamePath, result["assetPath"]?.ToString(), result.ToString());
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(DottedRenamePath));
        }

        [TestCase(" B")]
        [TestCase("B ")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("foo.meta")]
        [TestCase("FOO.META")]
        [TestCase("sprite.meta.")]
        [TestCase("foo.")]
        public void Rename_ProhibitedNewNameReturnsValidationError(string newName)
        {
            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "rename",
                ["assetPath"] = SourcePath,
                ["newName"] = newName
            });

            AssertErrorType(result, "validation_error");
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
        }

        [Test]
        public void InapplicableAndUnknownFieldsReturnValidationErrorWithoutMutation()
        {
            JObject inapplicable = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "copy",
                ["assetPath"] = SourcePath,
                ["destinationPath"] = CopyPath,
                ["newName"] = "IgnoredName"
            });
            JObject unknown = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "copy",
                ["assetPath"] = SourcePath,
                ["destinationPath"] = CopyPath,
                ["overwrite"] = true
            });

            AssertErrorType(inapplicable, "validation_error");
            Assert.That(
                inapplicable["error"]?["message"]?.ToString(),
                Does.Contain("newName"));
            AssertErrorType(unknown, "validation_error");
            Assert.That(unknown["error"]?["message"]?.ToString(), Does.Contain("overwrite"));
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.IsFalse(File.Exists(GetFullPath(CopyPath)));
        }

        [Test]
        public void CreateFolder_ReturnsGuidPathAndIsValidFolderReadBack()
        {
            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "create_folder",
                ["assetPath"] = CreatedFolderPath
            });

            string guid = result["guid"]?.ToString();
            Assert.IsFalse(string.IsNullOrEmpty(guid), result.ToString());
            Assert.AreEqual(CreatedFolderPath, result["assetPath"]?.ToString(), result.ToString());
            Assert.IsTrue(result["isValidFolder"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.IsTrue(AssetDatabase.IsValidFolder(CreatedFolderPath));
            Assert.AreEqual(CreatedFolderPath, AssetDatabase.GUIDToAssetPath(guid));
        }

        [Test]
        public void CreateFolder_MissingParentReturnsValidationErrorWithoutMutation()
        {
            string nestedFolder = MissingParentPath + "/Nested";

            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "create_folder",
                ["assetPath"] = nestedFolder
            });

            AssertErrorType(result, "validation_error");
            Assert.IsFalse(Directory.Exists(GetFullPath(MissingParentPath)));
            Assert.IsFalse(AssetDatabase.IsValidFolder(nestedFolder));
        }

        [Test]
        public void CreateFolder_OrphanDestinationMetaReturnsValidationErrorAndPreservesMetaBytes()
        {
            string destinationMetaPath = GetFullPath(CreatedFolderPath) + ".meta";
            byte[] orphanMeta = { 0x6f, 0x72, 0x70, 0x68, 0x61, 0x6e };
            File.WriteAllBytes(destinationMetaPath, orphanMeta);
            try
            {
                JObject result = new ManageAssetTool().Execute(new JObject
                {
                    ["action"] = "create_folder",
                    ["assetPath"] = CreatedFolderPath
                });

                AssertErrorType(result, "validation_error");
                Assert.That(
                    result["error"]?["message"]?.ToString(),
                    Does.Contain("a .meta file already exists"));
                CollectionAssert.AreEqual(orphanMeta, File.ReadAllBytes(destinationMetaPath));
                Assert.IsFalse(Directory.Exists(GetFullPath(CreatedFolderPath)));
                Assert.IsFalse(AssetDatabase.IsValidFolder(CreatedFolderPath));
            }
            finally
            {
                if (File.Exists(destinationMetaPath))
                    File.Delete(destinationMetaPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Move_ReadOnlySourceMetaReturnsExecutionErrorWithoutMutation()
        {
            string metaPath = GetFullPath(SourcePath) + ".meta";
            var metaInfo = new FileInfo(metaPath);
            bool wasReadOnly = metaInfo.IsReadOnly;
            try
            {
                metaInfo.IsReadOnly = true;

                JObject result = Execute("move", SourcePath, MovedPath);

                AssertErrorType(result, "tool_execution_error");
                Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("read-only"));
                Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
                Assert.IsFalse(File.Exists(GetFullPath(MovedPath)));
                Assert.IsFalse(File.Exists(GetFullPath(MovedPath) + ".meta"));
            }
            finally
            {
                metaInfo.Refresh();
                metaInfo.IsReadOnly = wasReadOnly;
            }
        }

        [Test]
        public void MissingSourceReturnsNotFoundError()
        {
            JObject result = Execute("copy", TestRoot + "/Missing.txt", CopyPath);

            AssertErrorType(result, "not_found_error");
            Assert.IsFalse(File.Exists(GetFullPath(CopyPath)));
        }

        [Test]
        public void ErrorResponseUsesHouseErrorObjectShape()
        {
            JObject result = new ManageAssetTool().Execute(new JObject
            {
                ["action"] = "unknown",
                ["assetPath"] = SourcePath
            });

            AssertErrorType(result, "validation_error");
            Assert.IsInstanceOf<JObject>(result["error"]);
            Assert.IsNotNull(result["error"]?["message"]);
        }

        private static JObject Execute(
            string action,
            string assetPath,
            string destinationPath)
        {
            return new ManageAssetTool().Execute(new JObject
            {
                ["action"] = action,
                ["assetPath"] = assetPath,
                ["destinationPath"] = destinationPath
            });
        }

        private static void AssertErrorType(JObject result, string expectedType)
        {
            Assert.AreEqual(
                expectedType,
                result["error"]?["type"]?.ToString(),
                result.ToString());
        }

        private static void CreateTextAsset(string assetPath, string contents)
        {
            string fullPath = GetFullPath(assetPath);
            File.WriteAllText(fullPath, contents);
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)));
        }

        private void ResetMutableFixtureState()
        {
            if (!_ownsTestRoot)
                return;

            ClearReadOnlyAttributes();
            string currentSourcePath = AssetDatabase.GUIDToAssetPath(_sourceGuid);
            if (!string.Equals(currentSourcePath, SourcePath, StringComparison.Ordinal))
            {
                Assert.IsFalse(string.IsNullOrEmpty(currentSourcePath));
                string sourceOccupantGuid = AssetDatabase.AssetPathToGUID(SourcePath);
                if (!string.IsNullOrEmpty(sourceOccupantGuid)
                    && !string.Equals(sourceOccupantGuid, _sourceGuid, StringComparison.Ordinal))
                {
                    Assert.IsTrue(AssetDatabase.DeleteAsset(SourcePath));
                }

                string restoreError = AssetDatabase.MoveAsset(currentSourcePath, SourcePath);
                Assert.IsTrue(string.IsNullOrEmpty(restoreError), restoreError);
            }

            AssetDatabase.DeleteAsset(CopyPath);
            AssetDatabase.DeleteAsset(CaseCopyPath);
            AssetDatabase.DeleteAsset(CreatedFolderPath);
            AssetDatabase.DeleteAsset(MissingParentPath);
            AssetDatabase.DeleteAsset(SourceFolderCopyPath);
            AssetDatabase.DeleteAsset(CaseVariantSourceFolderCopyPath);
            AssetDatabase.DeleteAsset(SourceFolderMovePath);
            string orphanCopyMetaPath = GetFullPath(CopyPath) + ".meta";
            if (File.Exists(orphanCopyMetaPath))
            {
                File.Delete(orphanCopyMetaPath);
                AssetDatabase.Refresh();
            }
            Assert.AreEqual(_sourceGuid, AssetDatabase.AssetPathToGUID(SourcePath));
            Assert.AreEqual(_existingGuid, AssetDatabase.AssetPathToGUID(ExistingPath));
            Assert.AreEqual(_sourceFolderGuid, AssetDatabase.AssetPathToGUID(SourceFolderPath));
            Assert.AreEqual(
                _sourceFolderChildGuid,
                AssetDatabase.AssetPathToGUID(SourceFolderChildPath));
        }

        private void ClearReadOnlyAttributes()
        {
            if (string.IsNullOrEmpty(_testRootFullPath)
                || !Directory.Exists(_testRootFullPath))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(
                         _testRootFullPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        private static string GetFullPath(string assetPath)
        {
            Assert.IsTrue(
                AssetPathUtils.TryNormalizeAssetPath(
                    assetPath,
                    out _,
                    out string fullPath,
                    out string pathError),
                pathError);
            return fullPath;
        }
    }
}
