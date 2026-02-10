using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for saving an existing scene GameObject as a Prefab asset
    /// </summary>
    public class SaveAsPrefabTool : McpToolBase
    {
        public SaveAsPrefabTool()
        {
            Name = "save_as_prefab";
            Description = "Saves an existing GameObject from the scene as a Prefab asset using PrefabUtility.SaveAsPrefabAssetAndConnect";
        }

        /// <summary>
        /// Execute the SaveAsPrefab tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string savePath = parameters["savePath"]?.ToObject<string>();

            // Validate required parameters
            if (string.IsNullOrEmpty(savePath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'savePath' not provided",
                    "validation_error"
                );
            }

            if (!savePath.EndsWith(".prefab"))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'savePath' must end with '.prefab'",
                    "validation_error"
                );
            }

            // Find source GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject sourceObject, out string identifierInfo);
            if (error != null) return error;

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save as prefab and connect the scene instance
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                sourceObject,
                savePath,
                InteractionMode.AutomatedAction
            );

            bool success = prefab != null;

            // Refresh the asset database
            AssetDatabase.Refresh();

            string message = success
                ? $"Successfully saved GameObject '{sourceObject.name}' as prefab at '{savePath}'"
                : $"Failed to save GameObject '{sourceObject.name}' as prefab at '{savePath}'";

            McpLogger.LogInfo(message);

            var result = new JObject
            {
                ["success"] = success,
                ["type"] = "text",
                ["message"] = message,
                ["prefabPath"] = savePath
            };

            if (success)
            {
                string guid = AssetDatabase.AssetPathToGUID(savePath);
                if (!string.IsNullOrEmpty(guid))
                {
                    result["guid"] = guid;
                }
            }

            return result;
        }
    }
}
