using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>Scene folder/save and build-list helpers for the zone baker.</summary>
    public static class ZoneBuildFiles
    {
        public static void EnsureSceneFolder()
        {
            if (!Directory.Exists("Assets/Scenes"))
                Directory.CreateDirectory("Assets/Scenes");
        }

        public static void SaveScene(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new System.InvalidOperationException($"Could not save zone scene to '{path}'.");
        }

        public static void AddToBuildSettings(params string[] paths)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (string path in paths)
            {
                if (scenes.Exists(s => s.path == path))
                    continue;

                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
