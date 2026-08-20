using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using McpUnity.Services;
using McpUnity.Unity;
using McpUnity.Utils;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using ComponentResolver = McpUnity.Utils.ComponentTypeResolver;
using EditorUnityEventTools = UnityEditor.Events.UnityEventTools;

namespace McpUnity.Tools
{
    /// <summary>
    /// Adds a validated persistent listener to a serialized UnityEvent. The caller supplies intent
    /// (event field, listener target, method, and optional static argument); this tool derives the
    /// PersistentListenerMode from the event and method signatures.
    /// </summary>
    public class WireUnityEventTool : McpToolBase
    {
        private static readonly HashSet<string> AllowedParameters = new HashSet<string>
        {
            "instanceId",
            "objectPath",
            "componentName",
            "eventFieldName",
            "listenerInstanceId",
            "listenerObjectPath",
            "listenerComponentName",
            "methodName",
            "staticArgument"
        };

        private sealed class ListenerBinding
        {
            public MethodInfo Method { get; set; }
            public PersistentListenerMode Mode { get; set; }
            public Type[] ParameterTypes { get; set; }
            public object StaticArgument { get; set; }
            public string Warning { get; set; }

            public string Signature => FormatMethodSignature(Method);
        }

        public WireUnityEventTool()
        {
            Name = "wire_unity_event";
            Description = "Adds a validated persistent listener to a UnityEvent field. The caller " +
                "provides the source GameObject/component, event field, listener GameObject/component, " +
                "method name, and an optional staticArgument. PersistentListenerMode is never accepted " +
                "from the caller: it is derived from the UnityEvent generic signature and the listener " +
                "method signature. Missing or ambiguous methods fail without adding a listener. The " +
                "response contains the mode and persistent call read back from SerializedProperty.";
        }

