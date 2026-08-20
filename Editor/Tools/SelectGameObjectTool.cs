using System;
using System.Threading.Tasks;
using McpUnity.Unity;
using McpUnity.Utils;
using McpUnity.Services;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for selecting GameObjects in the Unity Editor
    /// </summary>
    public class SelectGameObjectTool : McpToolBase
    {
        public SelectGameObjectTool()
        {
            Name = "select_gameobject";
            Description = "Sets the selected GameObject in the Unity editor by path, name or instance ID";
        }
        
        /// <summary>
        /// Execute the SelectGameObject tool with the provided parameters synchronously
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject</param>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string objectName = parameters["objectName"]?.ToObject<string>();
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            GameObject selectedGameObject = null;
            
            // Validate parameters - require either objectPath or instanceId
            if (string.IsNullOrEmpty(objectPath) && string.IsNullOrEmpty(objectName) && !instanceId.HasValue)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'objectPath', 'objectName' or 'instanceId' not provided", 
                    "validation_error"
                );
            }
            
            string requestedPath = !string.IsNullOrEmpty(objectPath) ? objectPath : objectName;
            JObject scopeError;
            if (instanceId.HasValue || !string.IsNullOrEmpty(objectPath))
            {
                scopeError = PrefabSessionScope.TryResolveGameObject(
                    instanceId, objectPath, out selectedGameObject);
            }
            else
            {
                scopeError = PrefabSessionScope.TryResolveGameObjectByName(
                    objectName, out selectedGameObject);
            }
            if (scopeError != null) return scopeError;

            if (selectedGameObject == null)
            {
                string identifier = instanceId.HasValue
                    ? $"instance ID {instanceId.Value}"
                    : $"path or name '{requestedPath}'";
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject not found using {identifier}.",
                    "not_found_error");
            }
            
            Selection.activeGameObject = selectedGameObject;

            // Ping the selected object
            EditorGUIUtility.PingObject(selectedGameObject);
            
            McpLogger.LogInfo($"[MCP Unity] Selected GameObject: {selectedGameObject?.name}");
            
            // Create the response
            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully selected GameObject {selectedGameObject?.name}"
            };
        }
    }
}
