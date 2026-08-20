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
        public ImportTextureAsSpriteTool()
        {
            Name = "import_texture_as_sprite";
            Description = "Sets a texture's import settings to Sprite type with configurable sprite mode, mesh type, and compression";
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

            // Ensure path starts with Assets/
            if (!assetPath.StartsWith("Assets/"))
            {
                assetPath = "Assets/" + assetPath;
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

            // Set texture type to Sprite
            importer.textureType = TextureImporterType.Sprite;

            // Set sprite import mode
            switch (spriteMode.ToLower())
            {
                case "multiple":
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    break;
                case "single":
                default:
                    importer.spriteImportMode = SpriteImportMode.Single;
                    break;
            }

            // Set mesh type via TextureImporterSettings
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            switch (meshType.ToLower())
            {
                case "tight":
                    settings.spriteMeshType = SpriteMeshType.Tight;
                    break;
                case "fullrect":
                default:
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    break;
            }
            importer.SetTextureSettings(settings);

            // Set compression
            TextureImporterCompression compressionSetting;
            switch (compression.ToLower())
            {
                case "lowquality":
                    compressionSetting = TextureImporterCompression.CompressedLQ;
                    break;
                case "normalquality":
                    compressionSetting = TextureImporterCompression.Compressed;
                    break;
                case "highquality":
                    compressionSetting = TextureImporterCompression.CompressedHQ;
                    break;
                case "none":
                default:
                    compressionSetting = TextureImporterCompression.Uncompressed;
                    break;
            }
            importer.textureCompression = compressionSetting;

            // Save and reimport
            importer.SaveAndReimport();

            McpLogger.LogInfo($"[MCP Unity] Imported texture as sprite: '{assetPath}' (mode={spriteMode}, mesh={meshType}, compression={compression})");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully set texture '{assetPath}' as Sprite (mode={spriteMode}, mesh={meshType}, compression={compression})",
                ["assetPath"] = assetPath,
                ["spriteMode"] = spriteMode,
                ["meshType"] = meshType,
                ["compression"] = compression
            };
        }
    }

    /// <summary>
    /// Tool for creating SpriteAtlas assets
    /// </summary>
    public class CreateSpriteAtlasTool : McpToolBase
    {
        private static Func<SpriteAtlas, bool> _readIncludeInBuild =
            atlas => atlas.IsIncludeInBuild();
        private static Func<SpriteAtlas, SpriteAtlasPackingSettings> _readPackingSettings =
            atlas => atlas.GetPackingSettings();

        public CreateSpriteAtlasTool()
        {
            Name = "create_sprite_atlas";
            Description = "Creates a SpriteAtlas asset that packs sprites from a specified folder. atlasName must exactly match the savePath filename without its .spriteatlas or .spriteatlasv2 extension; mismatches return validation_error before any asset is created.";
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

            // Ensure paths start with Assets/
            if (!savePath.StartsWith("Assets/"))
            {
                savePath = "Assets/" + savePath;
            }
            if (!folderPath.StartsWith("Assets/"))
            {
                folderPath = "Assets/" + folderPath;
            }

            // Ensure save path has .spriteatlas extension
            if (!savePath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase)
                && !savePath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
            {
                savePath += ".spriteatlas";
            }

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

            // Ensure save directory exists
            string saveDirectory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(saveDirectory) && !Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
                AssetDatabase.Refresh();
            }

            // Create the SpriteAtlas
            SpriteAtlas atlas = new SpriteAtlas();

            // Set packing settings
            SpriteAtlasPackingSettings packingSettings = new SpriteAtlasPackingSettings
            {
                enableRotation = allowRotation,
                enableTightPacking = tightPacking,
                padding = 4
            };
            atlas.SetPackingSettings(packingSettings);

            // Set texture settings (defaults)
            SpriteAtlasTextureSettings textureSettings = new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = false,
                sRGB = true,
                filterMode = FilterMode.Bilinear
            };
            atlas.SetTextureSettings(textureSettings);

            // Add the folder as a packable object
            UnityEngine.Object folderObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
            if (folderObj == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Could not load folder asset at '{folderPath}'",
                    "load_error"
                );
            }
            atlas.Add(new UnityEngine.Object[] { folderObj });

            // Set include in build
            atlas.SetIncludeInBuild(includeInBuild);

            // Save the atlas asset
            AssetDatabase.CreateAsset(atlas, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SpriteAtlas savedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(savePath);
            if (savedAtlas == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"SpriteAtlas could not be read back after creation at '{savePath}'",
                    "asset_creation_error"
                );
            }

            string actualAtlasName = savedAtlas.name;
            bool actualIncludeInBuild = _readIncludeInBuild(savedAtlas);
            SpriteAtlasPackingSettings actualPackingSettings = _readPackingSettings(savedAtlas);
            bool actualAllowRotation = actualPackingSettings.enableRotation;
            bool actualTightPacking = actualPackingSettings.enableTightPacking;
            McpLogger.LogInfo($"[MCP Unity] Created SpriteAtlas '{actualAtlasName}' at '{savePath}' with folder '{folderPath}'");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created SpriteAtlas '{actualAtlasName}' at '{savePath}' including folder '{folderPath}'",
                ["atlasName"] = actualAtlasName,
                ["savePath"] = savePath,
                ["folderPath"] = folderPath,
                ["includeInBuild"] = actualIncludeInBuild,
                ["allowRotation"] = actualAllowRotation,
                ["tightPacking"] = actualTightPacking
            };
        }
    }
}