        public override JObject Execute(JObject parameters)
        {
            foreach (JProperty supplied in parameters.Properties())
            {
                if (!AllowedParameters.Contains(supplied.Name))
                {
                    return Error(
                        $"Unknown parameter '{supplied.Name}'. Persistent listener mode is inferred and " +
                        "cannot be supplied by the caller.",
                        "validation_error");
                }
            }

            if (!TryReadExclusiveLocator(
                parameters,
                "instanceId",
                "objectPath",
                "Source",
                out int? instanceId,
                out string objectPath,
                out JObject locatorError))
            {
                return EnsureFailureEnvelope(locatorError);
            }

            if (!TryReadExclusiveLocator(
                parameters,
                "listenerInstanceId",
                "listenerObjectPath",
                "Listener",
                out int? listenerInstanceId,
                out string listenerObjectPath,
                out locatorError))
            {
                return EnsureFailureEnvelope(locatorError);
            }

            string componentName = parameters["componentName"]?.ToObject<string>();
            string eventFieldName = parameters["eventFieldName"]?.ToObject<string>();
            string listenerComponentName = parameters["listenerComponentName"]?.ToObject<string>();
            string methodName = parameters["methodName"]?.ToObject<string>();
            if (string.IsNullOrWhiteSpace(componentName)
                || string.IsNullOrWhiteSpace(eventFieldName)
                || string.IsNullOrWhiteSpace(methodName))
            {
                return Error(
                    "Parameters 'componentName', 'eventFieldName', and 'methodName' are required.",
                    "validation_error");
            }

            JObject sourceError = GameObjectToolUtils.FindGameObject(
                instanceId,
                objectPath,
                out GameObject sourceGameObject,
                out _);
            if (sourceError != null)
            {
                return EnsureFailureEnvelope(
                    GameObjectToolUtils.AddResolutionRole(sourceError, "Source"));
            }

            if (!TryResolveComponent(
                sourceGameObject,
                componentName,
                "source",
                out Component sourceComponent,
                out string sourceWarning,
                out JObject componentError))
            {
                return EnsureFailureEnvelope(componentError);
            }

            FieldInfo eventField = FindEventField(sourceComponent.GetType(), eventFieldName);
            if (eventField == null)
            {
                return Error(
                    $"UnityEvent field '{eventFieldName}' was not found on component " +
                    $"'{sourceComponent.GetType().FullName}'.",
                    "not_found_error");
            }
            if (!typeof(UnityEventBase).IsAssignableFrom(eventField.FieldType))
            {
                return Error(
                    $"Field '{eventField.Name}' has type '{eventField.FieldType.FullName}', which is not a UnityEvent.",
                    "validation_error");
            }

            var unityEvent = eventField.GetValue(sourceComponent) as UnityEventBase;
            if (unityEvent == null)
            {
                return Error(
                    $"UnityEvent field '{eventField.Name}' is null and cannot accept a persistent listener.",
                    "validation_error");
            }

            var sourceSerializedObject = new SerializedObject(sourceComponent);
            SerializedProperty eventProperty = sourceSerializedObject.FindProperty(eventField.Name);
            if (eventProperty == null)
            {
                return Error(
                    $"Field '{eventField.Name}' exists by reflection but is not a serialized property.",
                    "validation_error");
            }

            JObject listenerError = GameObjectToolUtils.FindGameObject(
                listenerInstanceId,
                listenerObjectPath,
                out GameObject listenerGameObject,
                out _);
            if (listenerError != null)
            {
                return EnsureFailureEnvelope(
                    GameObjectToolUtils.AddResolutionRole(listenerError, "Listener"));
            }

            UnityEngine.Object listenerTarget = listenerGameObject;
            string listenerWarning = null;
            if (!string.IsNullOrWhiteSpace(listenerComponentName))
            {
                if (!TryResolveComponent(
                    listenerGameObject,
                    listenerComponentName,
                    "listener",
                    out Component listenerComponent,
                    out listenerWarning,
                    out componentError))
                {
                    return EnsureFailureEnvelope(componentError);
                }
                listenerTarget = listenerComponent;
            }

            Type[] eventParameterTypes;
            try
            {
                eventParameterTypes = GetUnityEventParameterTypes(eventField.FieldType);
            }
            catch (InvalidOperationException ex)
            {
                return Error(ex.Message, "validation_error");
            }
            bool hasStaticArgument = parameters.TryGetValue("staticArgument", out JToken staticArgumentToken);
            if (!TryInferBinding(
                eventParameterTypes,
                listenerTarget,
                methodName,
                hasStaticArgument,
                staticArgumentToken,
                out ListenerBinding binding,
                out JObject bindingError))
            {
                return EnsureFailureEnvelope(bindingError);
            }

            int listenerIndex = unityEvent.GetPersistentEventCount();
            try
            {
                AddPersistentListener(unityEvent, listenerTarget, binding);
                unityEvent.SetPersistentListenerState(
                    listenerIndex,
                    UnityEventCallState.RuntimeOnly);
                EditorUtility.SetDirty(sourceComponent);
            }
            catch (Exception ex)
            {
                if (unityEvent.GetPersistentEventCount() > listenerIndex)
                {
                    EditorUnityEventTools.RemovePersistentListener(unityEvent, listenerIndex);
                }
                return Error(
                    $"Failed to add persistent listener '{binding.Signature}': {UnwrapMessage(ex)}",
                    "unity_event_write_error");
            }

            if (!TryReadBackListener(
                sourceComponent,
                eventField.Name,
                listenerIndex,
                listenerTarget,
                binding,
                out JObject readBack,
                out string verificationFailure))
            {
                EditorUnityEventTools.RemovePersistentListener(unityEvent, listenerIndex);
                EditorUtility.SetDirty(sourceComponent);
                return Error(
                    $"Persistent listener read-back verification failed and the added listener was removed: " +
                    verificationFailure,
                    "unity_event_verification_error");
            }

            var warnings = new List<string>();
            if (!string.IsNullOrEmpty(sourceWarning))
            {
                warnings.Add(sourceWarning);
            }
            if (!string.IsNullOrEmpty(listenerWarning))
            {
                warnings.Add(listenerWarning);
            }
            if (!string.IsNullOrEmpty(binding.Warning))
            {
                warnings.Add(binding.Warning);
            }

            var response = new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Wired '{eventField.Name}' to '{binding.Signature}' using " +
                    $"{readBack["mode"]?["name"]} ({readBack["mode"]?["value"]}).",
                ["instanceId"] = sourceGameObject.GetInstanceID(),
                ["componentName"] = sourceComponent.GetType().FullName,
                ["eventFieldName"] = eventField.Name,
                ["listenerIndex"] = listenerIndex,
                ["listenerTarget"] = readBack["listenerTarget"],
                ["methodName"] = readBack["methodName"],
                ["mode"] = readBack["mode"],
                ["callState"] = readBack["callState"],
                ["staticArgument"] = readBack["staticArgument"],
                ["persistentCall"] = readBack["persistentCall"]
            };
            if (warnings.Count > 0)
            {
                response["warnings"] = new JArray(warnings);
            }
            return response;
        }

