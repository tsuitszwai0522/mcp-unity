using System.IO;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Bootstrap Addressables for a project that has never been initialized.
    /// Equivalent to the "Create Addressables Settings" button in the Groups window.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrInitSettingsTool : McpToolBase
    {
        private const string DefaultConfigFolder = "Assets/AddressableAssetsData";
        private const string ConfigName = "AddressableAssetSettings";

        public AddrInitSettingsTool()
        {
            Name = "addr_init_settings";
            Description = "Initialize Unity Addressables (creates default settings asset and group). Safe to call when already initialized";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""folder"": { ""type"": ""string"", ""description"": ""Settings folder path (default Assets/AddressableAssetsData)"" }
            }
        }");

        public override JObject Execute(JObject parameters)
        {
            // Validate input up-front so bad folder params surface as validation_error
            // even on the idempotent path (where we wouldn't otherwise touch the folder).
            string folder = parameters["folder"]?.ToString();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = DefaultConfigFolder;
            }
            folder = folder.Replace('\\', '/').TrimEnd('/');

            // Guard against path traversal and writes outside the project Assets
            // folder — agents can pass arbitrary strings, and Directory.CreateDirectory
            // will happily create folders anywhere on disk otherwise.
            if (folder != "Assets" && !folder.StartsWith("Assets/"))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter 'folder' must start with 'Assets/' (got '{folder}')",
                    "validation_error");
            }
            if (folder.Contains("../") || folder.Contains("/..") || folder == "..")
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Parameter 'folder' must not contain parent traversal ('..') (got '{folder}')",
                    "validation_error");
            }

            var existing = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (existing != null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = "Addressables already initialized",
                    ["created"] = false,
                    ["settingsPath"] = AssetDatabase.GetAssetPath(existing),
                    ["defaultGroup"] = existing.DefaultGroup?.Name
                };
            }

            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            var settings = AddressableAssetSettings.Create(folder, ConfigName, true, true);
            if (settings == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to create AddressableAssetSettings at '{folder}'",
                    "create_failed");
            }

            AddressableAssetSettingsDefaultObject.Settings = settings;
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Addressables initialized at '{folder}'. Default group: '{settings.DefaultGroup?.Name}'",
                ["created"] = true,
                ["settingsPath"] = AssetDatabase.GetAssetPath(settings),
                ["defaultGroup"] = settings.DefaultGroup?.Name
            };
        }
    }
}
