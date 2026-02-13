using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace McpUnity.Utils
{
    /// <summary>
    /// Shared utility for resolving component types by name.
    /// Supports short names, fully-qualified names, and assembly-qualified names.
    /// </summary>
    public static class ComponentTypeResolver
    {
        /// <summary>
        /// Safely get types from an assembly, handling ReflectionTypeLoadException
        /// </summary>
        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
            catch (Exception)
            {
                return Enumerable.Empty<Type>();
            }
        }

        /// <summary>
        /// Find a component type by name. Supports:
        /// - Short names (e.g., "Outline", "Image")
        /// - Namespace-qualified names (e.g., "TMPro.TextMeshProUGUI")
        /// - Assembly-qualified names (e.g., "MyNamespace.MyComponent, Assembly-CSharp")
        /// </summary>
        /// <param name="componentName">The name of the component type</param>
        /// <returns>The component type, or null if not found</returns>
        public static Type FindComponentType(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                return null;

            // First try direct match (handles assembly-qualified and fully-qualified names)
            Type type = Type.GetType(componentName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
            {
                return type;
            }

            // Try common Unity namespaces
            string[] commonNamespaces = new string[]
            {
                "UnityEngine",
                "UnityEngine.UI",
                "UnityEngine.EventSystems",
                "UnityEngine.Animations",
                "UnityEngine.Rendering",
                "TMPro"
            };

            foreach (string ns in commonNamespaces)
            {
                type = Type.GetType($"{ns}.{componentName}, UnityEngine");
                if (type != null && typeof(Component).IsAssignableFrom(type))
                {
                    return type;
                }
            }

            // Determine if the input contains a namespace separator (for partial namespace matching)
            bool hasNamespaceSeparator = componentName.Contains(".");
            string suffixPattern = "." + componentName;
            List<Type> suffixMatches = hasNamespaceSeparator ? new List<Type>() : null;

            // Pass 1: exact match by short name or full name (returns immediately)
            // Also collect partial namespace suffix matches for Pass 2
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type t in SafeGetTypes(assembly))
                {
                    if (!typeof(Component).IsAssignableFrom(t))
                        continue;

                    // Exact match — return immediately
                    if (t.Name == componentName || t.FullName == componentName)
                        return t;

                    // Collect suffix matches for later uniqueness check
                    if (hasNamespaceSeparator && t.FullName != null
                        && t.FullName.EndsWith(suffixPattern, StringComparison.Ordinal))
                    {
                        suffixMatches.Add(t);
                    }
                }
            }

            // Pass 2: partial namespace match — only accept if exactly one type matched
            if (suffixMatches != null && suffixMatches.Count == 1)
            {
                return suffixMatches[0];
            }
            if (suffixMatches != null && suffixMatches.Count > 1)
            {
                string candidates = string.Join(", ", suffixMatches.Select(t => t.FullName));
                Debug.LogWarning($"[MCP Unity] Ambiguous component name '{componentName}' matched {suffixMatches.Count} types: {candidates}. Please use a fully-qualified name.");
            }

            return null;
        }
    }
}
