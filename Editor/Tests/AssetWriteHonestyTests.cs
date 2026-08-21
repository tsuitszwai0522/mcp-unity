using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using McpUnity.Services;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;

namespace McpUnity.Tests
{
    public class AssetWriteHonestyTests
    {
        private const string OnePixelPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";

        private static readonly Func<GameObject, Type, Component> OriginalAddComponent =
            GetUpdateComponentField<Func<GameObject, Type, Component>>("_addComponent");
        private static readonly Action<UnityEngine.Object> OriginalSetDirty =
            GetUpdateComponentField<Action<UnityEngine.Object>>("_setDirty");
        private static readonly AssetPathNormalizer OriginalNormalizeUniquePrefabPath =
            GetPrivateStaticField<AssetPathNormalizer>(
                typeof(CreatePrefabTool), "_normalizeUniquePrefabPath");
        private static readonly Func<GameObject, string, bool> OriginalSavePrefabContents =
            GetPrivateStaticField<Func<GameObject, string, bool>>(
                typeof(PrefabEditingService), "_savePrefabContents");
        private static readonly Func<string, Texture2D> OriginalLoadImportedTexture =
            GetPrivateStaticField<Func<string, Texture2D>>(
                typeof(ImportTextureAsSpriteTool), "_loadImportedTexture");
        private static readonly Func<string, TextureImporter> OriginalLoadPersistedImporter =
            GetPrivateStaticField<Func<string, TextureImporter>>(
                typeof(ImportTextureAsSpriteTool), "_loadPersistedImporter");
        private static readonly Func<TextureImporter, SpriteImportMode> OriginalReadSpriteImportMode =
            GetPrivateStaticField<Func<TextureImporter, SpriteImportMode>>(
                typeof(ImportTextureAsSpriteTool), "_readSpriteImportMode");
        private static readonly Func<TextureImporterSettings, SpriteMeshType> OriginalReadSpriteMeshType =
            GetPrivateStaticField<Func<TextureImporterSettings, SpriteMeshType>>(
                typeof(ImportTextureAsSpriteTool), "_readSpriteMeshType");
        private static readonly Func<TextureImporter, TextureImporterCompression> OriginalReadTextureCompression =
            GetPrivateStaticField<Func<TextureImporter, TextureImporterCompression>>(
                typeof(ImportTextureAsSpriteTool), "_readTextureCompression");
        private static readonly Action<string, ImportAssetOptions> OriginalImportAsset =
            GetPrivateStaticField<Action<string, ImportAssetOptions>>(
                typeof(ImportTextureAsSpriteTool), "_importAsset");
        private static readonly Action<UnityEngine.Object, string> OriginalCreateAtlasAsset =
            GetPrivateStaticField<Action<UnityEngine.Object, string>>(
                typeof(CreateSpriteAtlasTool), "_createAsset");
        private static readonly Func<string, SpriteAtlas> OriginalLoadSavedAtlas =
            GetPrivateStaticField<Func<string, SpriteAtlas>>(
                typeof(CreateSpriteAtlasTool), "_loadSavedAtlas");
        private static readonly Func<SpriteAtlas, UnityEngine.Object[]> OriginalReadAtlasPackables =
            GetPrivateStaticField<Func<SpriteAtlas, UnityEngine.Object[]>>(
                typeof(CreateSpriteAtlasTool), "_readPackables");

        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly Dictionary<string, FileAttributes> _originalAttributes =
            new Dictionary<string, FileAttributes>(StringComparer.Ordinal);
        private readonly HashSet<string> _externalArtifactRoots =
            new HashSet<string>(StringComparer.Ordinal);

        private string _testRoot;
        private string _testRootFullPath;
        private string _outsideToken;
        private bool _openedPrefabSession;

        [SetUp]
        public void SetUp()
        {
            Assert.AreEqual(
                PrefabEditingSessionStatus.None,
                PrefabEditingService.Status,
                "The fixture must not discard a Prefab session owned by another test.");

            string suffix = Guid.NewGuid().ToString("N");
            _testRoot = $"Assets/McpUnityAssetWriteHonestyTests_{suffix}";
            _outsideToken = $"McpUnityAssetWriteEscape_{suffix}";
            string folderGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(_testRoot));
            Assert.IsFalse(string.IsNullOrEmpty(folderGuid), "Test asset root creation must succeed.");
            Assert.IsTrue(AssetPathUtils.TryNormalizeAssetPath(
                _testRoot, out _, out _testRootFullPath, out string pathError), pathError);

            RestoreProductionSeams();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreProductionSeams();

            foreach (KeyValuePair<string, FileAttributes> item in _originalAttributes)
            {
                if (File.Exists(item.Key))
                {
                    File.SetAttributes(item.Key, item.Value);
                }
            }

            if (_openedPrefabSession && PrefabEditingService.Status == PrefabEditingSessionStatus.Active)
            {
                PrefabEditingService.Discard();
            }
            _openedPrefabSession = false;

            foreach (GameObject gameObject in _createdObjects.Where(item => item != null))
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            _createdObjects.Clear();

