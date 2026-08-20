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
        private sealed class ComponentTypeIndex
        {
            public int AssemblyCount { get; }
            public Dictionary<string, List<Type>> ByFullName { get; }
            public Dictionary<string, List<Type>> ByShortName { get; }
            public List<Type> AllTypes { get; }

            public ComponentTypeIndex(
                int assemblyCount,
                Dictionary<string, List<Type>> byFullName,
                Dictionary<string, List<Type>> byShortName,
                List<Type> allTypes)
            {
                AssemblyCount = assemblyCount;
                ByFullName = byFullName;
                ByShortName = byShortName;
                AllTypes = allTypes;
            }
        }

        private static readonly object TypeIndexLock = new object();
        private static ComponentTypeIndex _typeIndex;

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
        /// Find a component type by name, using the target GameObject to disambiguate
        /// short and partial namespace matches when exactly one candidate is attached.
        /// </summary>
        /// <param name="componentName">The name of the component type</param>
        /// <param name="targetGameObject">The GameObject used to narrow ambiguous matches</param>
        /// <param name="warning">A client-visible warning when target narrowing selected a candidate</param>
        /// <param name="ambiguityError">A client-visible error when multiple candidates remain</param>
        /// <returns>The resolved component type, or null if not found or ambiguous</returns>
        public static Type FindComponentType(
            string componentName,
            GameObject targetGameObject,
            out string warning,
            out string ambiguityError)
        {
            warning = null;
            ambiguityError = null;

            if (string.IsNullOrEmpty(componentName))
                return null;

            // Assembly-qualified names identify an exact type without relying on scan order.
            if (componentName.Contains(","))
            {
                Type assemblyQualifiedType = Type.GetType(componentName);
                return assemblyQualifiedType != null
                    && typeof(Component).IsAssignableFrom(assemblyQualifiedType)
                        ? assemblyQualifiedType
                        : null;
            }

            bool hasNamespaceSeparator = componentName.Contains(".");
            string suffixPattern = "." + componentName;
            ComponentTypeIndex index = GetTypeIndex();
            index.ByFullName.TryGetValue(componentName, out List<Type> fullNameMatches);

            if (fullNameMatches != null && fullNameMatches.Count > 0)
            {
                return ResolveCandidates(
                    componentName,
                    fullNameMatches,
                    targetGameObject,
                    out warning,
                    out ambiguityError);
            }

            IEnumerable<Type> candidates;
            if (hasNamespaceSeparator)
            {
                candidates = index.AllTypes.Where(type => type.FullName != null
                    && type.FullName.EndsWith(suffixPattern, StringComparison.Ordinal));
            }
            else if (!index.ByShortName.TryGetValue(componentName, out List<Type> shortNameMatches))
            {
                candidates = Enumerable.Empty<Type>();
            }
            else
            {
                candidates = shortNameMatches;
            }

            return ResolveCandidates(
                componentName,
                candidates,
                targetGameObject,
                out warning,
                out ambiguityError);
        }

        private static Type ResolveCandidates(
            string componentName,
            IEnumerable<Type> matches,
            GameObject targetGameObject,
            out string warning,
            out string ambiguityError)
        {
            warning = null;
            ambiguityError = null;

            List<Type> candidates = matches
                .Distinct()
                .OrderBy(GetDisplayName, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0)
                return null;
            if (candidates.Count == 1)
                return candidates[0];

            if (targetGameObject != null)
            {
                var attachedExactTypes = new HashSet<Type>(
                    targetGameObject.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType()));
                List<Type> attachedCandidates = candidates
                    .Where(candidate => attachedExactTypes.Contains(candidate))
                    .ToList();
                if (attachedCandidates.Count == 1)
                {
                    Type selected = attachedCandidates[0];
                    string otherCandidates = string.Join(", ", candidates
                        .Where(candidate => candidate != selected)
                        .Select(GetDisplayName));
                    warning = $"Component name '{componentName}' is ambiguous. " +
                        $"Selected '{GetDisplayName(selected)}' because it is the only exact " +
                        $"candidate type attached to GameObject '{targetGameObject.name}'. " +
                        $"Other candidates: {otherCandidates}.";
                    return selected;
                }
            }

            string allCandidates = string.Join(", ", candidates.Select(GetDisplayName));
            ambiguityError = $"Ambiguous component name '{componentName}' matched " +
                $"{candidates.Count} types: {allCandidates}. " +
                "Please use a fully-qualified component name.";
            return null;
        }

        private static ComponentTypeIndex GetTypeIndex()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            lock (TypeIndexLock)
            {
                // Unity Editor assemblies are only added within an AppDomain; script recompilation
                // reloads the domain and resets this static cache. Therefore an unchanged count
                // implies an unchanged assembly set here. Use assembly identities instead if Unity
                // ever supports unloading or replacing assemblies without a domain reload.
                if (_typeIndex != null && _typeIndex.AssemblyCount == assemblies.Length)
                    return _typeIndex;

                var byFullName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
                var byShortName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
                var allTypes = new List<Type>();

                foreach (Assembly assembly in assemblies)
                {
                    foreach (Type type in SafeGetTypes(assembly))
                    {
                        if (!typeof(Component).IsAssignableFrom(type))
                            continue;

                        allTypes.Add(type);
                        if (!string.IsNullOrEmpty(type.FullName))
                        {
                            AddToIndex(byFullName, type.FullName, type);
                        }
                        AddToIndex(byShortName, type.Name, type);
                    }
                }

                _typeIndex = new ComponentTypeIndex(
                    assemblies.Length,
                    byFullName,
                    byShortName,
                    allTypes);
                return _typeIndex;
            }
        }

        private static void AddToIndex(
            Dictionary<string, List<Type>> index,
            string key,
            Type type)
        {
            if (!index.TryGetValue(key, out List<Type> matches))
            {
                matches = new List<Type>();
                index[key] = matches;
            }
            matches.Add(type);
        }

        private static string GetDisplayName(Type type)
        {
            return type.FullName ?? type.Name;
        }
    }
}
