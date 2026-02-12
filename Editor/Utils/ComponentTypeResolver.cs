using System;
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

            // Try assemblies search - match by short name or full name
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in assembly.GetTypes())
                    {
                        if ((t.Name == componentName || t.FullName == componentName)
                            && typeof(Component).IsAssignableFrom(t))
                        {
                            return t;
                        }
                    }
                }
                catch (Exception)
                {
                    // Some assemblies might throw exceptions when getting types
                    continue;
                }
            }

            return null;
        }
    }
}
