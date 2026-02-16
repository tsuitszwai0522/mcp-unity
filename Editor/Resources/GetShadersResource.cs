using UnityEngine;
using UnityEditor;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;

namespace McpUnity.Resources
{
    /// <summary>
    /// Resource for listing all available shaders (project assets + built-in)
    /// </summary>
    public class GetShadersResource : McpResourceBase
    {
        public GetShadersResource()
        {
            Name = "get_shaders";
            Description = "Lists all available shaders in the project and built-in shaders";
            Uri = "unity://shaders";
        }

        /// <summary>
        /// Fetch all available shaders
        /// </summary>
        public override JObject Fetch(JObject parameters)
        {
            JArray shaders = new JArray();

            // Use ShaderUtil.GetAllShaderInfo to get the complete list (project + built-in)
            var shaderInfos = ShaderUtil.GetAllShaderInfo();

            foreach (var info in shaderInfos)
            {
                // Skip hidden/internal shaders (name starts with "Hidden/")
                if (info.name.StartsWith("Hidden/"))
                    continue;

                var shader = Shader.Find(info.name);
                if (shader == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(shader);
                bool isBuiltIn = string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/");

                var shaderObj = new JObject
                {
                    ["name"] = info.name,
                    ["isBuiltIn"] = isBuiltIn,
                    ["renderQueue"] = shader.renderQueue,
                    ["propertyCount"] = ShaderUtil.GetPropertyCount(shader)
                };

                if (!isBuiltIn)
                {
                    shaderObj["path"] = assetPath;
                }

                shaders.Add(shaderObj);
            }

            return new JObject
            {
                ["success"] = true,
                ["message"] = $"Retrieved {shaders.Count} shaders",
                ["shaders"] = shaders
            };
        }
    }
}