        private static bool TryReadExclusiveLocator(
            JObject parameters,
            string idName,
            string pathName,
            string role,
            out int? instanceId,
            out string objectPath,
            out JObject error)
        {
            instanceId = parameters[idName]?.Type == JTokenType.Null
                ? null
                : parameters[idName]?.ToObject<int?>();
            objectPath = parameters[pathName]?.ToObject<string>();
            bool hasId = instanceId.HasValue;
            bool hasPath = !string.IsNullOrWhiteSpace(objectPath);
            if (hasId == hasPath)
            {
                error = Error(
                    $"{role} requires exactly one of '{idName}' or '{pathName}'.",
                    "validation_error");
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryResolveComponent(
            GameObject gameObject,
            string componentName,
            string role,
            out Component component,
            out string warning,
            out JObject error)
        {
            component = null;
            Type componentType = ComponentResolver.FindComponentType(
                componentName,
                gameObject,
                out warning,
                out string ambiguityError);
            if (!string.IsNullOrEmpty(ambiguityError))
            {
                error = Error(ambiguityError, $"{role}_component_ambiguity_error");
                return false;
            }

            List<Component> candidates = componentType != null
                ? gameObject.GetComponents(componentType)
                    .Where(candidate => candidate != null)
                    .ToList()
                : gameObject.GetComponents<Component>()
                    .Where(candidate => candidate != null
                        && ComponentNameMatches(candidate.GetType(), componentName))
                    .ToList();
            if (candidates.Count == 0)
            {
                error = Error(
                    $"Component '{componentName}' not found on GameObject '{gameObject.name}'.",
                    "not_found_error");
                return false;
            }
            if (candidates.Count > 1)
            {
                error = CreateComponentAmbiguityError(
                    gameObject,
                    componentName,
                    role,
                    candidates);
                return false;
            }

            component = candidates[0];
            error = null;
            return true;
        }

        private static bool ComponentNameMatches(Type componentType, string requestedName)
        {
            return componentType.Name == requestedName
                || componentType.FullName == requestedName
                || componentType.AssemblyQualifiedName == requestedName;
        }

        private static JObject CreateComponentAmbiguityError(
            GameObject gameObject,
            string componentName,
            string role,
            IReadOnlyCollection<Component> candidates)
        {
            var candidateDetails = new JArray(candidates.Select(candidate => new JObject
            {
                ["instanceId"] = candidate.GetInstanceID(),
                ["componentType"] = candidate.GetType().FullName
            }));
            string candidateSummary = string.Join(", ", candidates.Select(candidate =>
                $"{candidate.GetType().FullName} (instanceId={candidate.GetInstanceID()})"));
            JObject response = Error(
                $"{char.ToUpper(role[0]) + role.Substring(1)} component '{componentName}' is ambiguous " +
                $"on GameObject '{gameObject.name}': {candidateSummary}. " +
                "Use a GameObject with exactly one matching component instance.",
                $"{role}_component_ambiguity_error");
            ((JObject)response["error"])["details"] = new JObject
            {
                ["role"] = role,
                ["candidates"] = candidateDetails
            };
            return response;
        }

        private static FieldInfo FindEventField(Type componentType, string requestedName)
        {
            var candidateNames = new List<string> { requestedName };
            if (!requestedName.StartsWith("m_"))
            {
                candidateNames.Add("m_" + char.ToUpper(requestedName[0]) + requestedName.Substring(1));
            }
            else if (requestedName.Length > 2)
            {
                candidateNames.Add(char.ToLower(requestedName[2]) + requestedName.Substring(3));
            }

            for (Type type = componentType; type != null; type = type.BaseType)
            {
                foreach (string candidateName in candidateNames)
                {
                    FieldInfo field = type.GetField(
                        candidateName,
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly);
                    if (field != null)
                    {
                        return field;
                    }
                }
            }
            return null;
        }

        private static Type[] GetUnityEventParameterTypes(Type eventType)
        {
            for (Type type = eventType; type != null; type = type.BaseType)
            {
                if (type == typeof(UnityEvent))
                {
                    return Type.EmptyTypes;
                }

                if (!type.IsGenericType)
                {
                    continue;
                }

                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(UnityEvent<>)
                    || definition == typeof(UnityEvent<,>)
                    || definition == typeof(UnityEvent<,,>)
                    || definition == typeof(UnityEvent<,,,>))
                {
                    return type.GetGenericArguments();
                }
            }

            throw new InvalidOperationException(
                $"UnityEvent type '{eventType.FullName}' has no supported UnityEvent base signature.");
        }

        private static bool TryInferBinding(
            Type[] eventParameterTypes,
            UnityEngine.Object listenerTarget,
            string methodName,
            bool hasStaticArgument,
            JToken staticArgumentToken,
            out ListenerBinding binding,
            out JObject error)
        {
            var candidates = new List<ListenerBinding>();
            try
            {
                if (!hasStaticArgument)
                {
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        eventParameterTypes,
                        PersistentListenerMode.EventDefined,
                        null);

                    if (eventParameterTypes.Length > 0)
                    {
                        AddCandidate(
                            candidates,
                            listenerTarget,
                            methodName,
                            Type.EmptyTypes,
                            PersistentListenerMode.Void,
                            null);
                    }
                }
                else
                {
                    AddStaticArgumentCandidates(
                        candidates,
                        listenerTarget,
                        methodName,
                        staticArgumentToken,
                        out error);
                    if (error != null)
                    {
                        binding = null;
                        return false;
                    }
                }
            }
            catch (AmbiguousMatchException ex)
            {
                binding = null;
                error = Error(
                    $"Method '{methodName}' is ambiguous on listener type " +
                    $"'{listenerTarget.GetType().FullName}': {ex.Message}",
                    "method_ambiguity_error");
                return false;
            }

            candidates = candidates
                .GroupBy(candidate => candidate.Mode + ":" + candidate.Signature)
                .Select(group => group.First())
                .ToList();
            if (candidates.Count == 0)
            {
                binding = null;
                string expected = hasStaticArgument
                    ? "a supported single static-argument signature"
                    : $"the event signature ({FormatTypes(eventParameterTypes)}) or a zero-argument void signature";
                error = Error(
                    $"Method '{methodName}' with {expected} was not found on listener type " +
                    $"'{listenerTarget.GetType().FullName}'.",
                    "method_not_found");
                return false;
            }
            if (candidates.Count > 1)
            {
                binding = null;
                error = Error(
                    $"Method '{methodName}' is ambiguous for this UnityEvent: " +
                    string.Join(", ", candidates.Select(candidate =>
                        $"{candidate.Mode} => {candidate.Signature}")) +
                    ". Rename the overloads or target an unambiguous method.",
                    "method_ambiguity_error");
                return false;
            }

            binding = candidates[0];
            MethodInfo guardedMethod = UnityEventBase.GetValidMethodInfo(
                listenerTarget,
                methodName,
                binding.ParameterTypes);
            if (guardedMethod == null || guardedMethod != binding.Method)
            {
                binding = null;
                error = Error(
                    $"UnityEvent method validation rejected '{methodName}' on " +
                    $"'{listenerTarget.GetType().FullName}'.",
                    "method_not_found");
                return false;
            }

            error = null;
            return true;
        }

