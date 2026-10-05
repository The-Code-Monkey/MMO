using System.Collections.Generic;
using FishNet.Managing;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Fails the build when an enabled scene ships without a NetworkManager. Without this
    /// a scene that was never baked would reach a player as a dead client.
    /// </summary>
    public sealed class NetworkManagerBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindViolations(CollectSceneInfo());
            if (violations.Count > 0)
            {
                throw new BuildFailedException(
                    "Enabled build scenes are missing a NetworkManager: " + string.Join(", ", violations));
            }
        }

        internal static IEnumerable<SceneBuildInfo> CollectSceneInfo()
        {
            var results = new List<SceneBuildInfo>();
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                bool contains = false;

                if (buildScene.enabled)
                {
                    Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                    try
                    {
                        contains = NetworkManagerSceneBaker.TryFind(scene, out NetworkManager _);
                    }
                    finally
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }

                results.Add(new SceneBuildInfo(buildScene.path, buildScene.enabled, contains));
            }

            return results;
        }
    }
}