using McpUnity.Resources;
using McpUnity.Services;
using McpUnity.Unity;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for retrieving detailed information about a specific GameObject.
    /// This tool provides the same functionality as the get_gameobject resource,
    /// but as a tool that can be invoked directly without URI template parameters.
    /// </summary>
    public class GetGameObjectTool : McpToolBase
    {
        public GetGameObjectTool()
        {
            Name = "get_gameobject";
            Description = "Retrieves detailed information about a specific GameObject by instance ID, plain name, or canonical hierarchical path (e.g., \"Parent/Child/MyObject\"). A plain token checks roots in the current scope first; if no root matches, its hierarchy-wide name fallback must be unique. Ambiguous roots, fallback names, or same-name path segments return object_path_ambiguity_error with canonical candidate paths and instanceIds; use 0-based Name[n] segments to disambiguate. Returns all component properties including Transform position, rotation, scale, and more.";
        }

        /// <summary>
        /// Execute the GetGameObject tool with the provided parameters
        /// </summary>
        /// <param name="parameters">Tool parameters as a JObject. Should include 'idOrName' which can be an instance ID, name, or path</param>
        /// <returns>A JObject containing the GameObject data</returns>
        public override JObject Execute(JObject parameters)
        {
            // Validate parameters
            if (parameters == null || !parameters.ContainsKey("idOrName"))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Missing required parameter: idOrName",
                    "validation_error"
                );
            }

            string idOrName = parameters["idOrName"]?.ToObject<string>();

            if (idOrName == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'idOrName' cannot be null",
                    "validation_error"
                );
            }

            // Try to parse as an instance ID first
            if (int.TryParse(idOrName, out int instanceId))
            {
                JObject scopeError = PrefabSessionScope.TryResolveGameObject(
                    instanceId, null, out GameObject gameObjectById);
                if (scopeError != null) return scopeError;
                return BuildResponseOrNotFound(gameObjectById, idOrName, parameters);
            }

            JObject pathScopeError = PrefabSessionScope.TryResolveGameObjectPathOrName(
                idOrName, out GameObject gameObjectByPath);
            if (pathScopeError != null) return pathScopeError;
            return BuildResponseOrNotFound(gameObjectByPath, idOrName, parameters);
        }

        private static JObject BuildResponseOrNotFound(
            GameObject gameObject,
            string idOrName,
            JObject parameters)
        {

            // Check if the GameObject was found
            if (gameObject == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject with '{idOrName}' reference not found. Make sure the GameObject exists and is loaded in the current scene(s).",
                    "not_found_error"
                );
            }

            // Parse optional depth control parameters
            int maxDepth = parameters["maxDepth"]?.ToObject<int?>() ?? -1;
            bool includeChildren = parameters["includeChildren"]?.ToObject<bool?>() ?? true;

            // Convert the GameObject to a JObject using the resource's static method
            JObject gameObjectData = GetGameObjectResource.GameObjectToJObject(
                gameObject, true, maxDepth, 0, includeChildren);

            // Create the response
            return new JObject
            {
                ["success"] = true,
                ["message"] = $"Retrieved GameObject data for '{gameObject.name}'",
                ["gameObject"] = gameObjectData,
                ["instanceId"] = gameObject.GetInstanceID()
            };
        }
    }
}
