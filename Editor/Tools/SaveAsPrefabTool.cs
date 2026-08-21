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
            Description = "Saves and connects an existing scene GameObject at an explicit .prefab path " +
                          "inside this project's Assets directory. Path validation happens before directory " +
                          "creation, and a read-only target fails without being changed.";
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

            if (!AssetPathUtils.TryNormalizeAssetPath(
                    savePath,
                    out string normalizedSavePath,
                    out string fullSavePath,
                    out string pathError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(pathError, "validation_error");
            }
            savePath = normalizedSavePath;

            if (AssetPathUtils.IsExistingFileReadOnly(fullSavePath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Cannot save prefab at '{savePath}' because the target file is read-only.",
                    "tool_execution_error");
            }

            // Find source GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject sourceObject, out string identifierInfo);
            if (error != null) return error;

            // Ensure the directory exists
            string directory = Path.GetDirectoryName(fullSavePath);
            bool targetExistedBefore = File.Exists(fullSavePath);
            bool metaExistedBefore = File.Exists(fullSavePath + ".meta");
            if (!AssetPathUtils.TryCreateOwnedDirectoryTree(
                    directory,
                    out string createdDirectoryRoot,
                    out string directoryError))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    directoryError,
                    "tool_execution_error");
            }
            try
            {
                // Save as prefab and connect the scene instance
                GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    sourceObject,
                    savePath,
                    InteractionMode.AutomatedAction
                );

                bool success = prefab != null;
                string actualSavePath = success ? AssetDatabase.GetAssetPath(prefab) : null;
                success = success && !string.IsNullOrEmpty(actualSavePath);

                if (!success)
                {
                    CleanupFailedNewAsset(
                        savePath,
                        fullSavePath,
                        targetExistedBefore,
                        metaExistedBefore,
                        createdDirectoryRoot);
                }

                string responsePath = success ? actualSavePath : savePath;
                string message = success
                    ? $"Successfully saved GameObject '{sourceObject.name}' as prefab at '{responsePath}'"
                    : $"Failed to save GameObject '{sourceObject.name}' as prefab at '{savePath}'";

                McpLogger.LogInfo(message);

                var result = new JObject
                {
                    ["success"] = success,
                    ["type"] = "text",
                    ["message"] = message,
                    ["prefabPath"] = responsePath
                };

                if (success)
                {
                    string guid = AssetDatabase.AssetPathToGUID(actualSavePath);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        result["guid"] = guid;
                    }
                }

                return result;
            }
            catch
            {
                CleanupFailedNewAsset(
                    savePath,
                    fullSavePath,
                    targetExistedBefore,
                    metaExistedBefore,
                    createdDirectoryRoot);
                throw;
            }
        }

        private static void CleanupFailedNewAsset(
            string assetPath,
            string fullPath,
            bool targetExistedBefore,
            bool metaExistedBefore,
            string createdDirectoryRoot)
        {
            bool pathWasEntirelyNew = !targetExistedBefore && !metaExistedBefore;
            if (pathWasEntirelyNew)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            if (!targetExistedBefore && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            if (!metaExistedBefore && File.Exists(fullPath + ".meta"))
            {
                File.Delete(fullPath + ".meta");
            }

            AssetPathUtils.DeleteOwnedDirectoryTree(createdDirectoryRoot);
            if (pathWasEntirelyNew)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
