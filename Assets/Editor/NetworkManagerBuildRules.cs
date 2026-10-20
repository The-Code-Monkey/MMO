using System.Collections.Generic;
using System.Linq;
using FishNet.Object;

namespace MMO.Editor
{
    public readonly struct SceneBuildInfo
    {
        public string Path { get; }
        public bool Enabled { get; }
        public bool ContainsNetworkManager { get; }

        /// <summary>
        /// The contents of the scene NetworkManager's spawnable collection, gathered while the
        /// scene was open. Null when the scene has no NetworkManager (so its wiring was never
        /// gathered and the missing-manager rule owns the message).
        /// </summary>
        public IReadOnlyList<NetworkObject> SpawnablePrefabs { get; }

        /// <summary>
        /// True when the scene's NetworkManager carries a <c>PlayerSpawner</c> whose player
        /// prefab is set. False when either is missing.
        /// </summary>
        public bool HasPlayerSpawnerPrefab { get; }

        public SceneBuildInfo(string path, bool enabled, bool containsNetworkManager)
            : this(path, enabled, containsNetworkManager, null, false)
        {
        }

        public SceneBuildInfo(
            string path,
            bool enabled,
            bool containsNetworkManager,
            IReadOnlyList<NetworkObject> spawnablePrefabs,
            bool hasPlayerSpawnerPrefab)
        {
            Path = path;
            Enabled = enabled;
            ContainsNetworkManager = containsNetworkManager;
            SpawnablePrefabs = spawnablePrefabs;
            HasPlayerSpawnerPrefab = hasPlayerSpawnerPrefab;
        }
    }

    public static class NetworkManagerBuildRules
    {
        public static IReadOnlyList<string> FindViolations(IEnumerable<SceneBuildInfo> scenes)
        {
            return scenes
                .Where(s => s.Enabled && !s.ContainsNetworkManager)
                .Select(s => s.Path)
                .ToList();
        }

        /// <summary>
        /// Fails a scene whose player wiring would come up dead: a spawnable collection with a
        /// null (or destroyed) entry — the "Prefab on id N is null" a stale bake leaves behind —
        /// or a PlayerSpawner with no player prefab to spawn. Scenes without a NetworkManager
        /// are reported by <see cref="FindViolations"/> instead, so they are skipped here.
        /// </summary>
        public static IReadOnlyList<string> FindPrefabViolations(IEnumerable<SceneBuildInfo> scenes)
        {
            var violations = new List<string>();

            foreach (SceneBuildInfo scene in scenes)
            {
                if (!scene.Enabled || !scene.ContainsNetworkManager)
                    continue;

                if (scene.SpawnablePrefabs == null)
                {
                    violations.Add(
                        $"{scene.Path}: NetworkManager has no SpawnablePrefabs collection.");
                    continue;
                }

                for (int i = 0; i < scene.SpawnablePrefabs.Count; i++)
                {
                    if (scene.SpawnablePrefabs[i] == null)
                    {
                        violations.Add(
                            $"{scene.Path}: SpawnablePrefabs[{i}] is null (stale or missing " +
                            $"prefab reference). Rebake the scene.");
                    }
                }

                if (!scene.HasPlayerSpawnerPrefab)
                {
                    violations.Add(
                        $"{scene.Path}: PlayerSpawner has no player prefab, so connecting " +
                        $"clients would spawn nothing. Rebake the scene.");
                }
            }

            return violations;
        }
    }
}
