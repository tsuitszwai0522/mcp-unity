using System;
using System.IO;
using McpUnity.Unity;
using McpUnity.Utils;
using UnityEngine;
using UnityEngine.U2D;
using UnityEditor;
using UnityEditor.U2D;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for importing textures as sprites by setting their TextureImporter settings
    /// </summary>
    public class ImportTextureAsSpriteTool : McpToolBase
    {
        private static Func<string, Texture2D> _loadImportedTexture =
            path => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        private static Func<string, TextureImporter> _loadPersistedImporter =
            path => AssetImporter.GetAtPath(path) as TextureImporter;
        private static Func<TextureImporter, SpriteImportMode> _readSpriteImportMode =
            importer => importer.spriteImportMode;
        private static Func<TextureImporterSettings, SpriteMeshType> _readSpriteMeshType =
            settings => settings.spriteMeshType;
        private static Func<TextureImporter, TextureImporterCompression> _readTextureCompression =
            importer => importer.textureCompression;
        private static Action<string, ImportAssetOptions> _importAsset =
            (path, options) => AssetDatabase.ImportAsset(path, options);

        public ImportTextureAsSpriteTool()
        {
            Name = "import_texture_as_sprite";
            Description = "Sets Sprite import settings for a texture at an explicit path inside this " +
                          "project's Assets directory. Invalid enum values return validation_error and " +
                          "list valid values before the importer is changed. Successful responses report " +
                          "assetPath, spriteMode, meshType, and compression read back after reimport.";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            string spriteMode = parameters["spriteMode"]?.ToObject<string>() ?? "Single";
            string meshType = parameters["meshType"]?.ToObject<string>() ?? "FullRect";
            string compression = parameters["compression"]?.ToObject<string>() ?? "None";

            // Validate required parameters
            if (string.IsNullOrEmpty(assetPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'assetPath' not provided",
                    "validation_error"
                );
            }

            if (!AssetPathUtils.TryNormalizeAssetPath(
                    assetPath,
                    out string normalizedAssetPath,
                    out string fullAssetPath,
                    out string pathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(pathError, "validation_error");
            }
            assetPath = normalizedAssetPath;

            if (!TryParseSpriteMode(spriteMode, out SpriteImportMode spriteImportMode))
            {
                return InvalidEnumResponse(
                    "spriteMode", spriteMode, "Single, Multiple");
            }
            if (!TryParseMeshType(meshType, out SpriteMeshType spriteMeshType))
            {
                return InvalidEnumResponse(
                    "meshType", meshType, "FullRect, Tight");
            }
            if (!TryParseCompression(
                    compression, out TextureImporterCompression compressionSetting))
            {
                return InvalidEnumResponse(
                    "compression",
                    compression,
                    "None, LowQuality, NormalQuality, HighQuality");
            }

            // Verify the asset exists
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (asset == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Texture asset not found at path '{assetPath}'",
                    "not_found_error"
                );
            }

            // Get the TextureImporter
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not get TextureImporter for asset '{assetPath}'",
                    "importer_error"
                );
            }

            string metaPath = fullAssetPath + ".meta";
            if (AssetPathUtils.IsExistingFileReadOnly(metaPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot update texture importer for '{assetPath}' because its meta file is read-only.",
                    "tool_execution_error");
            }

            FileSnapshot metaSnapshot;
            try
            {
                metaSnapshot = FileSnapshot.Capture(metaPath);
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not snapshot importer metadata for '{assetPath}': {ex.Message}",
                    "importer_error");
            }

            Texture2D importedAsset;
            TextureImporter persistedImporter;
            TextureImporterSettings persistedSettings;
            string actualAssetPath;
            string actualSpriteMode;
            string actualMeshType;
            string actualCompression;
            try
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = spriteImportMode;

                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = spriteMeshType;
                importer.SetTextureSettings(settings);
                importer.textureCompression = compressionSetting;
                importer.SaveAndReimport();

                importedAsset = _loadImportedTexture(assetPath);
                persistedImporter = _loadPersistedImporter(assetPath);
                if (importedAsset == null || persistedImporter == null)
                {
                    return CreateReadbackFailureWithRollback(
                        assetPath,
                        metaSnapshot,
                        $"Could not read back imported texture settings for '{assetPath}'.");
                }

                persistedSettings = new TextureImporterSettings();
                persistedImporter.ReadTextureSettings(persistedSettings);
                actualAssetPath = AssetDatabase.GetAssetPath(importedAsset);
                actualSpriteMode = ReadSpriteMode(_readSpriteImportMode(persistedImporter));
                actualMeshType = ReadMeshType(_readSpriteMeshType(persistedSettings));
                actualCompression = ReadCompression(_readTextureCompression(persistedImporter));
                if (string.IsNullOrEmpty(actualAssetPath))
                {
                    return CreateReadbackFailureWithRollback(
                        assetPath,
                        metaSnapshot,
                        $"The imported texture path for '{assetPath}' could not be read back.");
                }

                McpLogger.LogInfo(
                    $"[MCP Unity] Imported texture as sprite: '{actualAssetPath}' " +
                    $"(mode={actualSpriteMode}, mesh={actualMeshType}, " +
                    $"compression={actualCompression})");

                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] =
                        $"Successfully set texture '{actualAssetPath}' as Sprite " +
                        $"(mode={actualSpriteMode}, mesh={actualMeshType}, " +
                        $"compression={actualCompression})",
                    ["assetPath"] = actualAssetPath,
                    ["spriteMode"] = actualSpriteMode,
                    ["meshType"] = actualMeshType,
                    ["compression"] = actualCompression
                };
            }
            catch (Exception ex)
            {
                return CreateReadbackFailureWithRollback(
                    assetPath,
                    metaSnapshot,
                    $"Failed to save or read back texture importer settings for '{assetPath}': " +
                    ex.Message);
            }
        }

        private static bool TryParseSpriteMode(string value, out SpriteImportMode result)
        {
            if (string.Equals(value, "Multiple", StringComparison.OrdinalIgnoreCase))
            {
                result = SpriteImportMode.Multiple;
                return true;
            }
            if (string.Equals(value, "Single", StringComparison.OrdinalIgnoreCase))
            {
                result = SpriteImportMode.Single;
                return true;
            }
            result = default(SpriteImportMode);
            return false;
        }

        private static bool TryParseMeshType(string value, out SpriteMeshType result)
        {
            if (string.Equals(value, "Tight", StringComparison.OrdinalIgnoreCase))
            {
                result = SpriteMeshType.Tight;
                return true;
            }
            if (string.Equals(value, "FullRect", StringComparison.OrdinalIgnoreCase))
            {
                result = SpriteMeshType.FullRect;
                return true;
            }
            result = default(SpriteMeshType);
            return false;
        }

        private static bool TryParseCompression(
            string value,
            out TextureImporterCompression result)
        {
            if (string.Equals(value, "LowQuality", StringComparison.OrdinalIgnoreCase))
            {
                result = TextureImporterCompression.CompressedLQ;
                return true;
            }
            if (string.Equals(value, "NormalQuality", StringComparison.OrdinalIgnoreCase))
            {
                result = TextureImporterCompression.Compressed;
                return true;
            }
            if (string.Equals(value, "HighQuality", StringComparison.OrdinalIgnoreCase))
            {
                result = TextureImporterCompression.CompressedHQ;
                return true;
            }
            if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
            {
                result = TextureImporterCompression.Uncompressed;
                return true;
            }
            result = default(TextureImporterCompression);
            return false;
        }

        private static JObject InvalidEnumResponse(
            string field,
            string value,
            string validValues)
        {
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Enum value '{value}' is invalid for {field}. Valid values: {validValues}",
                "validation_error");
        }

        private static JObject CreateReadbackFailureWithRollback(
            string assetPath,
            FileSnapshot metaSnapshot,
            string failureMessage)
        {
            try
            {
                metaSnapshot.RestoreContents();
                try
                {
                    _importAsset(
                        assetPath,
                        ImportAssetOptions.ForceUpdate
                        | ImportAssetOptions.ForceSynchronousImport);
                }
                finally
                {
                    metaSnapshot.RestoreMetadata();
                }
            }
            catch (Exception rollbackException)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"{failureMessage} Importer rollback also failed: {rollbackException.Message}",
                    "importer_error");
            }

            return McpUnitySocketHandler.CreateErrorResponse(
                $"{failureMessage} Original importer metadata was restored.",
                "importer_error");
        }

        private static string ReadSpriteMode(SpriteImportMode value)
        {
            switch (value)
            {
                case SpriteImportMode.Single:
                    return "Single";
                case SpriteImportMode.Multiple:
                    return "Multiple";
                default:
                    return value.ToString();
            }
        }

        private static string ReadMeshType(SpriteMeshType value)
        {
            switch (value)
            {
                case SpriteMeshType.FullRect:
                    return "FullRect";
                case SpriteMeshType.Tight:
                    return "Tight";
                default:
                    return value.ToString();
            }
        }

        private static string ReadCompression(TextureImporterCompression value)
        {
            switch (value)
            {
                case TextureImporterCompression.CompressedLQ:
                    return "LowQuality";
                case TextureImporterCompression.Compressed:
                    return "NormalQuality";
                case TextureImporterCompression.CompressedHQ:
                    return "HighQuality";
                case TextureImporterCompression.Uncompressed:
                    return "None";
                default:
                    return value.ToString();
            }
        }

        private sealed class FileSnapshot
        {
            private string Path { get; set; }
            private bool Existed { get; set; }
            private byte[] Contents { get; set; }
            private FileAttributes Attributes { get; set; }
            private DateTime LastWriteTimeUtc { get; set; }

            public static FileSnapshot Capture(string path)
            {
                bool existed = File.Exists(path);
                return new FileSnapshot
                {
                    Path = path,
                    Existed = existed,
                    Contents = existed ? File.ReadAllBytes(path) : null,
                    Attributes = existed
                        ? File.GetAttributes(path)
                        : default(FileAttributes),
                    LastWriteTimeUtc = existed
                        ? File.GetLastWriteTimeUtc(path)
                        : default(DateTime)
                };
            }

            public void RestoreContents()
            {
                if (!Existed)
                {
                    if (File.Exists(Path))
                    {
                        File.Delete(Path);
                    }
                    return;
                }

                if (!File.Exists(Path))
                {
                    throw new IOException(
                        $"Importer metadata file '{Path}' disappeared during the operation.");
                }

                File.WriteAllBytes(Path, Contents);
            }

            public void RestoreMetadata()
            {
                if (!Existed || !File.Exists(Path))
                {
                    return;
                }
                File.SetLastWriteTimeUtc(Path, LastWriteTimeUtc);
                File.SetAttributes(Path, Attributes);
            }
        }
    }

    /// <summary>
    /// Tool for creating SpriteAtlas assets
    /// </summary>
    public class CreateSpriteAtlasTool : McpToolBase
    {
        private static Action<UnityEngine.Object, string> _createAsset =
            AssetDatabase.CreateAsset;
        private static Func<string, SpriteAtlas> _loadSavedAtlas =
            path => AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
        private static Func<SpriteAtlas, bool> _readIncludeInBuild =
            atlas => atlas.IsIncludeInBuild();
        private static Func<SpriteAtlas, SpriteAtlasPackingSettings> _readPackingSettings =
            atlas => atlas.GetPackingSettings();
        private static Func<SpriteAtlas, UnityEngine.Object[]> _readPackables =
            atlas => atlas.GetPackables();

        public CreateSpriteAtlasTool()
        {
            Name = "create_sprite_atlas";
            Description = "Creates a SpriteAtlas at an explicit path inside this project's Assets " +
                          "directory from an explicit Assets folder. Paths are rejected rather than " +
                          "prepended. atlasName must exactly match the savePath filename without its " +
                          ".spriteatlas or .spriteatlasv2 extension; mismatches return validation_error " +
                          "before any asset is created. Successful payload values, including folderPath, " +
                          "are read back from the saved atlas.";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string atlasName = parameters["atlasName"]?.ToObject<string>();
            string savePath = parameters["savePath"]?.ToObject<string>();
            string folderPath = parameters["folderPath"]?.ToObject<string>();
            bool includeInBuild = parameters["includeInBuild"]?.ToObject<bool>() ?? true;
            bool allowRotation = parameters["allowRotation"]?.ToObject<bool>() ?? true;
            bool tightPacking = parameters["tightPacking"]?.ToObject<bool>() ?? false;

            // Validate required parameters
            if (string.IsNullOrEmpty(atlasName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'atlasName' not provided",
                    "validation_error"
                );
            }

            if (string.IsNullOrEmpty(savePath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'savePath' not provided",
                    "validation_error"
                );
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'folderPath' not provided",
                    "validation_error"
                );
            }

            if (!AssetPathUtils.TryNormalizeAssetPath(
                    savePath, out string normalizedSavePath, out _, out string savePathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid savePath. {savePathError}", "validation_error");
            }
            savePath = normalizedSavePath;
            if (!AssetPathUtils.TryNormalizeAssetPath(
                    folderPath, out string normalizedFolderPath, out _, out string folderPathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid folderPath. {folderPathError}", "validation_error");
            }
            folderPath = normalizedFolderPath;

            // Ensure save path has .spriteatlas extension
            if (!savePath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase)
                && !savePath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
            {
                savePath += ".spriteatlas";
            }

            if (!AssetPathUtils.TryNormalizeAssetPath(
                    savePath,
                    out string normalizedSavePathWithExtension,
                    out string fullSavePath,
                    out string extendedSavePathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Invalid savePath. {extendedSavePathError}",
                    "validation_error");
            }
            savePath = normalizedSavePathWithExtension;

            string savePathAtlasName = Path.GetFileNameWithoutExtension(savePath);
            if (!string.Equals(atlasName, savePathAtlasName, StringComparison.Ordinal))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"atlasName '{atlasName}' must exactly match savePath filename " +
                    $"'{savePathAtlasName}' (without extension). No asset was created.",
                    "validation_error"
                );
            }

            // Verify the folder exists
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Folder not found at path '{folderPath}'",
                    "not_found_error"
                );
            }

            UnityEngine.Object folderObj =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (folderObj == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not load folder asset at '{folderPath}'",
                    "load_error"
                );
            }

            if (File.Exists(fullSavePath)
                || File.Exists(fullSavePath + ".meta")
                || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(savePath) != null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"A file or asset already exists at '{savePath}'. No asset was created.",
                    "asset_creation_error");
            }

            // Ensure save directory exists
            string saveDirectory = Path.GetDirectoryName(fullSavePath);
            if (!AssetPathUtils.TryCreateOwnedDirectoryTree(
                    saveDirectory,
                    out string createdDirectoryRoot,
                    out string directoryError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    directoryError,
                    "asset_creation_error");
            }
            SpriteAtlas atlas = null;
            bool completed = false;
            try
            {
                if (!string.IsNullOrEmpty(createdDirectoryRoot))
                {
                    AssetDatabase.Refresh();
                }

                atlas = new SpriteAtlas();
                SpriteAtlasPackingSettings packingSettings = new SpriteAtlasPackingSettings
                {
                    enableRotation = allowRotation,
                    enableTightPacking = tightPacking,
                    padding = 4
                };
                atlas.SetPackingSettings(packingSettings);

                SpriteAtlasTextureSettings textureSettings = new SpriteAtlasTextureSettings
                {
                    readable = false,
                    generateMipMaps = false,
                    sRGB = true,
                    filterMode = FilterMode.Bilinear
                };
                atlas.SetTextureSettings(textureSettings);
                atlas.Add(new UnityEngine.Object[] { folderObj });
                atlas.SetIncludeInBuild(includeInBuild);

                _createAsset(atlas, savePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                SpriteAtlas savedAtlas = _loadSavedAtlas(savePath);
                if (savedAtlas == null)
                {
                    string cleanupError = CleanupFailedAtlas(
                        savePath, fullSavePath, createdDirectoryRoot);
                    string cleanupMessage = string.IsNullOrEmpty(cleanupError)
                        ? "The incomplete asset was removed."
                        : $"Cleanup also failed: {cleanupError}";
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"SpriteAtlas could not be read back after creation at '{savePath}'. " +
                        cleanupMessage,
                        "asset_creation_error"
                    );
                }

                string actualSavePath = AssetDatabase.GetAssetPath(savedAtlas);
                if (string.IsNullOrEmpty(actualSavePath))
                {
                    string cleanupError = CleanupFailedAtlas(
                        savePath, fullSavePath, createdDirectoryRoot);
                    string cleanupMessage = string.IsNullOrEmpty(cleanupError)
                        ? "The incomplete asset was removed."
                        : $"Cleanup also failed: {cleanupError}";
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"SpriteAtlas path could not be read back after creation at '{savePath}'. " +
                        cleanupMessage,
                        "asset_creation_error");
                }

                UnityEngine.Object[] actualPackables = _readPackables(savedAtlas);
                string actualFolderPath = actualPackables != null && actualPackables.Length == 1
                    ? AssetDatabase.GetAssetPath(actualPackables[0])
                    : null;
                if (string.IsNullOrEmpty(actualFolderPath))
                {
                    string cleanupError = CleanupFailedAtlas(
                        savePath, fullSavePath, createdDirectoryRoot);
                    string cleanupMessage = string.IsNullOrEmpty(cleanupError)
                        ? "The incomplete asset was removed."
                        : $"Cleanup also failed: {cleanupError}";
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"SpriteAtlas packable folder could not be read back after creation " +
                        $"at '{savePath}'. {cleanupMessage}",
                        "asset_creation_error");
                }

                string actualAtlasName = savedAtlas.name;
                bool actualIncludeInBuild = _readIncludeInBuild(savedAtlas);
                SpriteAtlasPackingSettings actualPackingSettings =
                    _readPackingSettings(savedAtlas);
                bool actualAllowRotation = actualPackingSettings.enableRotation;
                bool actualTightPacking = actualPackingSettings.enableTightPacking;
                McpLogger.LogInfo(
                    $"[MCP Unity] Created SpriteAtlas '{actualAtlasName}' at " +
                    $"'{actualSavePath}' with folder '{actualFolderPath}'");

                var response = new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] =
                        $"Successfully created SpriteAtlas '{actualAtlasName}' at " +
                        $"'{actualSavePath}' including folder '{actualFolderPath}'",
                    ["atlasName"] = actualAtlasName,
                    ["savePath"] = actualSavePath,
                    ["folderPath"] = actualFolderPath,
                    ["includeInBuild"] = actualIncludeInBuild,
                    ["allowRotation"] = actualAllowRotation,
                    ["tightPacking"] = actualTightPacking
                };
                completed = true;
                return response;
            }
            catch (Exception ex)
            {
                string cleanupError = CleanupFailedAtlas(
                    savePath, fullSavePath, createdDirectoryRoot);
                string cleanupMessage = string.IsNullOrEmpty(cleanupError)
                    ? "The incomplete asset and directories were removed."
                    : $"Cleanup also failed: {cleanupError}";
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create SpriteAtlas at '{savePath}': {ex.Message} " +
                    cleanupMessage,
                    "asset_creation_error");
            }
            finally
            {
                if (!completed && atlas != null && !EditorUtility.IsPersistent(atlas))
                {
                    UnityEngine.Object.DestroyImmediate(atlas);
                }
            }
        }

        private static string CleanupFailedAtlas(
            string assetPath,
            string fullPath,
            string createdDirectoryRoot)
        {
            try
            {
                AssetDatabase.DeleteAsset(assetPath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                if (File.Exists(fullPath + ".meta"))
                {
                    File.Delete(fullPath + ".meta");
                }
                AssetPathUtils.DeleteOwnedDirectoryTree(createdDirectoryRoot);
                AssetDatabase.Refresh();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