        private static void AddStaticArgumentCandidates(
            List<ListenerBinding> candidates,
            UnityEngine.Object listenerTarget,
            string methodName,
            JToken token,
            out JObject error)
        {
            error = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                foreach (Type parameterType in FindNullableStaticParameterTypes(
                    listenerTarget.GetType(), methodName))
                {
                    PersistentListenerMode mode = parameterType == typeof(string)
                        ? PersistentListenerMode.String
                        : PersistentListenerMode.Object;
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { parameterType },
                        mode,
                        null);
                }
                return;
            }

            switch (token.Type)
            {
                case JTokenType.Boolean:
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { typeof(bool) },
                        PersistentListenerMode.Bool,
                        token.ToObject<bool>());
                    return;
                case JTokenType.Integer:
                    int intValue = token.ToObject<int>();
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { typeof(int) },
                        PersistentListenerMode.Int,
                        intValue);
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { typeof(float) },
                        PersistentListenerMode.Float,
                        Convert.ToSingle(intValue));
                    return;
                case JTokenType.Float:
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { typeof(float) },
                        PersistentListenerMode.Float,
                        token.ToObject<float>());
                    return;
                case JTokenType.String:
                    AddCandidate(
                        candidates,
                        listenerTarget,
                        methodName,
                        new[] { typeof(string) },
                        PersistentListenerMode.String,
                        token.ToObject<string>());
                    return;
                case JTokenType.Object:
                    if (!TryResolveStaticObjectArgument(
                        (JObject)token,
                        out UnityEngine.Object objectArgument,
                        out string objectWarning,
                        out error))
                    {
                        return;
                    }
                    foreach (Type parameterType in FindObjectStaticParameterTypes(
                        listenerTarget.GetType(), methodName, objectArgument))
                    {
                        AddCandidate(
                            candidates,
                            listenerTarget,
                            methodName,
                            new[] { parameterType },
                            PersistentListenerMode.Object,
                            objectArgument,
                            objectWarning);
                    }
                    return;
                default:
                    error = Error(
                        $"staticArgument JSON type '{token.Type}' is not supported. Use bool, number, " +
                        "string, null, or a structured Unity object locator.",
                        "validation_error");
                    return;
            }
        }

        private static void AddCandidate(
            List<ListenerBinding> candidates,
            UnityEngine.Object listenerTarget,
            string methodName,
            Type[] parameterTypes,
            PersistentListenerMode mode,
            object staticArgument,
            string warning = null)
        {
            MethodInfo method = UnityEventBase.GetValidMethodInfo(
                listenerTarget,
                methodName,
                parameterTypes);
            if (method == null
                || method.IsStatic
                || method.ReturnType != typeof(void)
                || !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(parameterTypes))
            {
                return;
            }

            candidates.Add(new ListenerBinding
            {
                Method = method,
                Mode = mode,
                ParameterTypes = parameterTypes,
                StaticArgument = staticArgument,
                Warning = warning
            });
        }

        private static IEnumerable<Type> FindNullableStaticParameterTypes(
            Type listenerType,
            string methodName)
        {
            return EnumerateNamedMethods(listenerType, methodName)
                .Where(method => !method.IsStatic && method.ReturnType == typeof(void))
                .Select(method => method.GetParameters())
                .Where(parameters => parameters.Length == 1)
                .Select(parameters => parameters[0].ParameterType)
                .Where(parameterType => parameterType == typeof(string)
                    || typeof(UnityEngine.Object).IsAssignableFrom(parameterType))
                .Distinct();
        }

        private static IEnumerable<Type> FindObjectStaticParameterTypes(
            Type listenerType,
            string methodName,
            UnityEngine.Object objectArgument)
        {
            Type argumentType = objectArgument.GetType();
            return EnumerateNamedMethods(listenerType, methodName)
                .Where(method => !method.IsStatic && method.ReturnType == typeof(void))
                .Select(method => method.GetParameters())
                .Where(parameters => parameters.Length == 1)
                .Select(parameters => parameters[0].ParameterType)
                .Where(parameterType => typeof(UnityEngine.Object).IsAssignableFrom(parameterType)
                    && parameterType.IsAssignableFrom(argumentType))
                .Distinct();
        }

        private static IEnumerable<MethodInfo> EnumerateNamedMethods(
            Type listenerType,
            string methodName)
        {
            for (Type type = listenerType; type != null; type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly))
                {
                    if (method.Name == methodName)
                    {
                        yield return method;
                    }
                }
            }
        }

        private static bool TryResolveStaticObjectArgument(
            JObject locator,
            out UnityEngine.Object resolved,
            out string warning,
            out JObject error)
        {
            resolved = null;
            warning = null;
            var allowed = new HashSet<string>
            {
                "assetPath", "instanceId", "objectPath", "componentName"
            };
            foreach (JProperty supplied in locator.Properties())
            {
                if (!allowed.Contains(supplied.Name))
                {
                    error = Error(
                        $"Unknown static object locator key '{supplied.Name}'.",
                        "validation_error");
                    return false;
                }
            }

            bool hasAssetPath = !string.IsNullOrWhiteSpace(locator["assetPath"]?.ToObject<string>());
            bool hasInstanceId = locator["instanceId"] != null
                && locator["instanceId"].Type != JTokenType.Null;
            bool hasObjectPath = !string.IsNullOrWhiteSpace(locator["objectPath"]?.ToObject<string>());
            if ((hasAssetPath ? 1 : 0) + (hasInstanceId ? 1 : 0) + (hasObjectPath ? 1 : 0) != 1)
            {
                error = Error(
                    "A static object argument requires exactly one of assetPath, instanceId, or objectPath.",
                    "validation_error");
                return false;
            }

            string componentName = locator["componentName"]?.ToObject<string>();
            if (hasAssetPath)
            {
                if (!string.IsNullOrWhiteSpace(componentName))
                {
                    error = Error(
                        "componentName cannot be combined with an assetPath static argument.",
                        "validation_error");
                    return false;
                }
                string assetPath = locator["assetPath"].ToObject<string>();
                resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (resolved == null)
                {
                    error = Error(
                        $"Static argument asset was not found at '{assetPath}'.",
                        "not_found_error");
                    return false;
                }
                error = null;
                return true;
            }

            int? instanceId = hasInstanceId
                ? locator["instanceId"].ToObject<int?>()
                : null;
            string objectPath = hasObjectPath
                ? locator["objectPath"].ToObject<string>()
                : null;
            JObject findError = GameObjectToolUtils.FindGameObject(
                instanceId,
                objectPath,
                out GameObject gameObject,
                out _);
            if (findError != null)
            {
                error = EnsureFailureEnvelope(
                    GameObjectToolUtils.AddResolutionRole(findError, "Static argument"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(componentName))
            {
                resolved = gameObject;
                error = null;
                return true;
            }

            if (!TryResolveComponent(
                gameObject,
                componentName,
                "static_argument",
                out Component component,
                out warning,
                out error))
            {
                return false;
            }
            resolved = component;
            return true;
        }

        private static void AddPersistentListener(
            UnityEventBase unityEvent,
            UnityEngine.Object listenerTarget,
            ListenerBinding binding)
        {
            Delegate action = CreateUnityAction(listenerTarget, binding.Method, binding.ParameterTypes);
            switch (binding.Mode)
            {
                case PersistentListenerMode.EventDefined:
                    AddDynamicPersistentListener(unityEvent, action, binding.ParameterTypes);
                    return;
                case PersistentListenerMode.Void:
                    EditorUnityEventTools.AddVoidPersistentListener(unityEvent, (UnityAction)action);
                    return;
                case PersistentListenerMode.Int:
                    EditorUnityEventTools.AddIntPersistentListener(
                        unityEvent,
                        (UnityAction<int>)action,
                        (int)binding.StaticArgument);
                    return;
                case PersistentListenerMode.Float:
                    EditorUnityEventTools.AddFloatPersistentListener(
                        unityEvent,
                        (UnityAction<float>)action,
                        (float)binding.StaticArgument);
                    return;
                case PersistentListenerMode.String:
                    EditorUnityEventTools.AddStringPersistentListener(
                        unityEvent,
                        (UnityAction<string>)action,
                        (string)binding.StaticArgument);
                    return;
                case PersistentListenerMode.Bool:
                    EditorUnityEventTools.AddBoolPersistentListener(
                        unityEvent,
                        (UnityAction<bool>)action,
                        (bool)binding.StaticArgument);
                    return;
                case PersistentListenerMode.Object:
                    AddObjectPersistentListener(unityEvent, action, binding);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported inferred PersistentListenerMode '{binding.Mode}'.");
            }
        }

        private static Delegate CreateUnityAction(
            UnityEngine.Object listenerTarget,
            MethodInfo method,
            Type[] parameterTypes)
        {
            Type actionType;
            switch (parameterTypes.Length)
            {
                case 0:
                    actionType = typeof(UnityAction);
                    break;
                case 1:
                    actionType = typeof(UnityAction<>).MakeGenericType(parameterTypes);
                    break;
                case 2:
                    actionType = typeof(UnityAction<,>).MakeGenericType(parameterTypes);
                    break;
                case 3:
                    actionType = typeof(UnityAction<,,>).MakeGenericType(parameterTypes);
                    break;
                case 4:
                    actionType = typeof(UnityAction<,,,>).MakeGenericType(parameterTypes);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"UnityEvent listener arity {parameterTypes.Length} is not supported.");
            }
            return Delegate.CreateDelegate(actionType, listenerTarget, method);
        }

        private static void AddDynamicPersistentListener(
            UnityEventBase unityEvent,
            Delegate action,
            Type[] parameterTypes)
        {
            if (parameterTypes.Length == 0)
            {
                EditorUnityEventTools.AddPersistentListener((UnityEvent)unityEvent, (UnityAction)action);
                return;
            }

            MethodInfo addMethod = typeof(EditorUnityEventTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "AddPersistentListener"
                    && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == parameterTypes.Length)
                .Single();
            addMethod.MakeGenericMethod(parameterTypes)
                .Invoke(null, new object[] { unityEvent, action });
        }

        private static void AddObjectPersistentListener(
            UnityEventBase unityEvent,
            Delegate action,
            ListenerBinding binding)
        {
            MethodInfo addMethod = typeof(EditorUnityEventTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "AddObjectPersistentListener"
                    && method.IsGenericMethodDefinition)
                .Single();
            addMethod.MakeGenericMethod(binding.ParameterTypes[0])
                .Invoke(null, new[] { (object)unityEvent, action, binding.StaticArgument });
        }

        private static bool TryReadBackListener(
            Component sourceComponent,
            string eventFieldName,
            int listenerIndex,
            UnityEngine.Object expectedTarget,
            ListenerBinding binding,
            out JObject readBack,
            out string failure)
        {
            readBack = null;
            var serializedObject = new SerializedObject(sourceComponent);
            SerializedProperty eventProperty = serializedObject.FindProperty(eventFieldName);
            SerializedProperty calls = eventProperty?
                .FindPropertyRelative("m_PersistentCalls")?
                .FindPropertyRelative("m_Calls");
            if (calls == null || !calls.isArray || calls.arraySize <= listenerIndex)
            {
                failure = "The appended m_PersistentCalls.m_Calls element was not present.";
                return false;
            }

            SerializedProperty call = calls.GetArrayElementAtIndex(listenerIndex);
            SerializedProperty target = call.FindPropertyRelative("m_Target");
            SerializedProperty methodName = call.FindPropertyRelative("m_MethodName");
            SerializedProperty mode = call.FindPropertyRelative("m_Mode");
            SerializedProperty callState = call.FindPropertyRelative("m_CallState");
            SerializedProperty arguments = call.FindPropertyRelative("m_Arguments");
            if (target == null || methodName == null || mode == null || callState == null || arguments == null)
            {
                failure = "The appended persistent call did not expose the expected serialized fields.";
                return false;
            }

            if (target.objectReferenceValue != expectedTarget)
            {
                failure = "m_Target did not retain the requested listener identity.";
                return false;
            }
            if (methodName.stringValue != binding.Method.Name)
            {
                failure = $"m_MethodName read back as '{methodName.stringValue}'.";
                return false;
            }
            if (mode.intValue != Convert.ToInt32(binding.Mode))
            {
                failure = $"m_Mode read back as {mode.intValue}, expected {binding.Mode}.";
                return false;
            }

            MethodInfo guardedMethod = UnityEventBase.GetValidMethodInfo(
                target.objectReferenceValue,
                methodName.stringValue,
                binding.ParameterTypes);
            if (guardedMethod == null || guardedMethod != binding.Method)
            {
                failure = "UnityEventBase.GetValidMethodInfo rejected the read-back listener.";
                return false;
            }

            readBack = new JObject
            {
                ["listenerTarget"] = ReadSerializedFieldsTool.SerializedPropertyToJToken(target),
                ["methodName"] = methodName.stringValue,
                ["mode"] = ReadEnum(mode, typeof(PersistentListenerMode)),
                ["callState"] = ReadEnum(callState, typeof(UnityEventCallState)),
                ["staticArgument"] = ReadStaticArgument(arguments, binding.Mode),
                ["persistentCall"] = ReadSerializedFieldsTool.SerializedPropertyToJToken(call)
            };
            failure = null;
            return true;
        }

        private static JObject ReadEnum(SerializedProperty property, Type enumType)
        {
            int value = property.intValue;
            return new JObject
            {
                ["name"] = Enum.GetName(enumType, value) ?? value.ToString(),
                ["value"] = value,
                ["index"] = property.enumValueIndex
            };
        }

        private static JToken ReadStaticArgument(
            SerializedProperty arguments,
            PersistentListenerMode mode)
        {
            switch (mode)
            {
                case PersistentListenerMode.Int:
                    return arguments.FindPropertyRelative("m_IntArgument").intValue;
                case PersistentListenerMode.Float:
                    return arguments.FindPropertyRelative("m_FloatArgument").floatValue;
                case PersistentListenerMode.String:
                    return arguments.FindPropertyRelative("m_StringArgument").stringValue;
                case PersistentListenerMode.Bool:
                    return arguments.FindPropertyRelative("m_BoolArgument").boolValue;
                case PersistentListenerMode.Object:
                    return ReadSerializedFieldsTool.SerializedPropertyToJToken(
                        arguments.FindPropertyRelative("m_ObjectArgument"));
                default:
                    return JValue.CreateNull();
            }
        }

        private static string FormatMethodSignature(MethodInfo method)
        {
            return $"{method.DeclaringType?.FullName}.{method.Name}(" +
                string.Join(", ", method.GetParameters()
                    .Select(parameter => parameter.ParameterType.FullName)) + ")";
        }

        private static string FormatTypes(IEnumerable<Type> types)
        {
            return string.Join(", ", types.Select(type => type.FullName));
        }

        private static string UnwrapMessage(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }
            return current.Message;
        }

        private static JObject Error(string message, string type)
        {
            JObject response = McpUnitySocketHandler.CreateErrorResponse(message, type);
            return EnsureFailureEnvelope(response);
        }

        private static JObject EnsureFailureEnvelope(JObject response)
        {
            if (response == null)
            {
                response = McpUnitySocketHandler.CreateErrorResponse(
                    "UnityEvent wiring failed without an error response.",
                    "internal_error");
            }

            JObject nestedError = response["error"] as JObject;
            string message = response["message"]?.ToObject<string>()
                ?? nestedError?["message"]?.ToObject<string>()
                ?? "UnityEvent wiring failed.";
            if (nestedError == null)
            {
                nestedError = new JObject
                {
                    ["type"] = "unity_event_error",
                    ["message"] = message
                };
                response["error"] = nestedError;
            }
            else
            {
                nestedError["message"] = message;
                if (nestedError["type"] == null)
                {
                    nestedError["type"] = "unity_event_error";
                }
            }

            response["success"] = false;
            response["message"] = message;
            return response;
        }
    }
}