            foreach (GameObject gameObject in UnityEngine.Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject != null
                    && !EditorUtility.IsPersistent(gameObject)
                    && gameObject.name.StartsWith(_testRoot, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            AssetDatabase.DeleteAsset(_testRoot);
            if (!string.IsNullOrEmpty(_testRootFullPath) && Directory.Exists(_testRootFullPath))
            {
                Directory.Delete(_testRootFullPath, true);
            }
            if (!string.IsNullOrEmpty(_testRootFullPath)
                && File.Exists(_testRootFullPath + ".meta"))
            {
                File.Delete(_testRootFullPath + ".meta");
            }

            foreach (string root in _externalArtifactRoots.OrderByDescending(path => path.Length))
            {
                DeleteArtifactRoot(root);
            }
            _externalArtifactRoots.Clear();
            _originalAttributes.Clear();
            AssetDatabase.Refresh();
        }

        [Test]
        public void L1_CreatePrefab_RejectsEveryOutsidePathWithoutDiskWrites_AndAllowsAssetsPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string parentEscapeStem = $"../{_outsideToken}/ParentEscape";
            string deepEscapeStem = $"Assets/../../{_outsideToken}/DeepEscape";
            string absoluteStem = Path.Combine(_testRootFullPath, "AbsoluteEscape");
            string bareStem = $"{_outsideToken}/BareEscape";

            AssertCreatePrefabValidationFailure(
                parentEscapeStem,
                Path.GetFullPath(Path.Combine(projectRoot, parentEscapeStem + ".prefab")));
            AssertCreatePrefabValidationFailure(
                deepEscapeStem,
                Path.GetFullPath(Path.Combine(projectRoot, deepEscapeStem + ".prefab")));
            AssertCreatePrefabValidationFailure(absoluteStem, absoluteStem + ".prefab");
            AssertCreatePrefabValidationFailure(
                bareStem,
                Path.GetFullPath(Path.Combine(projectRoot, bareStem + ".prefab")));

            string validStem = _testRoot + "/PositivePrefab";
            JObject first = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = validStem
            });
            JObject second = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = validStem
            });

            Assert.IsTrue(first["success"]?.ToObject<bool>() ?? false, first.ToString());
            Assert.AreEqual(validStem + ".prefab", first["prefabPath"]?.ToString());
            Assert.IsTrue(second["success"]?.ToObject<bool>() ?? false, second.ToString());
            Assert.AreEqual(validStem + "_1.prefab", second["prefabPath"]?.ToString());
            Assert.IsTrue(File.Exists(GetFullPath(validStem + ".prefab")));
            Assert.IsTrue(File.Exists(GetFullPath(validStem + "_1.prefab")));
        }

        [Test]
        public void L1_SaveAsPrefab_RejectsEveryOutsidePathBeforeDirectoryCreation_AndAllowsAssetsPath()
        {
            GameObject source = Track(new GameObject(_testRoot + "/SaveSource"));
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string parentEscape = $"../{_outsideToken}/ParentEscape.prefab";
            string deepEscape = $"Assets/../../{_outsideToken}/DeepEscape.prefab";
            string absolute = Path.Combine(_testRootFullPath, "AbsoluteEscape.prefab");
            string bare = $"{_outsideToken}/BareEscape.prefab";

            AssertSaveAsValidationFailure(
                source, parentEscape, Path.GetFullPath(Path.Combine(projectRoot, parentEscape)));
            AssertSaveAsValidationFailure(
                source, deepEscape, Path.GetFullPath(Path.Combine(projectRoot, deepEscape)));
            AssertSaveAsValidationFailure(source, absolute, absolute);
            AssertSaveAsValidationFailure(
                source, bare, Path.GetFullPath(Path.Combine(projectRoot, bare)));

            string validPath = _testRoot + "/Created/Nested/Positive.prefab";
            JObject result = new SaveAsPrefabTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["savePath"] = validPath
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(validPath, result["prefabPath"]?.ToString());
            Assert.IsTrue(File.Exists(GetFullPath(validPath)));
        }

        [Test]
        public void L1_SpriteTools_RejectExplicitlyInvalidPaths_AndAtlasAllowsAssetsPaths()
        {
            string packablesFolder = _testRoot + "/Sprites";
            AssetDatabase.CreateFolder(_testRoot, "Sprites");

            string[] invalidTexturePaths =
            {
                $"../{_outsideToken}/ParentTexture.png",
                $"Assets/../../{_outsideToken}/DeepTexture.png",
                Path.Combine(_testRootFullPath, "AbsoluteTexture.png"),
                $"{_outsideToken}/BareTexture.png"
            };
            foreach (string invalidTexturePath in invalidTexturePaths)
            {
                string expectedFullPath = ResolveAgainstProject(invalidTexturePath);
                TrackExternalTarget(expectedFullPath);
                string expectedDirectory = Path.GetDirectoryName(expectedFullPath);
                bool directoryExistedBefore = Directory.Exists(expectedDirectory);
                JObject importResult = new ImportTextureAsSpriteTool().Execute(new JObject
                {
                    ["assetPath"] = invalidTexturePath
                });
                AssertValidationError(importResult);
                Assert.IsFalse(File.Exists(expectedFullPath));
                Assert.AreEqual(directoryExistedBefore, Directory.Exists(expectedDirectory));
            }

            var invalidAtlasSavePaths = new[]
            {
                new { Name = "ParentAtlas", Path = $"../{_outsideToken}/ParentAtlas.spriteatlas" },
                new { Name = "DeepAtlas", Path = $"Assets/../../{_outsideToken}/DeepAtlas.spriteatlas" },
                new { Name = "AbsoluteAtlas", Path = Path.Combine(_testRootFullPath, "AbsoluteAtlas.spriteatlas") },
                new { Name = "BareAtlas", Path = $"{_outsideToken}/BareAtlas.spriteatlas" }
            };
            foreach (var invalidSave in invalidAtlasSavePaths)
            {
                string expectedFullPath = ResolveAgainstProject(invalidSave.Path);
                TrackExternalTarget(expectedFullPath);
                string expectedDirectory = Path.GetDirectoryName(expectedFullPath);
                bool directoryExistedBefore = Directory.Exists(expectedDirectory);
                JObject result = new CreateSpriteAtlasTool().Execute(new JObject
                {
                    ["atlasName"] = invalidSave.Name,
                    ["savePath"] = invalidSave.Path,
                    ["folderPath"] = packablesFolder
                });
                AssertValidationError(result);
                Assert.IsFalse(File.Exists(expectedFullPath));
                Assert.AreEqual(directoryExistedBefore, Directory.Exists(expectedDirectory));
            }

            string[] invalidFolderPaths =
            {
                $"../{_outsideToken}",
                $"Assets/../../{_outsideToken}",
                GetFullPath(packablesFolder),
                "Sprites"
            };
            for (int i = 0; i < invalidFolderPaths.Length; i++)
            {
                string atlasName = "UnusedAtlas" + i;
                string unusedAtlasPath = _testRoot + "/" + atlasName + ".spriteatlas";
                JObject result = new CreateSpriteAtlasTool().Execute(new JObject
                {
                    ["atlasName"] = atlasName,
                    ["savePath"] = unusedAtlasPath,
                    ["folderPath"] = invalidFolderPaths[i]
                });
                AssertValidationError(result);
                Assert.IsFalse(File.Exists(GetFullPath(unusedAtlasPath)));
            }

            string validAtlasPath = _testRoot + "/PositiveAtlas.spriteatlas";
            JObject validResult = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "PositiveAtlas",
                ["savePath"] = validAtlasPath,
                ["folderPath"] = packablesFolder
            });
            Assert.IsTrue(validResult["success"]?.ToObject<bool>() ?? false, validResult.ToString());
            Assert.IsTrue(File.Exists(GetFullPath(validAtlasPath)));
            SpriteAtlas persistedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(validAtlasPath);
            Assert.IsNotNull(persistedAtlas);
            UnityEngine.Object[] persistedPackables = persistedAtlas.GetPackables();
            Assert.AreEqual(1, persistedPackables.Length);
            Assert.AreEqual(
                AssetDatabase.GetAssetPath(persistedPackables[0]),
                validResult["folderPath"]?.ToString());
        }

        [Test]
        public void L1_AtlasExtensionRenormalizationFailureIsValidationErrorWithoutProjectRootWrite()
        {
            string packablesFolder = _testRoot + "/RenormalizeSprites";
            AssetDatabase.CreateFolder(_testRoot, "RenormalizeSprites");
            string projectRootAtlasPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Assets.spriteatlas");
            string projectRootAtlasMetaPath = projectRootAtlasPath + ".meta";
            DiskFileState atlasBefore = DiskFileState.Capture(projectRootAtlasPath);
            DiskFileState metaBefore = DiskFileState.Capture(projectRootAtlasMetaPath);

            foreach (string savePath in new[] { "Assets", "Assets/foo/..", "Assets/." })
            {
                JObject result = new CreateSpriteAtlasTool().Execute(new JObject
                {
                    ["atlasName"] = "Assets",
                    ["savePath"] = savePath,
                    ["folderPath"] = packablesFolder
                });

                AssertValidationError(result);
                atlasBefore.AssertUnchanged(projectRootAtlasPath);
                metaBefore.AssertUnchanged(projectRootAtlasMetaPath);
            }
        }

        [Test]
        public void L1_CreatePrefabUniquePathRenormalizationFailureIsVisibleAndCleansTemporaryRoot()
        {
            string prefabStem = _testRoot + "/InjectedUniqueNormalization";
            int rootCountBefore = SceneManager.GetActiveScene().rootCount;
            SetPrivateStaticField(
                typeof(CreatePrefabTool),
                "_normalizeUniquePrefabPath",
                new AssetPathNormalizer((
                    string assetPath,
                    out string normalizedAssetPath,
                    out string fullPath,
                    out string errorMessage) =>
                {
                    normalizedAssetPath = null;
                    fullPath = null;
                    errorMessage = $"Injected normalization failure for '{assetPath}'.";
                    return false;
                }));

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabStem
            });

            AssertValidationError(result);
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("Injected"));
            Assert.AreEqual(rootCountBefore, SceneManager.GetActiveScene().rootCount);
            Assert.IsFalse(File.Exists(GetFullPath(prefabStem + ".prefab")));
        }

        [Test]
        public void L2_CreatePrefab_ReadOnlyRawTargetFailsWithoutChangingContentsOrAttributes()
        {
            string prefabPath = _testRoot + "/ReadOnlyRaw.prefab";
            string fullPath = GetFullPath(prefabPath);
            byte[] originalContents = { 0x50, 0x52, 0x45, 0x46, 0x41, 0x42 };
            File.WriteAllBytes(fullPath, originalContents);
            FileAttributes readOnlyAttributes = MakeReadOnly(fullPath);

            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabPath.Substring(0, prefabPath.Length - ".prefab".Length)
            });

            Assert.AreEqual("tool_execution_error", result["error"]?["type"]?.ToString(), result.ToString());
            CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(fullPath));
            Assert.AreEqual(readOnlyAttributes, File.GetAttributes(fullPath));
            Assert.IsFalse(File.Exists(fullPath + ".meta"));
        }

        [Test]
        public void L2_SaveAsPrefab_ReadOnlyTargetFailsWithoutChangingContentsOrAttributes()
        {
            string prefabPath = CreatePrefabAsset("SaveAsReadOnly");
            string fullPath = GetFullPath(prefabPath);
            byte[] originalContents = File.ReadAllBytes(fullPath);
            FileAttributes readOnlyAttributes = MakeReadOnly(fullPath);
            GameObject source = Track(new GameObject(_testRoot + "/SaveAsReadOnlySource"));

            JObject result = new SaveAsPrefabTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["savePath"] = prefabPath
            });

            Assert.AreEqual("tool_execution_error", result["error"]?["type"]?.ToString(), result.ToString());
            CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(fullPath));
            Assert.AreEqual(readOnlyAttributes, File.GetAttributes(fullPath));
        }

        [Test]
        public void L2_SaveAsPrefab_FileInDirectoryPositionPreservesUnownedAssetMetadata()
        {
            string texturePath = CreateTextureAsset("SaveAsDirectoryCollision.png");
            string fullTexturePath = GetFullPath(texturePath);
            string metaPath = fullTexturePath + ".meta";
            DiskFileState textureBefore = DiskFileState.Capture(fullTexturePath);
            DiskFileState metaBefore = DiskFileState.Capture(metaPath);
            GameObject source = Track(new GameObject(_testRoot + "/SaveAsDirectoryCollisionSource"));

            string prefabPath = texturePath + "/Foo.prefab";
            JObject prefabResult = new SaveAsPrefabTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["savePath"] = prefabPath
            });

            Assert.AreEqual(
                "tool_execution_error",
                prefabResult["error"]?["type"]?.ToString(),
                prefabResult.ToString());
            Assert.That(
                prefabResult["error"]?["message"]?.ToString(),
                Does.Contain("existing file"));
            textureBefore.AssertUnchanged(fullTexturePath);
            metaBefore.AssertUnchanged(metaPath);
            Assert.IsFalse(File.Exists(GetFullPath(prefabPath)));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        }

        [Test]
        public void L2_Atlas_FileInDirectoryPositionPreservesUnownedAssetMetadata()
        {
            string texturePath = CreateTextureAsset("AtlasDirectoryCollision.png");
            string fullTexturePath = GetFullPath(texturePath);
            string metaPath = fullTexturePath + ".meta";
            DiskFileState textureBefore = DiskFileState.Capture(fullTexturePath);
            DiskFileState metaBefore = DiskFileState.Capture(metaPath);
            string packablesFolder = _testRoot + "/DirectoryCollisionSprites";
            AssetDatabase.CreateFolder(_testRoot, "DirectoryCollisionSprites");
            string atlasPath = texturePath + "/MyAtlas.spriteatlas";
            JObject atlasResult = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "MyAtlas",
                ["savePath"] = atlasPath,
                ["folderPath"] = packablesFolder
            });

            Assert.AreEqual(
                "asset_creation_error",
                atlasResult["error"]?["type"]?.ToString(),
                atlasResult.ToString());
            Assert.That(
                atlasResult["error"]?["message"]?.ToString(),
                Does.Contain("existing file"));
            textureBefore.AssertUnchanged(fullTexturePath);
            metaBefore.AssertUnchanged(metaPath);
            Assert.IsFalse(File.Exists(GetFullPath(atlasPath)));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        }

        [Test]
        public void L2_SavePrefabContents_ReadOnlyTargetFailsWithoutChangingContentsOrAttributes()
        {
            string prefabPath = CreatePrefabAsset("ContentsReadOnly");
            GameObject prefabRoot = PrefabEditingService.Open(prefabPath);
            _openedPrefabSession = true;
            prefabRoot.name = "UnsavedReadOnlyChange";
            string fullPath = GetFullPath(prefabPath);
            byte[] originalContents = File.ReadAllBytes(fullPath);
            FileAttributes readOnlyAttributes = MakeReadOnly(fullPath);

            JObject result = new SavePrefabContentsTool().Execute(new JObject());

            Assert.AreEqual(
                "tool_execution_error",
                result["error"]?["type"]?.ToString(),
                result.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("read-only"));
            Assert.IsTrue(PrefabEditingService.IsEditing, "The failed save must leave edits open.");
            CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(fullPath));
            Assert.AreEqual(readOnlyAttributes, File.GetAttributes(fullPath));
        }

        [Test]
        public void L2_CreatePrefab_SaveThrowStillRemovesTemporaryRootAndCreatesNothing()
        {
            string missingStem = _testRoot + "/MissingDirectory/Nested/ThrowProbe";
            string missingDirectory = GetFullPath(_testRoot + "/MissingDirectory");
            int rootCountBefore = SceneManager.GetActiveScene().rootCount;

            Assert.Throws<ArgumentException>(() => new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = missingStem
            }));

            Assert.AreEqual(rootCountBefore, SceneManager.GetActiveScene().rootCount);
            Assert.IsFalse(Directory.Exists(missingDirectory));
            Assert.IsFalse(File.Exists(GetFullPath(missingStem + ".prefab")));
        }

        [Test]
        public void L1_SavePrefabContentsDelegate_NormalizationFailureFailsClosed()
        {
            GameObject root = Track(new GameObject(_testRoot + "/FailClosedRoot"));
            string invalidPath = $"../{_outsideToken}/FailClosed.prefab";
            string expectedFullPath = ResolveAgainstProject(invalidPath);
            TrackExternalTarget(expectedFullPath);

            InvalidOperationException exception =
                Assert.Catch<InvalidOperationException>(() =>
                    OriginalSavePrefabContents(root, invalidPath));

            Assert.That(exception.Message, Does.Contain("Cannot save Prefab"));
            Assert.That(exception.Message, Does.Contain("Assets"));
            Assert.IsFalse(File.Exists(expectedFullPath));
        }

        [Test]
        public void L2_UpdateComponent_AddReturnsNullReportsFailureWithoutSetDirty()
        {
            GameObject target = Track(new GameObject(_testRoot + "/NullAddTarget"));
            int dirtyCalls = 0;
            SetUpdateComponentField(
                "_addComponent",
                new Func<GameObject, Type, Component>((_, __) => null));
            SetUpdateComponentField(
                "_setDirty",
                new Action<UnityEngine.Object>(_ => dirtyCalls++));

            JObject result = new UpdateComponentTool().Execute(new JObject
            {
                ["instanceId"] = target.GetInstanceID(),
                ["componentName"] = typeof(AssetWriteHonestyProbeComponent).FullName
            });

            Assert.IsFalse(result["success"]?.ToObject<bool>() ?? true, result.ToString());
            Assert.AreEqual(0, dirtyCalls, "SetDirty must not run when no component was added.");
            Assert.IsNull(target.GetComponent<AssetWriteHonestyProbeComponent>());
            Assert.AreEqual("componentName", result["failedFields"]?[0]?["field"]?.ToString());
        }

        [Test]
        public void L2_InvalidSpriteEnumsFailBeforeImporterMutationAndListValidValues()
        {
            string texturePath = CreateTextureAsset("UnknownEnums.png");
            string metaPath = GetFullPath(texturePath) + ".meta";
            DiskFileState metaBefore = DiskFileState.Capture(metaPath);
            TextureImporter importerBefore = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(importerBefore);
            TextureImporterType textureTypeBefore = importerBefore.textureType;
            SpriteImportMode spriteModeBefore = importerBefore.spriteImportMode;
            TextureImporterCompression compressionBefore = importerBefore.textureCompression;
            var settingsBefore = new TextureImporterSettings();
            importerBefore.ReadTextureSettings(settingsBefore);
            SpriteMeshType meshTypeBefore = settingsBefore.spriteMeshType;

            var invalidCases = new[]
            {
                new { Field = "spriteMode", Value = "BogusMode", Valid = "Single, Multiple" },
                new { Field = "meshType", Value = "BogusMesh", Valid = "FullRect, Tight" },
                new
                {
                    Field = "compression",
                    Value = "BogusCompression",
                    Valid = "None, LowQuality, NormalQuality, HighQuality"
                }
            };

            foreach (var invalidCase in invalidCases)
            {
                var parameters = new JObject
                {
                    ["assetPath"] = texturePath,
                    ["spriteMode"] = "Single",
                    ["meshType"] = "FullRect",
                    ["compression"] = "None"
                };
                parameters[invalidCase.Field] = invalidCase.Value;

                JObject result = new ImportTextureAsSpriteTool().Execute(parameters);

                AssertValidationError(result);
                Assert.That(
                    result["error"]?["message"]?.ToString(),
                    Does.Contain(invalidCase.Value).And.Contain(invalidCase.Valid));
                metaBefore.AssertUnchanged(metaPath);
            }

            TextureImporter importerAfter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(importerAfter);
            var settingsAfter = new TextureImporterSettings();
            importerAfter.ReadTextureSettings(settingsAfter);
            Assert.AreEqual(textureTypeBefore, importerAfter.textureType);
            Assert.AreEqual(spriteModeBefore, importerAfter.spriteImportMode);
            Assert.AreEqual(meshTypeBefore, settingsAfter.spriteMeshType);
            Assert.AreEqual(compressionBefore, importerAfter.textureCompression);
        }

        [Test]
        public void L3_SpriteResponseUsesInjectedReadbackValuesInsteadOfRequestValues()
        {
            string texturePath = CreateTextureAsset("DifferentialReadback.png");
            TextureImporter loadedPersistedImporter = null;
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_loadPersistedImporter",
                new Func<string, TextureImporter>(path =>
                {
                    loadedPersistedImporter = OriginalLoadPersistedImporter(path);
                    return loadedPersistedImporter;
                }));
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readSpriteImportMode",
                new Func<TextureImporter, SpriteImportMode>(importer =>
                {
                    Assert.AreSame(
                        loadedPersistedImporter,
                        importer,
                        "The response reader must receive the importer returned by the persisted loader.");
                    return SpriteImportMode.Multiple;
                }));
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readSpriteMeshType",
                new Func<TextureImporterSettings, SpriteMeshType>(_ => SpriteMeshType.Tight));
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readTextureCompression",
                new Func<TextureImporter, TextureImporterCompression>(
                    _ => TextureImporterCompression.CompressedHQ));

            JObject result = new ImportTextureAsSpriteTool().Execute(new JObject
            {
                ["assetPath"] = texturePath,
                ["spriteMode"] = "Single",
                ["meshType"] = "FullRect",
                ["compression"] = "None"
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(texturePath, result["assetPath"]?.ToString());
            Assert.AreEqual("Multiple", result["spriteMode"]?.ToString());
            Assert.AreEqual("Tight", result["meshType"]?.ToString());
            Assert.AreEqual("HighQuality", result["compression"]?.ToString());
            Assert.AreNotEqual("Single", result["spriteMode"]?.ToString());
            Assert.AreNotEqual("FullRect", result["meshType"]?.ToString());
            Assert.AreNotEqual("None", result["compression"]?.ToString());
        }

        [Test]
        public void L3_SpriteResponseMatchesForceReloadedPersistedImporterRoundTrip()
        {
            string texturePath = CreateTextureAsset("PersistedRoundTrip.png");

            JObject result = new ImportTextureAsSpriteTool().Execute(new JObject
            {
                ["assetPath"] = texturePath,
                ["spriteMode"] = "Single",
                ["meshType"] = "FullRect",
                ["compression"] = "None"
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            AssetDatabase.ImportAsset(
                texturePath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Texture2D persistedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            TextureImporter persistedImporter =
                AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(persistedTexture);
            Assert.IsNotNull(persistedImporter);
            var persistedSettings = new TextureImporterSettings();
            persistedImporter.ReadTextureSettings(persistedSettings);

            Assert.AreEqual(
                AssetDatabase.GetAssetPath(persistedTexture),
                result["assetPath"]?.ToString());
            Assert.AreEqual(SpriteImportMode.Single, persistedImporter.spriteImportMode);
            Assert.AreEqual("Single", result["spriteMode"]?.ToString());
            Assert.AreEqual(SpriteMeshType.FullRect, persistedSettings.spriteMeshType);
            Assert.AreEqual("FullRect", result["meshType"]?.ToString());
            Assert.AreEqual(
                TextureImporterCompression.Uncompressed,
                persistedImporter.textureCompression);
            Assert.AreEqual("None", result["compression"]?.ToString());
        }

        [Test]
        public void L2_ImporterReadbackFailureRestoresPersistedMetadataAndSettings()
        {
            string texturePath = CreateTextureAsset("RollbackImporter.png");
            string metaPath = GetFullPath(texturePath) + ".meta";
            TextureImporter importerBefore = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(importerBefore);
            importerBefore.textureType = TextureImporterType.Default;
            importerBefore.spriteImportMode = SpriteImportMode.Single;
            importerBefore.textureCompression = TextureImporterCompression.Uncompressed;
            var baselineSettings = new TextureImporterSettings();
            importerBefore.ReadTextureSettings(baselineSettings);
            baselineSettings.spriteMeshType = SpriteMeshType.FullRect;
            importerBefore.SetTextureSettings(baselineSettings);
            importerBefore.SaveAndReimport();

            importerBefore = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(importerBefore);
            TextureImporterType textureTypeBefore = importerBefore.textureType;
            SpriteImportMode spriteModeBefore = importerBefore.spriteImportMode;
            TextureImporterCompression compressionBefore = importerBefore.textureCompression;
            var settingsBefore = new TextureImporterSettings();
            importerBefore.ReadTextureSettings(settingsBefore);
            SpriteMeshType meshTypeBefore = settingsBefore.spriteMeshType;
            Assert.AreEqual(TextureImporterType.Default, textureTypeBefore);
            Assert.AreEqual(SpriteImportMode.Single, spriteModeBefore);
            Assert.AreEqual(SpriteMeshType.FullRect, meshTypeBefore);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, compressionBefore);
            DiskFileState metaBefore = DiskFileState.Capture(metaPath);

            int rollbackImportCount = 0;
            ImportAssetOptions rollbackImportOptions = default(ImportAssetOptions);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_importAsset",
                new Action<string, ImportAssetOptions>((path, options) =>
                {
                    rollbackImportCount++;
                    rollbackImportOptions = options;
                    AssetDatabase.ImportAsset(path, options);
                }));

            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_loadPersistedImporter",
                new Func<string, TextureImporter>(_ => null));

            JObject result = new ImportTextureAsSpriteTool().Execute(new JObject
            {
                ["assetPath"] = texturePath,
                ["spriteMode"] = "Multiple",
                ["meshType"] = "Tight",
                ["compression"] = "HighQuality"
            });

            Assert.AreEqual("importer_error", result["error"]?["type"]?.ToString(), result.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("restored"));
            Assert.AreEqual(1, rollbackImportCount, "Rollback must force one synchronous reimport.");
            Assert.AreEqual(
                ImportAssetOptions.ForceUpdate,
                rollbackImportOptions & ImportAssetOptions.ForceUpdate);
            Assert.AreEqual(
                ImportAssetOptions.ForceSynchronousImport,
                rollbackImportOptions & ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importerAfter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            Assert.IsNotNull(importerAfter);
            var settingsAfter = new TextureImporterSettings();
            importerAfter.ReadTextureSettings(settingsAfter);
            Assert.AreEqual(textureTypeBefore, importerAfter.textureType);
            Assert.AreEqual(spriteModeBefore, importerAfter.spriteImportMode);
            Assert.AreEqual(meshTypeBefore, settingsAfter.spriteMeshType);
            Assert.AreEqual(compressionBefore, importerAfter.textureCompression);
            Assert.AreNotEqual(SpriteImportMode.Multiple, importerAfter.spriteImportMode);
            Assert.AreNotEqual(SpriteMeshType.Tight, settingsAfter.spriteMeshType);
            Assert.AreNotEqual(TextureImporterType.Sprite, importerAfter.textureType);
            Assert.AreNotEqual(
                TextureImporterCompression.CompressedHQ,
                importerAfter.textureCompression);
            metaBefore.AssertUnchanged(metaPath);
        }

        [Test]
        public void L2_AtlasCreateAssetThrowRemovesPartialFileDirectoriesAndTransientObject()
        {
            string packablesFolder = _testRoot + "/AtlasRollbackSprites";
            AssetDatabase.CreateFolder(_testRoot, "AtlasRollbackSprites");
            string atlasPath =
                _testRoot + "/AtlasFailure/Nested/InjectedFailure.spriteatlas";
            string fullAtlasPath = GetFullPath(atlasPath);
            string createdDirectoryRoot = GetFullPath(_testRoot + "/AtlasFailure");
            int transientAtlasCountBefore = CountTransientSpriteAtlases();

            SetPrivateStaticField(
                typeof(CreateSpriteAtlasTool),
                "_createAsset",
                new Action<UnityEngine.Object, string>((_, path) =>
                {
                    File.WriteAllBytes(GetFullPath(path), new byte[] { 0x50, 0x41, 0x52, 0x54 });
                    throw new InvalidOperationException("Injected CreateAsset failure");
                }));

            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "InjectedFailure",
                ["savePath"] = atlasPath,
                ["folderPath"] = packablesFolder
            });

            Assert.AreEqual(
                "asset_creation_error",
                result["error"]?["type"]?.ToString(),
                result.ToString());
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("removed"));
            Assert.IsFalse(File.Exists(fullAtlasPath));
            Assert.IsFalse(File.Exists(fullAtlasPath + ".meta"));
            Assert.IsFalse(Directory.Exists(createdDirectoryRoot));
            Assert.IsFalse(File.Exists(createdDirectoryRoot + ".meta"));
            Assert.AreEqual(transientAtlasCountBefore, CountTransientSpriteAtlases());
        }

        [Test]
        public void L3_AtlasFolderPathUsesPersistedPackablesReaderInsteadOfRequest()
        {
            string requestedFolder = _testRoot + "/RequestedPackables";
            string readbackFolder = _testRoot + "/ReadbackPackables";
            AssetDatabase.CreateFolder(_testRoot, "RequestedPackables");
            AssetDatabase.CreateFolder(_testRoot, "ReadbackPackables");
            UnityEngine.Object readbackFolderObject =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(readbackFolder);
            Assert.IsNotNull(readbackFolderObject);
            SetPrivateStaticField(
                typeof(CreateSpriteAtlasTool),
                "_readPackables",
                new Func<SpriteAtlas, UnityEngine.Object[]>(
                    _ => new[] { readbackFolderObject }));

            string atlasPath = _testRoot + "/PackablesReadback.spriteatlas";
            JObject result = new CreateSpriteAtlasTool().Execute(new JObject
            {
                ["atlasName"] = "PackablesReadback",
                ["savePath"] = atlasPath,
                ["folderPath"] = requestedFolder
            });

            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            Assert.AreEqual(readbackFolder, result["folderPath"]?.ToString());
            Assert.AreNotEqual(requestedFolder, result["folderPath"]?.ToString());
        }

        [Test]
        public void FixturePngValidatorRejectsCrcValidButInvalidZlibPayload()
        {
            byte[] corruptedPng = Convert.FromBase64String(OnePixelPngBase64);
            int offset = 8;
            bool mutatedIdat = false;
            while (offset < corruptedPng.Length)
            {
                int dataLength = (int)ReadUInt32BigEndian(corruptedPng, offset);
                string chunkType = Encoding.ASCII.GetString(corruptedPng, offset + 4, 4);
                if (chunkType == "IDAT")
                {
                    corruptedPng[offset + 8 + dataLength - 1] ^= 0x01;
                    uint repairedChunkCrc =
                        ComputePngCrc(corruptedPng, offset + 4, dataLength + 4);
                    WriteUInt32BigEndian(
                        corruptedPng,
                        offset + 8 + dataLength,
                        repairedChunkCrc);
                    mutatedIdat = true;
                    break;
                }
                offset += 12 + dataLength;
            }

            Assert.IsTrue(mutatedIdat, "The embedded fixture must contain an IDAT chunk.");
            Assert.IsFalse(
                TryValidatePngBytes(corruptedPng, out string error),
                "CRC-valid but zlib-invalid IDAT data must not pass fixture validation.");
            StringAssert.Contains("Adler-32", error);
        }

        [Test]
        public void UnityToolDescriptionsDiscloseAssetWriteContracts()
        {
            string createDescription = new CreatePrefabTool().Description;
            StringAssert.Contains("Assets directory", createDescription);
            StringAssert.Contains("_1, _2", createDescription);
            StringAssert.Contains("read-only", createDescription);

            string saveAsDescription = new SaveAsPrefabTool().Description;
            StringAssert.Contains("before directory", saveAsDescription);
            StringAssert.Contains("read-only", saveAsDescription);

            string importDescription = new ImportTextureAsSpriteTool().Description;
            StringAssert.Contains("validation_error", importDescription);
            StringAssert.Contains("valid values", importDescription);
            StringAssert.Contains("read back", importDescription);

            string atlasDescription = new CreateSpriteAtlasTool().Description;
            StringAssert.Contains("rejected rather than", atlasDescription);
            StringAssert.Contains("folderPath", atlasDescription);
            StringAssert.Contains("read back", atlasDescription);

            string contentsDescription = new SavePrefabContentsTool().Description;
            StringAssert.Contains("read-only", contentsDescription);

            string updateDescription = new UpdateComponentTool().Description;
            StringAssert.Contains("success=false", updateDescription);
            StringAssert.Contains("without marking", updateDescription);
        }

        private void AssertCreatePrefabValidationFailure(string prefabStem, string expectedFullPath)
        {
            TrackExternalTarget(expectedFullPath);
            string expectedDirectory = Path.GetDirectoryName(expectedFullPath);
            bool directoryExistedBefore = Directory.Exists(expectedDirectory);
            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabStem
            });
            AssertValidationError(result);
            Assert.IsFalse(File.Exists(expectedFullPath));
            Assert.AreEqual(directoryExistedBefore, Directory.Exists(expectedDirectory));
        }

        private void AssertSaveAsValidationFailure(
            GameObject source,
            string savePath,
            string expectedFullPath)
        {
            TrackExternalTarget(expectedFullPath);
            string expectedDirectory = Path.GetDirectoryName(expectedFullPath);
            bool directoryExistedBefore = Directory.Exists(expectedDirectory);
            JObject result = new SaveAsPrefabTool().Execute(new JObject
            {
                ["instanceId"] = source.GetInstanceID(),
                ["savePath"] = savePath
            });
            AssertValidationError(result);
            Assert.IsFalse(File.Exists(expectedFullPath));
            Assert.AreEqual(directoryExistedBefore, Directory.Exists(expectedDirectory));
        }

        private static void AssertValidationError(JObject result)
        {
            Assert.AreEqual("validation_error", result["error"]?["type"]?.ToString(), result.ToString());
        }

        private string CreatePrefabAsset(string name)
        {
            string prefabStem = _testRoot + "/" + name;
            JObject result = new CreatePrefabTool().Execute(new JObject
            {
                ["prefabName"] = prefabStem
            });
            Assert.IsTrue(result["success"]?.ToObject<bool>() ?? false, result.ToString());
            return result["prefabPath"].ToString();
        }

        private string CreateTextureAsset(string fileName)
        {
            string assetPath = _testRoot + "/" + fileName;
            byte[] pngBytes = DecodeAndValidateEmbeddedPng();
            File.WriteAllBytes(GetFullPath(assetPath), pngBytes);
            AssetDatabase.ImportAsset(
                assetPath, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath),
                $"Embedded PNG fixture passed Base64, PNG structure, chunk CRC, and IDAT " +
                $"zlib-inflate validation, but Unity failed to import '{assetPath}' as a Texture2D. " +
                "Inspect the Unity Console for the importer error.");
            return assetPath;
        }

        private static byte[] DecodeAndValidateEmbeddedPng()
        {
            byte[] pngBytes;
            try
            {
                pngBytes = Convert.FromBase64String(OnePixelPngBase64);
            }
            catch (FormatException ex)
            {
                Assert.Fail(
                    $"Embedded PNG fixture constant is not valid Base64 before Unity import: " +
                    ex.Message);
                return null;
            }

            Assert.IsTrue(
                TryValidatePngBytes(pngBytes, out string validationError),
                $"Embedded PNG fixture constant is invalid before Unity import: {validationError}");
            return pngBytes;
        }

        private static bool TryValidatePngBytes(byte[] bytes, out string error)
        {
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            if (bytes == null || bytes.Length < signature.Length + 12)
            {
                error = "the decoded byte array is null or too short to contain a PNG.";
                return false;
            }

            for (int i = 0; i < signature.Length; i++)
            {
                if (bytes[i] != signature[i])
                {
                    error = $"the PNG signature differs at byte {i}.";
                    return false;
                }
            }

            bool sawHeader = false;
            bool sawImageData = false;
            bool sawEnd = false;
            var compressedImageData = new List<byte>();
            int offset = signature.Length;
            int chunkIndex = 0;
            while (offset < bytes.Length)
            {
                if (bytes.Length - offset < 12)
                {
                    error = $"chunk {chunkIndex} is truncated before its length/type/data/CRC fields.";
                    return false;
                }

                uint rawDataLength = ReadUInt32BigEndian(bytes, offset);
                if (rawDataLength > int.MaxValue)
                {
                    error = $"chunk {chunkIndex} declares an unsupported length {rawDataLength}.";
                    return false;
                }

                int dataLength = (int)rawDataLength;
                long chunkEnd = (long)offset + 12L + dataLength;
                if (chunkEnd > bytes.Length)
                {
                    error = $"chunk {chunkIndex} extends beyond the decoded byte array.";
                    return false;
                }

                string chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                if (chunkIndex == 0 && chunkType != "IHDR")
                {
                    error = $"the first chunk is '{chunkType}', not IHDR.";
                    return false;
                }

                uint storedCrc = ReadUInt32BigEndian(bytes, offset + 8 + dataLength);
                uint computedCrc = ComputePngCrc(bytes, offset + 4, dataLength + 4);
                if (storedCrc != computedCrc)
                {
                    error = $"chunk '{chunkType}' CRC mismatch: stored 0x{storedCrc:X8}, " +
                        $"computed 0x{computedCrc:X8}.";
                    return false;
                }

                if (chunkType == "IHDR")
                {
                    if (sawHeader || dataLength != 13)
                    {
                        error = "IHDR must appear once with exactly 13 data bytes.";
                        return false;
                    }
                    sawHeader = true;
                }
                else if (chunkType == "IDAT")
                {
                    sawImageData = true;
                    for (int i = 0; i < dataLength; i++)
                    {
                        compressedImageData.Add(bytes[offset + 8 + i]);
                    }
                }
                else if (chunkType == "IEND")
                {
                    if (dataLength != 0)
                    {
                        error = "IEND must have zero data bytes.";
                        return false;
                    }
                    sawEnd = true;
                    offset = (int)chunkEnd;
                    if (offset != bytes.Length)
                    {
                        error = "trailing bytes remain after IEND.";
                        return false;
                    }
                    break;
                }

                offset = (int)chunkEnd;
                chunkIndex++;
            }

            if (!sawHeader || !sawImageData || !sawEnd)
            {
                error = "the PNG must contain IHDR, at least one IDAT, and IEND.";
                return false;
            }

            if (!TryInflatePngImageData(compressedImageData.ToArray(), out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryInflatePngImageData(byte[] zlibData, out string error)
        {
            if (zlibData == null || zlibData.Length < 7)
            {
                error = "the concatenated IDAT zlib stream is too short.";
                return false;
            }

            int compressionMethod = zlibData[0] & 0x0F;
            int windowInfo = zlibData[0] >> 4;
            int header = (zlibData[0] << 8) | zlibData[1];
            if (compressionMethod != 8 || windowInfo > 7 || header % 31 != 0)
            {
                error = "the concatenated IDAT data has an invalid zlib header.";
                return false;
            }
            if ((zlibData[1] & 0x20) != 0)
            {
                error = "the concatenated IDAT zlib stream requires an unsupported preset dictionary.";
                return false;
            }

            int deflateLength = zlibData.Length - 6;
            var rawDeflate = new byte[deflateLength];
            Buffer.BlockCopy(zlibData, 2, rawDeflate, 0, deflateLength);
            byte[] inflatedBytes;
            try
            {
                using (var input = new MemoryStream(rawDeflate))
                using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[256];
                    int read;
                    while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > 1024 * 1024)
                        {
                            error = "the inflated IDAT fixture exceeds the 1 MiB safety limit.";
                            return false;
                        }
                        output.Write(buffer, 0, read);
                    }
                    inflatedBytes = output.ToArray();
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                error = $"the concatenated IDAT DEFLATE payload could not be inflated: {ex.Message}";
                return false;
            }

            if (inflatedBytes.Length == 0)
            {
                error = "the concatenated IDAT stream inflated to zero bytes.";
                return false;
            }

            uint storedAdler = ReadUInt32BigEndian(zlibData, zlibData.Length - 4);
            uint computedAdler = ComputeAdler32(inflatedBytes);
            if (storedAdler != computedAdler)
            {
                error = $"the inflated IDAT Adler-32 mismatch: stored 0x{storedAdler:X8}, " +
                    $"computed 0x{computedAdler:X8}.";
                return false;
            }

            error = null;
            return true;
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static void WriteUInt32BigEndian(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        private static uint ComputePngCrc(byte[] bytes, int offset, int count)
        {
            uint crc = 0xFFFFFFFFU;
            for (int i = 0; i < count; i++)
            {
                crc ^= bytes[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1U) != 0
                        ? 0xEDB88320U ^ (crc >> 1)
                        : crc >> 1;
                }
            }
            return crc ^ 0xFFFFFFFFU;
        }

        private static uint ComputeAdler32(byte[] bytes)
        {
            const uint Modulus = 65521U;
            uint a = 1U;
            uint b = 0U;
            for (int i = 0; i < bytes.Length; i++)
            {
                a = (a + bytes[i]) % Modulus;
                b = (b + a) % Modulus;
            }
            return (b << 16) | a;
        }

        private static int CountTransientSpriteAtlases()
        {
            return UnityEngine.Resources.FindObjectsOfTypeAll<SpriteAtlas>()
                .Count(atlas => atlas != null && !EditorUtility.IsPersistent(atlas));
        }

        private FileAttributes MakeReadOnly(string fullPath)
        {
            FileAttributes original = File.GetAttributes(fullPath);
            if (!_originalAttributes.ContainsKey(fullPath))
            {
                _originalAttributes.Add(fullPath, original);
            }
            FileAttributes readOnly = original | FileAttributes.ReadOnly;
            File.SetAttributes(fullPath, readOnly);
            return File.GetAttributes(fullPath);
        }

        private string GetFullPath(string assetPath)
        {
            Assert.IsTrue(AssetPathUtils.TryNormalizeAssetPath(
                assetPath, out _, out string fullPath, out string error), error);
            return fullPath;
        }

        private static string ResolveAgainstProject(string path)
        {
            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    path.Replace('/', Path.DirectorySeparatorChar)));
        }

        private GameObject Track(GameObject gameObject)
        {
            if (gameObject != null && !_createdObjects.Contains(gameObject))
            {
                _createdObjects.Add(gameObject);
            }
            return gameObject;
        }

        private void TrackExternalTarget(string fullTargetPath)
        {
            string directory = Path.GetDirectoryName(fullTargetPath);
            if (!string.IsNullOrEmpty(directory)
                && !directory.StartsWith(_testRootFullPath, StringComparison.Ordinal))
            {
                _externalArtifactRoots.Add(directory);
            }
        }

        private static void DeleteArtifactRoot(string root)
        {
            if (File.Exists(root))
            {
                File.SetAttributes(root, File.GetAttributes(root) & ~FileAttributes.ReadOnly);
                File.Delete(root);
            }
            if (Directory.Exists(root))
            {
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                }
                Directory.Delete(root, true);
            }
            if (File.Exists(root + ".meta"))
            {
                File.SetAttributes(
                    root + ".meta",
                    File.GetAttributes(root + ".meta") & ~FileAttributes.ReadOnly);
                File.Delete(root + ".meta");
            }
        }

        private static T GetUpdateComponentField<T>(string name)
        {
            FieldInfo field = typeof(UpdateComponentTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new MissingFieldException(typeof(UpdateComponentTool).FullName, name);
            }
            return (T)field.GetValue(null);
        }

        private static void SetUpdateComponentField(string name, object value)
        {
            FieldInfo field = typeof(UpdateComponentTool).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                Assert.Fail($"UpdateComponentTool private field '{name}' was not found.");
            }
            field.SetValue(null, value);
        }

        private static void RestoreUpdateComponentDelegates()
        {
            SetUpdateComponentField("_addComponent", OriginalAddComponent);
            SetUpdateComponentField("_setDirty", OriginalSetDirty);
        }

        private static T GetPrivateStaticField<T>(Type ownerType, string name)
        {
            FieldInfo field = ownerType.GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                throw new MissingFieldException(ownerType.FullName, name);
            }
            return (T)field.GetValue(null);
        }

        private static void SetPrivateStaticField(
            Type ownerType,
            string name,
            object value)
        {
            FieldInfo field = ownerType.GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                Assert.Fail($"{ownerType.Name} private field '{name}' was not found.");
            }
            field.SetValue(null, value);
        }

        private static void RestoreProductionSeams()
        {
            RestoreUpdateComponentDelegates();
            SetPrivateStaticField(
                typeof(CreatePrefabTool),
                "_normalizeUniquePrefabPath",
                OriginalNormalizeUniquePrefabPath);
            SetPrivateStaticField(
                typeof(PrefabEditingService),
                "_savePrefabContents",
                OriginalSavePrefabContents);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_loadImportedTexture",
                OriginalLoadImportedTexture);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_loadPersistedImporter",
                OriginalLoadPersistedImporter);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readSpriteImportMode",
                OriginalReadSpriteImportMode);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readSpriteMeshType",
                OriginalReadSpriteMeshType);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_readTextureCompression",
                OriginalReadTextureCompression);
            SetPrivateStaticField(
                typeof(ImportTextureAsSpriteTool),
                "_importAsset",
                OriginalImportAsset);
            SetPrivateStaticField(
                typeof(CreateSpriteAtlasTool),
                "_createAsset",
                OriginalCreateAtlasAsset);
            SetPrivateStaticField(
                typeof(CreateSpriteAtlasTool),
                "_loadSavedAtlas",
                OriginalLoadSavedAtlas);
            SetPrivateStaticField(
                typeof(CreateSpriteAtlasTool),
                "_readPackables",
                OriginalReadAtlasPackables);
        }

        private sealed class DiskFileState
        {
            private bool Existed { get; set; }
            private byte[] Contents { get; set; }
            private FileAttributes Attributes { get; set; }

            public static DiskFileState Capture(string path)
            {
                bool existed = File.Exists(path);
                return new DiskFileState
                {
                    Existed = existed,
                    Contents = existed ? File.ReadAllBytes(path) : null,
                    Attributes = existed
                        ? File.GetAttributes(path)
                        : default(FileAttributes)
                };
            }

            public void AssertUnchanged(string path)
            {
                Assert.AreEqual(Existed, File.Exists(path), path);
                if (!Existed)
                {
                    return;
                }

                CollectionAssert.AreEqual(Contents, File.ReadAllBytes(path), path);
                Assert.AreEqual(Attributes, File.GetAttributes(path), path);
            }
        }
    }

    public class AssetWriteHonestyProbeComponent : MonoBehaviour
    {
    }
}
