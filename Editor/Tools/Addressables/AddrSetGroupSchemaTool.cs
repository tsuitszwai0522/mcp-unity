using System;
using System.Collections.Generic;
using System.Linq;
using McpUnity.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace McpUnity.Tools.Addressables
{
    /// <summary>
    /// Partial update of a group's BundledAssetGroupSchema — only provided fields
    /// are applied. Returns a diff so callers can see what actually changed (and
    /// verify dry-runs).
    /// </summary>
    [McpUnityFirstParty]
    public class AddrSetGroupSchemaTool : McpToolBase
    {
        public AddrSetGroupSchemaTool()
        {
            Name = "addr_set_group_schema";
            Description = "Update fields on an Addressables group's BundledAssetGroupSchema (compression, include_in_build, packed_mode, bundle_naming, cache flags, build_path/load_path profile variables). Partial — only provided fields change. Supports dry_run. Validate-all then apply: a failing field aborts the request with zero side effects.";
        }

        public override JObject ParameterSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""group"": { ""type"": ""string"", ""description"": ""Group name (must exist)"" },
                ""dry_run"": { ""type"": ""boolean"", ""description"": ""If true, compute the diff without saving (default false)"" },
                ""values"": {
                    ""type"": ""object"",
                    ""description"": ""Partial set of schema fields to apply. Only provided keys are changed."",
                    ""properties"": {
                        ""compression"": { ""type"": ""string"", ""description"": ""Uncompressed | LZ4 | LZMA"" },
                        ""include_in_build"": { ""type"": ""boolean"" },
                        ""packed_mode"": { ""type"": ""string"", ""description"": ""PackTogether | PackSeparately | PackTogetherByLabel"" },
                        ""bundle_naming"": { ""type"": ""string"", ""description"": ""AppendHash | NoHash | OnlyHash | FileNameHash"" },
                        ""use_asset_bundle_cache"": { ""type"": ""boolean"" },
                        ""use_unitywebrequest_for_local_bundles"": { ""type"": ""boolean"" },
                        ""retry_count"": { ""type"": ""integer"", ""minimum"": 0 },
                        ""timeout"": { ""type"": ""integer"", ""minimum"": 0 },
                        ""build_path"": { ""type"": ""string"", ""description"": ""Profile variable name — e.g. Local.BuildPath or Remote.BuildPath"" },
                        ""load_path"": { ""type"": ""string"", ""description"": ""Profile variable name — e.g. Local.LoadPath or Remote.LoadPath"" }
                    }
                }
            },
            ""required"": [""group"", ""values""]
        }");

        /// <summary>
        /// One planned change. <see cref="Apply"/> is only invoked after every
        /// field in the payload has validated successfully, so a mid-request
        /// failure can never leave the schema half-mutated.
        /// </summary>
        private sealed class SchemaChange
        {
            public string Field;
            public JToken From;
            public JToken To;
            public Action Apply;
        }

        public override JObject Execute(JObject parameters)
        {
            string groupName = parameters["group"]?.ToString();
            var settings = AddrHelper.TryGetSettings(out var settingsError);
            if (settings == null) return settingsError;

            var group = AddrHelper.ResolveGroup(settings, groupName, out var groupError);
            if (group == null) return groupError;

            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Group '{groupName}' does not have a BundledAssetGroupSchema attached",
                    "schema_not_found");
            }

            var values = parameters["values"] as JObject;
            if (values == null || values.Count == 0)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "Parameter 'values' must be a non-empty object",
                    "validation_error");
            }

            bool dryRun = parameters["dry_run"]?.ToObject<bool>() ?? false;

            // Phase 1: validate every field and build a change plan. Any failure
            // aborts BEFORE mutating `schema`, so callers never see a
            // validation_error response with partial in-memory state.
            var variableNames = settings.profileSettings.GetVariableNames();
            var changes = new List<SchemaChange>();
            foreach (var prop in values.Properties())
            {
                var planError = PlanField(settings, schema, variableNames, prop.Name, prop.Value, changes);
                if (planError != null) return planError;
            }

            // Phase 2: apply.
            if (!dryRun)
            {
                foreach (var change in changes) change.Apply();
                if (changes.Count > 0)
                {
                    AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.GroupSchemaModified);
                }
            }

            var diff = new JObject();
            foreach (var change in changes)
            {
                diff[change.Field] = new JObject { ["from"] = change.From, ["to"] = change.To };
            }
            var appliedNames = changes.Select(c => c.Field).ToList();

            return new JObject
            {
                ["success"] = true,
                ["type"] = "text",
                ["message"] = appliedNames.Count == 0
                    ? $"No fields changed on group '{group.Name}' (all provided values already matched current state)"
                    : (dryRun
                        ? $"Dry-run: would change {appliedNames.Count} field(s) on group '{group.Name}' ({string.Join(", ", appliedNames)})"
                        : $"Updated {appliedNames.Count} field(s) on group '{group.Name}' ({string.Join(", ", appliedNames)})"),
                ["group"] = group.Name,
                ["dryRun"] = dryRun,
                ["changed"] = new JArray(appliedNames),
                ["diff"] = diff
            };
        }

        /// <summary>
        /// Validate one field and, if it actually differs from the current schema
        /// value, append a <see cref="SchemaChange"/> to <paramref name="changes"/>.
        /// Returns an error JObject on validation failure; null otherwise.
        /// </summary>
        private static JObject PlanField(
            AddressableAssetSettings settings,
            BundledAssetGroupSchema schema,
            List<string> variableNames,
            string field,
            JToken value,
            List<SchemaChange> changes)
        {
            switch (field)
            {
                case "compression":
                {
                    if (!TryParseEnum<BundledAssetGroupSchema.BundleCompressionMode>(value, field, out var parsed, out var err)) return err;
                    var before = schema.Compression;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before.ToString(),
                        To = parsed.ToString(),
                        Apply = () => schema.Compression = parsed,
                    });
                    return null;
                }
                case "include_in_build":
                {
                    if (!TryParseBool(value, field, out var parsed, out var err)) return err;
                    var before = schema.IncludeInBuild;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before,
                        To = parsed,
                        Apply = () => schema.IncludeInBuild = parsed,
                    });
                    return null;
                }
                case "packed_mode":
                {
                    if (!TryParseEnum<BundledAssetGroupSchema.BundlePackingMode>(value, field, out var parsed, out var err)) return err;
                    var before = schema.BundleMode;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before.ToString(),
                        To = parsed.ToString(),
                        Apply = () => schema.BundleMode = parsed,
                    });
                    return null;
                }
                case "bundle_naming":
                {
                    if (!TryParseEnum<BundledAssetGroupSchema.BundleNamingStyle>(value, field, out var parsed, out var err)) return err;
                    var before = schema.BundleNaming;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before.ToString(),
                        To = parsed.ToString(),
                        Apply = () => schema.BundleNaming = parsed,
                    });
                    return null;
                }
                case "use_asset_bundle_cache":
                {
                    if (!TryParseBool(value, field, out var parsed, out var err)) return err;
                    var before = schema.UseAssetBundleCache;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before,
                        To = parsed,
                        Apply = () => schema.UseAssetBundleCache = parsed,
                    });
                    return null;
                }
                case "use_unitywebrequest_for_local_bundles":
                {
                    if (!TryParseBool(value, field, out var parsed, out var err)) return err;
                    var before = schema.UseUnityWebRequestForLocalBundles;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before,
                        To = parsed,
                        Apply = () => schema.UseUnityWebRequestForLocalBundles = parsed,
                    });
                    return null;
                }
                case "retry_count":
                {
                    if (!TryParseNonNegativeInt(value, field, out var parsed, out var err)) return err;
                    var before = schema.RetryCount;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before,
                        To = parsed,
                        Apply = () => schema.RetryCount = parsed,
                    });
                    return null;
                }
                case "timeout":
                {
                    if (!TryParseNonNegativeInt(value, field, out var parsed, out var err)) return err;
                    var before = schema.Timeout;
                    if (before == parsed) return null;
                    changes.Add(new SchemaChange
                    {
                        Field = field,
                        From = before,
                        To = parsed,
                        Apply = () => schema.Timeout = parsed,
                    });
                    return null;
                }
                case "build_path":
                    return PlanProfileReference(settings, schema.BuildPath, variableNames, field, value, changes);
                case "load_path":
                    return PlanProfileReference(settings, schema.LoadPath, variableNames, field, value, changes);
                default:
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Unknown schema field '{field}'. Allowed: compression, include_in_build, packed_mode, bundle_naming, use_asset_bundle_cache, use_unitywebrequest_for_local_bundles, retry_count, timeout, build_path, load_path",
                        "validation_error");
            }
        }

        private static JObject PlanProfileReference(
            AddressableAssetSettings settings,
            ProfileValueReference reference,
            List<string> variableNames,
            string field,
            JToken value,
            List<SchemaChange> changes)
        {
            if (value == null || value.Type != JTokenType.String)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Field '{field}' must be a profile variable name (string)",
                    "validation_error");
            }

            string variableName = value.ToString();
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Field '{field}' must be a non-empty profile variable name",
                    "validation_error");
            }

            string before = reference.GetName(settings);
            if (before == variableName) return null;

            if (!variableNames.Contains(variableName))
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Profile variable '{variableName}' does not exist. Available: [{string.Join(", ", variableNames)}]",
                    "variable_not_found");
            }

            changes.Add(new SchemaChange
            {
                Field = field,
                From = before,
                To = variableName,
                Apply = () =>
                {
                    if (!reference.SetVariableByName(settings, variableName))
                    {
                        throw new InvalidOperationException(
                            $"Failed to set profile variable '{variableName}' on {field}");
                    }
                },
            });
            return null;
        }

        /// <summary>
        /// Strict boolean parser. Only accepts JSON boolean tokens — no string
        /// coercion, no 0/1 integer coercion. Keeps the Unity-side contract in
        /// lockstep with the zod schema on the Node side.
        /// </summary>
        private static bool TryParseBool(JToken value, string field, out bool parsed, out JObject error)
        {
            parsed = false;
            error = null;
            if (value?.Type == JTokenType.Boolean)
            {
                parsed = value.Value<bool>();
                return true;
            }

            error = McpUnitySocketHandler.CreateErrorResponse(
                $"Field '{field}' must be a boolean (got {(value?.Type.ToString() ?? "null").ToLowerInvariant()})",
                "validation_error");
            return false;
        }

        /// <summary>
        /// Strict non-negative integer parser. Rejects JSON strings, floats, and
        /// negative integers. Addressables schema fields `retry_count` and
        /// `timeout` are counts/seconds — negatives would be meaningless.
        /// </summary>
        private static bool TryParseNonNegativeInt(JToken value, string field, out int parsed, out JObject error)
        {
            parsed = 0;
            error = null;
            if (value?.Type == JTokenType.Integer)
            {
                parsed = value.Value<int>();
                if (parsed >= 0) return true;
            }

            error = McpUnitySocketHandler.CreateErrorResponse(
                $"Field '{field}' must be a non-negative integer",
                "validation_error");
            return false;
        }

        /// <summary>
        /// Strict enum parser. Rejects numeric strings (which would bypass the
        /// allowed-names list and write undefined enum values into the schema)
        /// and runs a final <see cref="Enum.IsDefined"/> check so synonyms like
        /// trailing whitespace or casing never smuggle in undefined members.
        /// </summary>
        private static bool TryParseEnum<TEnum>(JToken value, string field, out TEnum parsed, out JObject error)
            where TEnum : struct, Enum
        {
            parsed = default;
            error = null;

            if (value == null || value.Type != JTokenType.String)
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Field '{field}' must be one of: {string.Join(" | ", Enum.GetNames(typeof(TEnum)))}",
                    "validation_error");
                return false;
            }

            string raw = value.ToString();
            if (string.IsNullOrWhiteSpace(raw)
                || int.TryParse(raw, out _)
                || !Enum.TryParse(raw, true, out parsed)
                || !Enum.IsDefined(typeof(TEnum), parsed))
            {
                error = McpUnitySocketHandler.CreateErrorResponse(
                    $"Field '{field}' must be one of: {string.Join(" | ", Enum.GetNames(typeof(TEnum)))} (got '{raw}')",
                    "validation_error");
                return false;
            }

            return true;
        }
    }
}
