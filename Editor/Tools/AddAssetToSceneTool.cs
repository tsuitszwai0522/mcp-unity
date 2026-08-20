using System;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using McpUnity.Unity;
using McpUnity.Utils;
using McpUnity.Services;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for adding assets from the AssetDatabase to the Unity scene
    /// </summary>
    public class AddAssetToSceneTool : McpToolBase
    {
        private static Action<UnityEngine.Object> _pingObject = EditorGUIUtility.PingObject;

        public AddAssetToSceneTool()
        {
            Name = "add_asset_to_scene";
            Description = "Instantiates an AssetDatabase prefab in the active loaded-scene or open-Prefab context";
        }
        
        /// <summary>
        /// Execute the AddAssetToScene tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string assetPath = parameters["assetPath"]?.ToObject<string>();
            string guid = parameters["guid"]?.ToObject<string>();
            Vector3 position = parameters["position"]?.ToObject<JObject>() != null 
                ? new Vector3(
                    parameters["position"]["x"]?.ToObject<float>() ?? 0f,
                    parameters["position"]["y"]?.ToObject<float>() ?? 0f,
                    parameters["position"]["z"]?.ToObject<float>() ?? 0f
                ) 
                : Vector3.zero;
            
            // Optional parent game object
            string parentPath = parameters["parentPath"]?.ToObject<string>();
            int? parentId = parameters["parentId"]?.ToObject<int?>();
            
            // Validate parameters - require either assetPath or guid
            if (string.IsNullOrEmpty(assetPath) && string.IsNullOrEmpty(guid))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'assetPath' or 'guid' not provided", 
                    "validation_error"
                );
            }
            
            // If we have a GUID but no path, convert GUID to path
            if (string.IsNullOrEmpty(assetPath) && !string.IsNullOrEmpty(guid))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Asset with GUID '{guid}' not found", 
                        "not_found_error"
                    );
                }
            }
            
            // Load the asset
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to load asset at path '{assetPath}'", 
                    "not_found_error"
                );
            }
            
            // Check if the asset is a prefab or another type that can be instantiated
            bool isPrefab = PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.NotAPrefab;
            bool canInstantiate = asset is GameObject || isPrefab;
            
            if (!canInstantiate)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Asset of type '{asset.GetType().Name}' cannot be instantiated in the scene", 
                    "invalid_asset_type"
                );
            }
            
            JObject scopeError = PrefabSessionScope.TryGetPrefabRoot(out GameObject prefabRoot);
            if (scopeError != null) return scopeError;

            GameObject parent = null;
            bool parentRequested = !string.IsNullOrEmpty(parentPath) || parentId.HasValue;
            if (parentRequested)
            {
                scopeError = PrefabSessionScope.TryResolveGameObject(
                    parentId, parentPath, out parent);
                if (scopeError != null) return scopeError;
                if (parent == null)
                {
                    string parentIdentifier = parentId.HasValue
                        ? $"instance ID {parentId.Value}"
                        : $"path '{parentPath}'";
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Parent GameObject not found using {parentIdentifier}; the asset was not instantiated.",
                        "not_found_error");
                }
            }
            else if (prefabRoot != null)
            {
                parent = prefabRoot;
            }

            // Instantiate the asset
            GameObject instance = null;
            try
            {
                instance = prefabRoot != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(asset, prefabRoot.scene)
                    : (GameObject)PrefabUtility.InstantiatePrefab(asset);
                
                // Set position
                instance.transform.position = position;
                
                // Set parent if specified
                if (parentRequested || prefabRoot != null)
                {
                    if (parent != null)
                    {
                        instance.transform.SetParent(parent.transform, false);
                    }
                }
                
                // Select the newly created object
                Selection.activeGameObject = instance;
                _pingObject(instance);
            }
            catch (Exception ex)
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);

                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Error instantiating asset: {ex.Message}", 
                    "instantiation_error"
                );
            }
            
            // Log the action
            McpLogger.LogInfo($"Added asset '{asset.name}' to the active context from path '{assetPath}'");
            
            // Create the response
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = prefabRoot != null
                    ? $"Successfully added asset '{asset.name}' with instance ID {instance.GetInstanceID()} to Prefab contents '{prefabRoot.scene.path}'"
                    : $"Successfully added asset '{asset.name}' with instance ID {instance.GetInstanceID()} to the scene",
                ["instanceId"] = instance.GetInstanceID()
            };
        }
    }
}
