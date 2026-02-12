using System;
using McpUnity.Unity;
using McpUnity.Utils;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for removing a component from a GameObject in the Unity Editor
    /// </summary>
    public class RemoveComponentTool : McpToolBase
    {
        public RemoveComponentTool()
        {
            Name = "remove_component";
            Description = "Removes a component from a GameObject. Identifies the GameObject by instance ID or hierarchy path.";
        }

        /// <summary>
        /// Execute the RemoveComponent tool with the provided parameters
        /// </summary>
        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();

            if (string.IsNullOrEmpty(componentName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'componentName' not provided",
                    "validation_error"
                );
            }

            // Find the GameObject
            JObject error = GameObjectToolUtils.FindGameObject(instanceId, objectPath, out GameObject gameObject, out string identifierInfo);
            if (error != null)
                return error;

            // Resolve the component type
            Type componentType = ComponentTypeResolver.FindComponentType(componentName);

            // Find the component on the GameObject
            Component component = componentType != null
                ? gameObject.GetComponent(componentType)
                : gameObject.GetComponent(componentName);

            if (component == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Component '{componentName}' not found on GameObject '{gameObject.name}'",
                    "component_error"
                );
            }

            // Prevent removing Transform (every GO must have one)
            if (component is Transform)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Cannot remove Transform component from a GameObject",
                    "validation_error"
                );
            }

            string goName = gameObject.name;
            string goPath = GameObjectToolUtils.GetGameObjectPath(gameObject);
            int goInstanceId = gameObject.GetInstanceID();

            // Remove the component with undo support
            Undo.DestroyObjectImmediate(component);
            EditorUtility.SetDirty(gameObject);

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully removed component '{componentName}' from GameObject '{goName}'",
                ["instanceId"] = goInstanceId,
                ["name"] = goName,
                ["path"] = goPath
            };
        }
    }
}
