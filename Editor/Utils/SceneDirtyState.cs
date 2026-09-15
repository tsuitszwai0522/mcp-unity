using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;

namespace McpUnity.Utils
{
    /// <summary>
    /// A loaded scene that has unsaved changes. Path is empty for an untitled scene.
    /// </summary>
    internal sealed class DirtySceneInfo
    {
        public string Name;
        public string Path;

        public bool IsUntitled => string.IsNullOrEmpty(Path);

        public static DirtySceneInfo From(Scene scene)
        {
            return new DirtySceneInfo { Name = scene.name, Path = scene.path };
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["name"] = Name ?? string.Empty,
                ["path"] = IsUntitled ? JValue.CreateNull() : new JValue(Path),
                ["untitled"] = IsUntitled
            };
        }

        public string Describe()
        {
            return IsUntitled
                ? $"'{(string.IsNullOrEmpty(Name) ? "Untitled" : Name)}' (untitled)"
                : $"'{Path}'";
        }
    }

    /// <summary>
    /// Reads unsaved-change state of the scenes loaded in the Editor hierarchy.
    /// Preview scenes are not part of SceneManager's list and are not reported.
    /// </summary>
    internal static class SceneDirtyState
    {
        public static List<Scene> GetDirtyLoadedScenes()
        {
            var dirty = new List<Scene>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                {
                    dirty.Add(scene);
                }
            }
            return dirty;
        }

        public static IReadOnlyList<DirtySceneInfo> DescribeDirtyLoadedScenes()
        {
            return GetDirtyLoadedScenes().Select(DirtySceneInfo.From).ToList();
        }

        public static JArray ToJson(IEnumerable<DirtySceneInfo> scenes)
        {
            return new JArray(scenes.Select(scene => scene.ToJson()));
        }
    }
}
