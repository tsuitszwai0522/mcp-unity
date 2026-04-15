using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Shared helpers for Addressables profile tools — profile id resolution.
    /// </summary>
    internal static class AddrProfileHelper
    {
        public static string ResolveProfileId(
            AddressableAssetSettings settings,
            string profileName,
            out JObject error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(profileName))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'profile' must be a non-empty string",
                    "validation_error");
                return null;
            }

            string id = settings.profileSettings.GetProfileId(profileName);
            if (!string.IsNullOrEmpty(id)) return id;

            var available = string.Join(", ", settings.profileSettings.GetAllProfileNames());
            error = McpUnitySocketHandler.CreateErrorResponse(
                $"Profile '{profileName}' not found. Available: [{available}]",
                "profile_not_found");
            return null;
        }
    }

    /// <summary>
    /// List all Addressables profiles with every profile's resolved variable map.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrListProfilesTool : McpToolBase
    {
        public AddrListProfilesTool()
        {
            Name = "addr_list_profiles";
            Description = "List all Unity Addressables profiles and their variable values";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var profileSettings = settings.profileSettings;
            var variableNames = profileSettings.GetVariableNames();
            string activeProfileId = settings.activeProfileId;
            string activeProfileName = profileSettings.GetProfileName(activeProfileId);

            var profiles = new JArray();
            foreach (var profileName in profileSettings.GetAllProfileNames())
            {
                string profileId = profileSettings.GetProfileId(profileName);
                var variables = new JObject();
                foreach (var variableName in variableNames)
                {
                    variables[variableName] = profileSettings.GetValueByName(profileId, variableName);
                }

                profiles.Add(new JObject
                {
                    ["id"] = profileId,
                    ["name"] = profileName,
                    ["isActive"] = profileId == activeProfileId,
                    ["variables"] = variables
                });
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Found {profiles.Count} profile(s), active: '{activeProfileName}'",
                ["activeProfile"] = activeProfileName,
                ["activeProfileId"] = activeProfileId,
                ["variableNames"] = new JArray(variableNames),
                ["profiles"] = profiles
            };
        }
    }

    /// <summary>
    /// Query the currently active Addressables profile.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrGetActiveProfileTool : McpToolBase
    {
        public AddrGetActiveProfileTool()
        {
            Name = "addr_get_active_profile";
            Description = "Get the currently active Unity Addressables profile with its resolved variable values";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            var profileSettings = settings.profileSettings;
            string activeProfileId = settings.activeProfileId;
            string activeProfileName = profileSettings.GetProfileName(activeProfileId);

            var variables = new JObject();
            foreach (var variableName in profileSettings.GetVariableNames())
            {
                variables[variableName] = profileSettings.GetValueByName(activeProfileId, variableName);
            }

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Active profile: '{activeProfileName}'",
                ["id"] = activeProfileId,
                ["name"] = activeProfileName,
                ["variables"] = variables
            };
        }
    }

    /// <summary>
    /// Switch the active Addressables profile by name.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrSetActiveProfileTool : McpToolBase
    {
        public AddrSetActiveProfileTool()
        {
            Name = "addr_set_active_profile";
            Description = "Switch the active Unity Addressables profile by name";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""profile"": { ""type"": ""string"", ""description"": ""Profile name (must exist)"" }
            },
            ""required"": [""profile""]
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            string profileName = parameters["profile"]?.ToString();
            string newId = AddrProfileHelper.ResolveProfileId(settings, profileName, out var resolveError);
            if (newId == null) return resolveError;

            string previousId = settings.activeProfileId;
            string previousName = settings.profileSettings.GetProfileName(previousId);

            if (previousId == newId)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Active profile is already '{profileName}'",
                    ["changed"] = false,
                    ["activeProfile"] = profileName,
                    ["previousProfile"] = previousName
                };
            }

            settings.activeProfileId = newId;
            // The setter itself fires SetDirty/ModificationEvent.ActiveProfileSet, but we
            // still flush to disk so the change survives a domain reload.
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = $"Active profile changed '{previousName}' -> '{profileName}'",
                ["changed"] = true,
                ["activeProfile"] = profileName,
                ["previousProfile"] = previousName
            };
        }
    }

    /// <summary>
    /// Set one or more profile variable values on a named profile. When the
    /// variable does not exist yet, it is created at the profile-settings level
    /// (which makes it available on every profile) before being set.
    /// </summary>
    [McpUnityFirstParty]
    public class AddrSetProfileVariableTool : McpToolBase
    {
        public AddrSetProfileVariableTool()
        {
            Name = "addr_set_profile_variable";
            Description = "Set an Addressables profile variable (e.g. Remote.LoadPath) on a named profile. Pass create_if_missing=true to create the variable at the profile-settings level; newly-created variables are added to ALL profiles, not only the named profile.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""profile"": { ""type"": ""string"", ""description"": ""Profile name (must exist)"" },
                ""variable"": { ""type"": ""string"", ""description"": ""Variable name (e.g. Remote.LoadPath)"" },
                ""value"": { ""type"": ""string"", ""description"": ""New value (may contain [BuildTarget] tokens)"" },
                ""create_if_missing"": { ""type"": ""boolean"", ""description"": ""If true, create the variable at profile-settings level when missing. WARNING: newly-created variables are added to ALL profiles, not only the named profile. Default false — error instead."" }
            },
            ""required"": [""profile"", ""variable"", ""value""]
        }");

        public override JObject Execute(JObject parameters)
        {
            var settings = AddrHelper.TryGetSettings(out var error);
            if (settings == null) return error;

            string profileName = parameters["profile"]?.ToString();
            string variable = parameters["variable"]?.ToString();
            // Tokens use `value != null` rather than IsNullOrWhiteSpace — an empty
            // string is a valid (if unusual) override and should not be treated as
            // missing.
            JToken valueToken = parameters["value"];
            bool createIfMissing = parameters["create_if_missing"]?.ToObject<bool>() ?? false;

            if (string.IsNullOrWhiteSpace(variable))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'variable' must be a non-empty string",
                    "validation_error");
            }
            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Required parameter 'value' must be provided (string)",
                    "validation_error");
            }
            string value = valueToken.ToString();

            string profileId = AddrProfileHelper.ResolveProfileId(settings, profileName, out var resolveError);
            if (profileId == null) return resolveError;

            var profileSettings = settings.profileSettings;
            var variableNames = profileSettings.GetVariableNames();
            bool created = false;
            if (!variableNames.Contains(variable))
            {
                if (!createIfMissing)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Profile variable '{variable}' does not exist. Pass create_if_missing=true to create it. Available: [{string.Join(", ", variableNames)}]",
                        "variable_not_found");
                }
                profileSettings.CreateValue(variable, value);
                created = true;
            }

            string previousValue = created ? null : profileSettings.GetValueByName(profileId, variable);
            profileSettings.SetValue(profileId, variable, value);

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = created
                    ? $"Created variable '{variable}' and set '{profileName}' = '{value}'"
                    : $"Updated '{profileName}'.'{variable}' from '{previousValue}' to '{value}'",
                ["profile"] = profileName,
                ["variable"] = variable,
                ["previousValue"] = previousValue,
                ["value"] = value,
                ["created"] = created
            };
        }
    }
}
