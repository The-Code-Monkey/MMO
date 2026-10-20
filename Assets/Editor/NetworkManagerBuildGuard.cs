using System.Collections.Generic;
using System.Reflection;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Fails the build when an enabled scene ships without a NetworkManager, or with player
    /// wiring that would come up dead (a stale entry in its SpawnablePrefabs, or a PlayerSpawner
    /// with no prefab). Without these a scene that was never baked would reach a player as a
    /// dead client.
    /// </summary>
    public sealed class NetworkManagerBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            IReadOnlyList<SceneBuildInfo> scenes = CollectSceneInfo();

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindViolations(scenes);
            if (violations.Count > 0)
            {
                throw new BuildFailedException(
                    "Enabled build scenes are missing a NetworkManager: " + string.Join(", ", violations));
            }

            IReadOnlyList<string> prefabViolations = NetworkManagerBuildRules.FindPrefabViolations(scenes);
            if (prefabViolations.Count > 0)
            {
                throw new BuildFailedException(
                    "Enabled build scenes have broken player wiring:\n" +
                    string.Join("\n", prefabViolations));
            }
        }

        internal static IReadOnlyList<SceneBuildInfo> CollectSceneInfo()
        {
            var results = new List<SceneBuildInfo>();
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                bool contains = false;
                IReadOnlyList<NetworkObject> spawnables = null;
                bool hasPlayerPrefab = false;

                if (buildScene.enabled)
                {
                    Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                    try
                    {
                        contains = NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager);
                        if (contains)
                        {
                            // Open scenes deserialize stale references as null, which is exactly
                            // what the rule needs to see.
                            spawnables = (manager.SpawnablePrefabs as SinglePrefabObjects)?.Prefabs;
                            hasPlayerPrefab = ReadPlayerPrefab(manager) != null;
                        }
                    }
                    finally
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }

                results.Add(new SceneBuildInfo(
                    buildScene.path, buildScene.enabled, contains, spawnables, hasPlayerPrefab));
            }

            return results;
        }

        /// <summary>
        /// FishNet keeps the spawner's prefab private and offers no getter for it, so the build
        /// guard reads the field the same way the tests do.
        /// </summary>
        private static NetworkObject ReadPlayerPrefab(NetworkManager manager)
        {
            PlayerSpawner spawner = manager.GetComponent<PlayerSpawner>();
            if (spawner == null)
                return null;

            FieldInfo field = typeof(PlayerSpawner).GetField(
                "_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

            return field?.GetValue(spawner) as NetworkObject;
        }
    }
}
