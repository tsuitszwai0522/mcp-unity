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

            string folder = parameters["folder"]?.ToString();
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = DefaultConfigFolder;
            }
            folder = folder.TrimEnd('/');

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
