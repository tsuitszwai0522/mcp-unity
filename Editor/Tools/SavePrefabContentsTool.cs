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
                          "By default saves changes back to the .prefab asset. Set discard=true to abandon changes.";
            IsAsync = false;
        }

        public override JObject Execute(JObject parameters)
        {
            bool discard = parameters["discard"]?.ToObject<bool>() ?? false;

            if (!PrefabEditingService.IsEditing)
            {
                return McpUnitySocketHandler.CreateErrorResponse(
                    "No Prefab is currently being edited. Call open_prefab_contents first.",
                    "validation_error"
                );
            }

            string prefabPath = PrefabEditingService.AssetPath;

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
