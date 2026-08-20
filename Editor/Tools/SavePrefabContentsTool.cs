using System;
using McpUnity.Unity;
using McpUnity.Services;
using Newtonsoft.Json.Linq;

namespace McpUnity.Tools
{
    /// <summary>
    /// Tool for saving or discarding Prefab contents that were opened with open_prefab_contents.
    /// Saves modifications back to the .prefab asset so all instances automatically reflect changes.
    /// </summary>
    public class SavePrefabContentsTool : McpToolBase
    {
        public SavePrefabContentsTool()
        {
            Name = "save_prefab_contents";
            Description = "Saves or discards changes to a Prefab that was opened with open_prefab_contents. " +
                          "By default saves changes back to the .prefab asset. Set discard=true to abandon " +
                          "an active session or acknowledge and clear a lost session.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            bool discard = parameters["discard"]?.ToObject<bool>() ?? false;

            if (PrefabEditingService.Status == PrefabEditingSessionStatus.Lost)
            {
                if (!discard)
                    return PrefabSessionScope.CreateSessionLostError();

                string lostPrefabPath = PrefabEditingService.LostAssetPath;
                bool previewWasUnloaded;
                try
                {
                    previewWasUnloaded = PrefabEditingService.DiscardWithCleanupResult();
                }
                catch (Exception ex)
                {
                    return McpUnitySocketHandler.CreateErrorResponse(
                        $"Failed to acknowledge the lost Prefab editing session for " +
                        $"'{lostPrefabPath}': {ex.Message}",
                        "prefab_cleanup_error");
                }

                string cleanupMessage = previewWasUnloaded
                    ? "A live preview root was unloaded before the recovery record was cleared; " +
                      "any unsaved preview edits were discarded."
                    : "No live preview root remained to unload; only the recovery record was cleared.";
                return new JObject
                {
                    ["success"] = true,
                    ["type"] = "text",
                    ["message"] = $"Acknowledged and cleared the lost Prefab editing session for '{lostPrefabPath}'. " +
                                  cleanupMessage,
                    ["prefabPath"] = lostPrefabPath,
                    ["discarded"] = true,
                    ["lostSessionAcknowledged"] = true
                };
            }

            JObject sessionError = PrefabSessionScope.RequireActiveSession(
                out _, out string prefabPath);
            if (sessionError != null) return sessionError;

            try
            {
                if (discard)
                {
                    PrefabEditingService.Discard();
                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "text",
                        ["message"] = $"Discarded changes to Prefab: '{prefabPath}'.",
                        ["prefabPath"] = prefabPath,
                        ["discarded"] = true
                    };
                }
                else
                {
                    PrefabEditingService.Save();
                    return new JObject
                    {
                        ["success"] = true,
                        ["type"] = "text",
                        ["message"] = $"Saved Prefab contents to: '{prefabPath}'. All instances will reflect the changes.",
                        ["prefabPath"] = prefabPath,
                        ["discarded"] = false
                    };
                }
            }
            catch (PrefabEditingCleanupException ex)
            {
                return new JObject
                {
                    ["error"] = new JObject
                    {
                        ["type"] = "prefab_cleanup_error",
                        ["message"] = ex.Message,
                        ["details"] = new JObject
                        {
                            ["saveCompleted"] = true,
                            ["sessionRecordPreserved"] = true,
                            ["sessionStatus"] = ex.SessionStatus.ToString()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    $"Failed to {(discard ? "discard" : "save")} Prefab contents: {ex.Message}",
                    "internal_error"
                );
            }
        }
    }
}
