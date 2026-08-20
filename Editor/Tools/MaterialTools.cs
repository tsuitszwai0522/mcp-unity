using System;
using System.Collections.Generic;
using McpUnity.Unity;
using McpUnity.Utils;
using McpUnity.Services;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Utility class for Material tool operations
    /// </summary>
    public static class MaterialToolUtils
    {
        /// <summary>
        /// Get the default lit shader based on the current render pipeline
        /// </summary>
        public static string GetDefaultShaderName()
        {
            // Check for URP
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                string pipelineName = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;

                if (pipelineName.Contains("Universal") || pipelineName.Contains("URP"))
                {
                    return "Universal Render Pipeline/Lit";
                }
                else if (pipelineName.Contains("HD") || pipelineName.Contains("HDRP"))
                {
                    return "HDRP/Lit";
                }
                else
                {
                    McpLogger.LogWarning("Unknown render pipeline, defaulting to Standard shader");
                }
            }

            // Default to Standard (Built-in Render Pipeline)
            return "Standard";
        }

        /// <summary>
        /// Find a shader by name, searching common Unity shader paths
        /// </summary>
        public static Shader FindShader(string shaderName)
        {
            // Try direct lookup first
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                return shader;
            }

            // Common shader path prefixes to try
            string[] prefixes = new string[]
            {
                "",
                "Standard",
                "Universal Render Pipeline/",
                "URP/",
                "HDRP/",
                "Hidden/",
                "Legacy Shaders/",
                "Mobile/",
                "Particles/",
                "Skybox/",
                "Sprites/",
                "UI/",
                "Unlit/"
            };

            foreach (string prefix in prefixes)
            {
                shader = Shader.Find(prefix + shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        /// <summary>
        /// Load a material from an asset path
        /// </summary>
        public static Material LoadMaterial(string materialPath)
        {
            if (string.IsNullOrEmpty(materialPath))
            {
                return null;
            }

            // Ensure path starts with Assets/
            if (!materialPath.StartsWith("Assets/"))
            {
                materialPath = "Assets/" + materialPath;
            }

            // Ensure .mat extension
            if (!materialPath.EndsWith(".mat"))
            {
                materialPath += ".mat";
            }

            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        /// <summary>
        /// Convert a JToken to a shader property value
        /// </summary>
        public static object ConvertPropertyValue(
            JToken token,
            ShaderUtil.ShaderPropertyType propertyType,
            object seed,
            List<string> failures)
        {
            failures ??= new List<string>();
            if (token == null)
            {
                failures.Add($"Missing value for shader property type {propertyType}");
                return null;
            }

            try
            {
                switch (propertyType)
                {
                    case ShaderUtil.ShaderPropertyType.Color:
                        return SerializedFieldConverter.TryConvertUnityStruct(
                            token, typeof(Color), seed, failures, out object colorValue)
                            ? colorValue
                            : null;

                    case ShaderUtil.ShaderPropertyType.Vector:
                        return SerializedFieldConverter.TryConvertUnityStruct(
                            token, typeof(Vector4), seed, failures, out object vectorValue)
                            ? vectorValue
                            : null;

                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                        {
                            failures.Add($"Expected a number for shader property type {propertyType}");
                            return null;
                        }
                        return token.ToObject<float>();

                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        if (token.Type == JTokenType.Null)
                        {
                            return null;
                        }
                        if (token.Type != JTokenType.String)
                        {
                            failures.Add("Expected an asset path string or null for texture property");
                            return null;
                        }
                        string texPath = token.ToObject<string>();
                        if (string.IsNullOrEmpty(texPath))
                        {
                            failures.Add("Texture asset path cannot be empty");
                            return null;
                        }
                        if (!texPath.StartsWith("Assets/"))
                        {
                            texPath = "Assets/" + texPath;
                        }
                        Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                        if (texture == null)
                        {
                            failures.Add($"Texture was not found at '{texPath}'");
                            return null;
                        }
                        return texture;

                    case ShaderUtil.ShaderPropertyType.Int:
                        if (token.Type == JTokenType.Integer)
                        {
                            return token.ToObject<int>();
                        }
                        if (token.Type == JTokenType.Float)
                        {
                            double numericValue = token.ToObject<double>();
                            if (double.IsNaN(numericValue)
                                || double.IsInfinity(numericValue)
                                || numericValue < int.MinValue
                                || numericValue > int.MaxValue
                                || Math.Truncate(numericValue) != numericValue)
                            {
                                failures.Add(
                                    "Expected a whole-number JSON value within Int32 range for shader property type Int");
                                return null;
                            }
                            return Convert.ToInt32(numericValue);
                        }
                        else
                        {
                            failures.Add(
                                "Expected an integer or whole-number JSON float for shader property type Int");
                            return null;
                        }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Failed to convert shader property value to {propertyType}: {ex.Message}");
                return null;
            }

            failures.Add($"Shader property type {propertyType} is not supported");
            return null;
        }

        /// <summary>
        /// Read a material property's current value for partial-write seeding.
        /// </summary>
        public static object GetPropertySeed(
            Material material,
            string propertyName,
            ShaderUtil.ShaderPropertyType propertyType,
            List<string> failures)
        {
            if (!material.HasProperty(propertyName))
            {
                failures.Add(
                    $"'{propertyName}' is not a property of shader {material.shader.name}");
                return null;
            }

            switch (propertyType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    return material.GetColor(propertyName);
                case ShaderUtil.ShaderPropertyType.Vector:
                    return material.GetVector(propertyName);
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    return material.GetFloat(propertyName);
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    return material.GetTexture(propertyName);
                case ShaderUtil.ShaderPropertyType.Int:
                    return material.GetInt(propertyName);
                default:
                    failures.Add($"Shader property type {propertyType} is not supported");
                    return null;
            }
        }

        /// <summary>
        /// Create a structured shader-property failure.
        /// </summary>
        public static JObject CreatePropertyFailure(string propertyName, string reason)
        {
            return new JObject
            {
                ["property"] = propertyName,
                ["reason"] = reason
            };
        }

        /// <summary>
        /// Find a GameObject by instance ID or path
        /// </summary>
        public static JObject FindGameObject(
            int? instanceId,
            string objectPath,
            out GameObject gameObject)
        {
            return PrefabSessionScope.TryResolveGameObject(
                instanceId, objectPath, out gameObject);
        }
    }

    /// <summary>
    /// Tool for creating new materials
    /// </summary>
    public class CreateMaterialTool : McpToolBase
    {
        public CreateMaterialTool()
        {
            Name = "create_material";
            Description = "Creates a new material with the specified shader and saves it to the project";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string name = parameters["name"]?.ToObject<string>();
            string shaderName = parameters["shader"]?.ToObject<string>();
            string savePath = parameters["savePath"]?.ToObject<string>();
            JToken propertiesToken = parameters["properties"];
            JObject properties = propertiesToken as JObject;
            JToken colorParam = parameters["color"];

            // Validate required parameters
            if (string.IsNullOrEmpty(name))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'name' not provided",
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

            // Default shader based on current render pipeline
            if (string.IsNullOrEmpty(shaderName))
            {
                shaderName = MaterialToolUtils.GetDefaultShaderName();
            }

            // Find the shader
            Shader shader = MaterialToolUtils.FindShader(shaderName);
            if (shader == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Shader '{shaderName}' not found in Unity",
                    "not_found_error"
                );
            }

            // Ensure save path has proper format
            if (!savePath.StartsWith("Assets/"))
            {
                savePath = "Assets/" + savePath;
            }
            if (!savePath.EndsWith(".mat"))
            {
                savePath += ".mat";
            }

            // Create the material
            Material material = new Material(shader);
            material.name = name;
            var modifiedProperties = new List<string>();
            var failedProperties = new List<JObject>();
            var unknownProperties = new List<string>();

            if (propertiesToken != null && properties == null)
            {
                failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                    "properties", "Expected an object containing shader property values"));
            }

            // Apply color if provided (auto-detect correct property name)
            if (colorParam != null)
            {
                string baseColorProperty = FindBaseColorPropertyName(material);
                if (baseColorProperty == null)
                {
                    failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                        "color", $"Shader {shader.name} has no color property"));
                }
                else
                {
                    var colorFailures = new List<string>();
                    object colorSeed = MaterialToolUtils.GetPropertySeed(
                        material,
                        baseColorProperty,
                        ShaderUtil.ShaderPropertyType.Color,
                        colorFailures);
                    object colorValue = MaterialToolUtils.ConvertPropertyValue(
                        colorParam,
                        ShaderUtil.ShaderPropertyType.Color,
                        colorSeed,
                        colorFailures);
                    if (colorFailures.Count > 0)
                    {
                        failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                            "color", string.Join("; ", colorFailures.ToArray())));
                    }
                    else
                    {
                        material.SetColor(baseColorProperty, (Color)colorValue);
                        modifiedProperties.Add("color");
                    }
                }
            }

            // Apply initial properties if provided
            if (properties != null && properties.Count > 0)
            {
                ApplyMaterialProperties(
                    material,
                    properties,
                    modifiedProperties,
                    failedProperties,
                    unknownProperties);
            }

            if (failedProperties.Count > 0)
            {
                UnityEngine.Object.DestroyImmediate(material);
                return new JObject
                {
                    ["success"] = false,
                    ["type"] = "text",
                    ["message"] =
                        $"Material '{name}' was not created because " +
                        $"{failedProperties.Count} property value(s) failed; no asset was created.",
                    ["materialPath"] = savePath,
                    ["materialName"] = name,
                    ["shaderName"] = shader.name,
                    ["modifiedProperties"] = new JArray(modifiedProperties),
                    ["failedProperties"] = new JArray(failedProperties),
                    ["unknownProperties"] = new JArray(unknownProperties)
                };
            }

            // Create the parent directory only after all property validation succeeds.
            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // Save the material as an asset
            AssetDatabase.CreateAsset(material, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            McpLogger.LogInfo($"[MCP Unity] Created material '{name}' with shader '{shaderName}' at '{savePath}'");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully created material '{name}' with shader '{shaderName}'",
                ["materialPath"] = savePath,
                ["materialName"] = name,
                ["shaderName"] = shader.name,
                ["modifiedProperties"] = new JArray(modifiedProperties),
                ["failedProperties"] = new JArray(failedProperties),
                ["unknownProperties"] = new JArray(unknownProperties)
            };
        }

        private void ApplyMaterialProperties(
            Material material,
            JObject properties,
            List<string> modifiedProperties,
            List<JObject> failedProperties,
            List<string> unknownProperties)
        {
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);

            foreach (var prop in properties.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;
                bool found = false;

                // Find the property in the shader
                for (int i = 0; i < propertyCount; i++)
                {
                    string shaderPropName = ShaderUtil.GetPropertyName(shader, i);
                    if (shaderPropName == propName)
                    {
                        found = true;
                        ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);
                        var conversionFailures = new List<string>();
                        object seed = MaterialToolUtils.GetPropertySeed(
                            material, propName, propType, conversionFailures);
                        object value = MaterialToolUtils.ConvertPropertyValue(
                            propValue, propType, seed, conversionFailures);

                        if (conversionFailures.Count > 0)
                        {
                            failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                                propName, string.Join("; ", conversionFailures.ToArray())));
                        }
                        else
                        {
                            try
                            {
                                SetMaterialProperty(material, propName, propType, value);
                                modifiedProperties.Add(propName);
                            }
                            catch (Exception ex)
                            {
                                failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                                    propName, $"Failed to set material property: {ex.Message}"));
                            }
                        }
                        break;
                    }
                }

                if (!found)
                {
                    string reason = $"'{propName}' is not a property of shader {shader.name}";
                    unknownProperties.Add(propName);
                    failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(propName, reason));
                }
            }
        }

        private void SetMaterialProperty(Material material, string propName, ShaderUtil.ShaderPropertyType propType, object value)
        {
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    material.SetColor(propName, (Color)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    material.SetVector(propName, (Vector4)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    material.SetFloat(propName, (float)value);
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    material.SetTexture(propName, (Texture)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Int:
                    material.SetInt(propName, (int)value);
                    break;
            }
        }

        /// <summary>
        /// Apply base color to material, auto-detecting the correct property name
        /// </summary>
        private string FindBaseColorPropertyName(Material material)
        {
            // Common color property names in order of preference
            string[] colorPropertyNames = new string[]
            {
                "_BaseColor",    // URP Lit, HDRP Lit
                "_Color",        // Standard, Legacy shaders
                "_TintColor",    // Particle shaders
                "_MainColor"     // Some custom shaders
            };

            foreach (string propName in colorPropertyNames)
            {
                if (material.HasProperty(propName))
                {
                    return propName;
                }
            }

            // Fallback: try to find any color property
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.Color)
                {
                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    return propName;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Tool for assigning materials to GameObjects
    /// </summary>
    public class AssignMaterialTool : McpToolBase
    {
        public AssignMaterialTool()
        {
            Name = "assign_material";
            Description = "Assigns a material to a GameObject's Renderer component";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            int? instanceId = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();
            string materialPath = parameters["materialPath"]?.ToObject<string>();
            int slot = parameters["slot"]?.ToObject<int?>() ?? 0;

            // Validate parameters
            if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Either 'instanceId' or 'objectPath' must be provided",
                    "validation_error"
                );
            }

            if (string.IsNullOrEmpty(materialPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'materialPath' not provided",
                    "validation_error"
                );
            }

            // Find the GameObject
            JObject scopeError = MaterialToolUtils.FindGameObject(
                instanceId, objectPath, out GameObject gameObject);
            if (scopeError != null) return scopeError;
            if (gameObject == null)
            {
                string identifier = instanceId.HasValue ? $"ID {instanceId.Value}" : $"path '{objectPath}'";
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject with {identifier} not found",
                    "not_found_error"
                );
            }

            // Get the Renderer component
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"GameObject '{gameObject.name}' does not have a Renderer component",
                    "component_error"
                );
            }

            // Load the material
            Material material = MaterialToolUtils.LoadMaterial(materialPath);
            if (material == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Material at path '{materialPath}' not found",
                    "not_found_error"
                );
            }

            // Validate slot index
            Material[] materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Material slot {slot} is out of range. GameObject has {materials.Length} material slot(s) (0-{materials.Length - 1})",
                    "validation_error"
                );
            }

            // Record for undo
            Undo.RecordObject(renderer, $"Assign Material to {gameObject.name}");

            // Assign the material
            materials[slot] = material;
            renderer.sharedMaterials = materials;

            // Mark as dirty
            EditorUtility.SetDirty(renderer);
            if (PrefabUtility.IsPartOfAnyPrefab(gameObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            McpLogger.LogInfo($"[MCP Unity] Assigned material '{material.name}' to '{gameObject.name}' at slot {slot}");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Successfully assigned material '{material.name}' to '{gameObject.name}' at slot {slot}",
                ["gameObjectName"] = gameObject.name,
                ["materialName"] = material.name,
                ["slot"] = slot
            };
        }
    }

    /// <summary>
    /// Tool for modifying material properties
    /// </summary>
    public class ModifyMaterialTool : McpToolBase
    {
        public ModifyMaterialTool()
        {
            Name = "modify_material";
            Description = "Modifies properties of an existing material";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string materialPath = parameters["materialPath"]?.ToObject<string>();
            JObject properties = parameters["properties"] as JObject;

            // Validate parameters
            if (string.IsNullOrEmpty(materialPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'materialPath' not provided",
                    "validation_error"
                );
            }

            if (properties == null || properties.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'properties' not provided or empty",
                    "validation_error"
                );
            }

            // Load the material
            Material material = MaterialToolUtils.LoadMaterial(materialPath);
            if (material == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Material at path '{materialPath}' not found",
                    "not_found_error"
                );
            }

            // Record for undo
            Undo.RecordObject(material, $"Modify Material {material.name}");

            // Apply properties
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            List<string> modifiedProperties = new List<string>();
            List<string> unknownProperties = new List<string>();
            List<JObject> failedProperties = new List<JObject>();

            foreach (var prop in properties.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;
                bool found = false;

                // Find the property in the shader
                for (int i = 0; i < propertyCount; i++)
                {
                    string shaderPropName = ShaderUtil.GetPropertyName(shader, i);
                    if (shaderPropName == propName)
                    {
                        found = true;
                        ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);
                        var conversionFailures = new List<string>();
                        object seed = MaterialToolUtils.GetPropertySeed(
                            material, propName, propType, conversionFailures);
                        object value = MaterialToolUtils.ConvertPropertyValue(
                            propValue, propType, seed, conversionFailures);

                        if (conversionFailures.Count > 0)
                        {
                            failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                                propName, string.Join("; ", conversionFailures.ToArray())));
                        }
                        else
                        {
                            try
                            {
                                SetMaterialProperty(material, propName, propType, value);
                                modifiedProperties.Add(propName);
                            }
                            catch (Exception ex)
                            {
                                failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                                    propName, $"Failed to set material property: {ex.Message}"));
                            }
                        }
                        break;
                    }
                }

                if (!found)
                {
                    unknownProperties.Add(propName);
                    failedProperties.Add(MaterialToolUtils.CreatePropertyFailure(
                        propName,
                        $"'{propName}' is not a property of shader {shader.name}"));
                }
            }

            if (modifiedProperties.Count > 0)
            {
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                McpLogger.LogInfo(
                    $"[MCP Unity] Modified material '{material.name}': " +
                    string.Join(", ", modifiedProperties));
            }

            JObject result = new JObject
            {
                ["success"] = failedProperties.Count == 0,
                ["type"] = "text",
                ["message"] = failedProperties.Count == 0
                    ? $"Successfully modified material '{material.name}'"
                    : modifiedProperties.Count == 0
                        ? $"No properties were modified on material '{material.name}'; " +
                            $"{failedProperties.Count} property value(s) failed"
                    : $"Modified material '{material.name}' with " +
                        $"{modifiedProperties.Count} property value(s) applied and " +
                        $"{failedProperties.Count} failure(s)",
                ["materialName"] = material.name,
                ["modifiedProperties"] = new JArray(modifiedProperties),
                ["failedProperties"] = new JArray(failedProperties),
                ["unknownProperties"] = new JArray(unknownProperties)
            };

            if (unknownProperties.Count > 0)
            {
                result["message"] = result["message"].ToString() +
                    $". Properties not found: {string.Join(", ", unknownProperties)}";
            }

            return result;
        }

        private void SetMaterialProperty(Material material, string propName, ShaderUtil.ShaderPropertyType propType, object value)
        {
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    material.SetColor(propName, (Color)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    material.SetVector(propName, (Vector4)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    material.SetFloat(propName, (float)value);
                    break;
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    material.SetTexture(propName, (Texture)value);
                    break;
                case ShaderUtil.ShaderPropertyType.Int:
                    material.SetInt(propName, (int)value);
                    break;
            }
        }
    }

    /// <summary>
    /// Tool for getting material information
    /// </summary>
    public class GetMaterialInfoTool : McpToolBase
    {
        public GetMaterialInfoTool()
        {
            Name = "get_material_info";
            Description = "Gets detailed information about a material including its shader and all properties";
        }

        public override JObject Execute(JObject parameters)
        {
            // Extract parameters
            string materialPath = parameters["materialPath"]?.ToObject<string>();

            // Validate parameters
            if (string.IsNullOrEmpty(materialPath))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'materialPath' not provided",
                    "validation_error"
                );
            }

            // Load the material
            Material material = MaterialToolUtils.LoadMaterial(materialPath);
            if (material == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Material at path '{materialPath}' not found",
                    "not_found_error"
                );
            }

            // Get shader info
            Shader shader = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);

            // Build properties array
            JArray propertiesArray = new JArray();
            for (int i = 0; i < propertyCount; i++)
            {
                string propName = ShaderUtil.GetPropertyName(shader, i);
                string propDescription = ShaderUtil.GetPropertyDescription(shader, i);
                ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);

                JObject propInfo = new JObject
                {
                    ["name"] = propName,
                    ["description"] = propDescription,
                    ["type"] = propType.ToString()
                };

                // Get current value
                propInfo["value"] = GetPropertyValue(material, propName, propType);

                // Add range info if applicable
                if (propType == ShaderUtil.ShaderPropertyType.Range)
                {
                    propInfo["rangeMin"] = ShaderUtil.GetRangeLimits(shader, i, 1);
                    propInfo["rangeMax"] = ShaderUtil.GetRangeLimits(shader, i, 2);
                }

                propertiesArray.Add(propInfo);
            }

            // Build render queue info
            string renderQueueName = "Custom";
            int renderQueue = material.renderQueue;
            if (renderQueue <= 2000) renderQueueName = "Background";
            else if (renderQueue <= 2450) renderQueueName = "Geometry";
            else if (renderQueue <= 2500) renderQueueName = "AlphaTest";
            else if (renderQueue <= 3000) renderQueueName = "Transparent";
            else renderQueueName = "Overlay";

            McpLogger.LogInfo($"[MCP Unity] Retrieved info for material '{material.name}'");

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Material info for '{material.name}'",
                ["materialName"] = material.name,
                ["materialPath"] = materialPath,
                ["shaderName"] = shader.name,
                ["renderQueue"] = renderQueue,
                ["renderQueueCategory"] = renderQueueName,
                ["enableInstancing"] = material.enableInstancing,
                ["doubleSidedGI"] = material.doubleSidedGI,
                ["passCount"] = material.passCount,
                ["properties"] = propertiesArray
            };
        }

        private JToken GetPropertyValue(Material material, string propName, ShaderUtil.ShaderPropertyType propType)
        {
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    Color color = material.GetColor(propName);
                    return new JObject
                    {
                        ["r"] = color.r,
                        ["g"] = color.g,
                        ["b"] = color.b,
                        ["a"] = color.a
                    };

                case ShaderUtil.ShaderPropertyType.Vector:
                    Vector4 vec = material.GetVector(propName);
                    return new JObject
                    {
                        ["x"] = vec.x,
                        ["y"] = vec.y,
                        ["z"] = vec.z,
                        ["w"] = vec.w
                    };

                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    return material.GetFloat(propName);

                case ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = material.GetTexture(propName);
                    if (tex != null)
                    {
                        return AssetDatabase.GetAssetPath(tex);
                    }
                    return null;

                case ShaderUtil.ShaderPropertyType.Int:
                    return material.GetInt(propName);

                default:
                    return null;
            }
        }
    }
}
