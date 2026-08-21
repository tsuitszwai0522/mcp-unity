using System.IO;
using McpUnity.Tools;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace McpUnity.Tests
{
    public class ImportTextureSettingsTests
    {
        private const string TestRoot = "Assets/ImportTextureSettingsTests_Temp";
        private const string TexturePath = TestRoot + "/SettingsProbe.png";
        private const string DefaultTexturePath = TestRoot + "/DefaultTypeProbe.png";

        private string _testRootFullPath;
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

            string folderGuid =
                AssetDatabase.CreateFolder("Assets", "ImportTextureSettingsTests_Temp");
            Assert.IsFalse(string.IsNullOrEmpty(folderGuid));
            _ownsTestRoot = true;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels(new[]
                {
                    Color.red,
                    Color.green,
                    Color.blue,
                    Color.white
                });
                texture.Apply();
                byte[] pngBytes = texture.EncodeToPNG();
                File.WriteAllBytes(GetFullPath(TexturePath), pngBytes);
                File.WriteAllBytes(GetFullPath(DefaultTexturePath), pngBytes);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(
                TexturePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(
                DefaultTexturePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultTexturePath));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (!_ownsTestRoot)
                return;

            bool deleted = AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
            Assert.IsTrue(deleted, $"Failed to delete owned test folder '{TestRoot}'.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(TestRoot));
            Assert.IsFalse(Directory.Exists(_testRootFullPath));
            Assert.IsFalse(File.Exists(_testRootFullPath + ".meta"));
            _ownsTestRoot = false;
        }

        [Test]
        public void WrapModeClampPersistsInFreshImporterSettings()
        {
            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["wrapMode"] = "Clamp"
            });

            Assert.AreEqual("Clamp", result["wrapMode"]?.ToString(), result.ToString());
            Assert.AreEqual("Clamp", result["wrapModeU"]?.ToString(), result.ToString());
            Assert.AreEqual("Clamp", result["wrapModeV"]?.ToString(), result.ToString());
            Assert.AreEqual("Clamp", result["wrapModeW"]?.ToString(), result.ToString());
            TextureImporterSettings persisted = ReadFreshSettings();
            Assert.AreEqual(TextureWrapMode.Clamp, persisted.wrapModeU);
            Assert.AreEqual(TextureWrapMode.Clamp, persisted.wrapModeV);
            Assert.AreEqual(TextureWrapMode.Clamp, persisted.wrapModeW);
        }

        [Test]
        public void SpriteBorderMapsLeftBottomRightTopToVector4ComponentsAndPersists()
        {
            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["spriteBorder"] = new JObject
                {
                    ["left"] = 1,
                    ["bottom"] = 2,
                    ["right"] = 3,
                    ["top"] = 4
                }
            });

            Assert.IsNotNull(result["spriteBorder"], result.ToString());
            Assert.AreEqual(new Vector4(1, 2, 3, 4), ReadFreshSettings().spriteBorder);
        }

        [Test]
        public void OmittedWrapModeOnSpriteDoesNotChangePersistedMirrorValueOrWarn()
        {
            Execute(new JObject
            {
                ["assetPath"] = TexturePath
            });

            TextureImporter importer = GetFreshImporter();
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.wrapMode = TextureWrapMode.Mirror;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath
            });

            Assert.AreEqual(TextureWrapMode.Mirror, ReadFreshSettings().wrapMode);
            Assert.IsNull(result["warnings"], result.ToString());
        }

        [Test]
        public void MixedPerAxisWrapModesAreReportedHonestlyAndRemainPersisted()
        {
            Execute(new JObject
            {
                ["assetPath"] = TexturePath
            });

            TextureImporter importer = GetFreshImporter();
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.wrapModeU = TextureWrapMode.Repeat;
            settings.wrapModeV = TextureWrapMode.Clamp;
            settings.wrapModeW = TextureWrapMode.Repeat;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath
            });

            Assert.AreEqual("Mixed", result["wrapMode"]?.ToString(), result.ToString());
            Assert.AreEqual("Repeat", result["wrapModeU"]?.ToString(), result.ToString());
            Assert.AreEqual("Clamp", result["wrapModeV"]?.ToString(), result.ToString());
            Assert.AreEqual("Repeat", result["wrapModeW"]?.ToString(), result.ToString());
            TextureImporterSettings persisted = ReadFreshSettings();
            Assert.AreEqual(TextureWrapMode.Repeat, persisted.wrapModeU);
            Assert.AreEqual(TextureWrapMode.Clamp, persisted.wrapModeV);
            Assert.AreEqual(TextureWrapMode.Repeat, persisted.wrapModeW);
        }

        [Test]
        public void TextureTypeConversionResetsOmittedWrapAndWritesDefaultCompressionWithWarning()
        {
            const string expectedWarning =
                "textureType changed from Default to Sprite; Unity reset other importer settings " +
                "to Sprite defaults. This call then wrote spriteMode/meshType/compression (tool " +
                "defaults when omitted) and any provided wrapMode/spriteBorder; all other settings " +
                "remain at the Sprite defaults.";
            TextureImporter defaultImporter = GetFreshImporter(DefaultTexturePath);
            defaultImporter.textureType = TextureImporterType.Default;
            var defaultSettings = new TextureImporterSettings();
            defaultImporter.ReadTextureSettings(defaultSettings);
            defaultSettings.wrapMode = TextureWrapMode.Mirror;
            defaultImporter.SetTextureSettings(defaultSettings);
            defaultImporter.textureCompression = TextureImporterCompression.CompressedHQ;
            defaultImporter.SaveAndReimport();
            Assert.AreEqual(
                TextureImporterType.Default,
                GetFreshImporter(DefaultTexturePath).textureType);
            Assert.AreEqual(
                TextureWrapMode.Mirror,
                ReadFreshSettings(DefaultTexturePath).wrapMode);
            Assert.AreEqual(
                TextureImporterCompression.CompressedHQ,
                GetFreshImporter(DefaultTexturePath).textureCompression);

            JObject result = Execute(new JObject
            {
                ["assetPath"] = DefaultTexturePath
            });

            JArray warnings = result["warnings"] as JArray;
            Assert.IsNotNull(warnings, result.ToString());
            Assert.That(warnings.Values<string>(), Contains.Item(expectedWarning));
            Assert.That(result["message"]?.ToString(), Does.Contain(expectedWarning));
            Assert.AreEqual(
                TextureImporterType.Sprite,
                GetFreshImporter(DefaultTexturePath).textureType);
            Assert.AreNotEqual(
                TextureWrapMode.Mirror,
                ReadFreshSettings(DefaultTexturePath).wrapMode);
            Assert.AreEqual("None", result["compression"]?.ToString(), result.ToString());
            Assert.AreEqual(
                TextureImporterCompression.Uncompressed,
                GetFreshImporter(DefaultTexturePath).textureCompression);
        }

        [Test]
        public void InvalidWrapModeReturnsValidationErrorListsValuesAndLeavesImporterUnchanged()
        {
            string metaPath = GetFullPath(TexturePath) + ".meta";
            byte[] metaBefore = File.ReadAllBytes(metaPath);
            TextureWrapMode wrapBefore = ReadFreshSettings().wrapMode;

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["wrapMode"] = "PingPong"
            });

            AssertErrorType(result, "validation_error");
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("Repeat, Clamp, Mirror, MirrorOnce"));
            CollectionAssert.AreEqual(metaBefore, File.ReadAllBytes(metaPath));
            Assert.AreEqual(wrapBefore, ReadFreshSettings().wrapMode);
        }

        [Test]
        public void SpriteBorderUnknownKeyReturnsValidationErrorAndListsLegalKeys()
        {
            string metaPath = GetFullPath(TexturePath) + ".meta";
            byte[] metaBefore = File.ReadAllBytes(metaPath);

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["spriteBorder"] = new JObject
                {
                    ["left"] = 1,
                    ["bottom"] = 2,
                    ["right"] = 3,
                    ["top"] = 4,
                    ["center"] = 5
                }
            });

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("center"));
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("left, bottom, right, top"));
            CollectionAssert.AreEqual(metaBefore, File.ReadAllBytes(metaPath));
        }

        [Test]
        public void SpriteBorderMissingKeyReturnsValidationErrorWithoutImporterMutation()
        {
            string metaPath = GetFullPath(TexturePath) + ".meta";
            byte[] metaBefore = File.ReadAllBytes(metaPath);

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["spriteBorder"] = new JObject
                {
                    ["left"] = 1,
                    ["bottom"] = 2,
                    ["right"] = 3
                }
            });

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("top"));
            CollectionAssert.AreEqual(metaBefore, File.ReadAllBytes(metaPath));
        }

        [Test]
        public void MultipleSpriteModeWithBorderReturnsValidationErrorWithoutImporterMutation()
        {
            string metaPath = GetFullPath(TexturePath) + ".meta";
            byte[] metaBefore = File.ReadAllBytes(metaPath);

            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath,
                ["spriteMode"] = "Multiple",
                ["spriteBorder"] = new JObject
                {
                    ["left"] = 1,
                    ["bottom"] = 2,
                    ["right"] = 3,
                    ["top"] = 4
                }
            });

            AssertErrorType(result, "validation_error");
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("Single"));
            Assert.That(result["error"]?["message"]?.ToString(), Does.Contain("Multiple"));
            Assert.That(
                result["error"]?["message"]?.ToString(),
                Does.Contain("each sprite"));
            CollectionAssert.AreEqual(metaBefore, File.ReadAllBytes(metaPath));
        }

        [Test]
        public void OmittedNewKeysStillReturnPersistedWrapModeAndSpriteBorder()
        {
            JObject result = Execute(new JObject
            {
                ["assetPath"] = TexturePath
            });
            TextureImporterSettings persisted = ReadFreshSettings();

            string expectedWrapMode = persisted.wrapModeU == persisted.wrapModeV
                && persisted.wrapModeU == persisted.wrapModeW
                    ? persisted.wrapModeU.ToString()
                    : "Mixed";
            Assert.AreEqual(expectedWrapMode, result["wrapMode"]?.ToString());
            Assert.AreEqual(persisted.wrapModeU.ToString(), result["wrapModeU"]?.ToString());
            Assert.AreEqual(persisted.wrapModeV.ToString(), result["wrapModeV"]?.ToString());
            Assert.AreEqual(persisted.wrapModeW.ToString(), result["wrapModeW"]?.ToString());
            JObject border = result["spriteBorder"] as JObject;
            Assert.IsNotNull(border, result.ToString());
            Assert.AreEqual(persisted.spriteBorder.x, border["left"]?.ToObject<float>());
            Assert.AreEqual(persisted.spriteBorder.y, border["bottom"]?.ToObject<float>());
            Assert.AreEqual(persisted.spriteBorder.z, border["right"]?.ToObject<float>());
            Assert.AreEqual(persisted.spriteBorder.w, border["top"]?.ToObject<float>());
        }

        private static JObject Execute(JObject parameters)
        {
            return new ImportTextureAsSpriteTool().Execute(parameters);
        }

        private static TextureImporter GetFreshImporter(string assetPath = TexturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.IsNotNull(importer);
            return importer;
        }

        private static TextureImporterSettings ReadFreshSettings(string assetPath = TexturePath)
        {
            TextureImporter importer = GetFreshImporter(assetPath);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            return settings;
        }

        private static void AssertErrorType(JObject result, string expectedType)
        {
            Assert.AreEqual(
                expectedType,
                result["error"]?["type"]?.ToString(),
                result.ToString());
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
