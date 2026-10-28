using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Wires a zone scene to the server: adds the per-scene <see cref="WorldBootstrap"/>, makes
    /// sure every authored spawn point carries a <see cref="PlayerSpawnPoint"/>, and registers the
    /// prefabs the scene's markers reference so server and client agree on their prefab ids.
    /// Idempotent — running it twice leaves the scene exactly as the first run did.
    ///
    /// <para>
    /// The zone baker (<see cref="ZoneSceneBaker"/>) calls <see cref="EnsureInActiveScene"/> while
    /// it assembles a scene, so a generated zone is wired by construction. This menu is for
    /// hand-authored scenes: point it at the open scene and it connects the same pieces.
    /// </para>
    /// </summary>
    public static class WorldBootstrapBaker
    {
        public const string MenuPath = "Tools/MMO/Wire Zone With World Bootstrap";

        /// <summary>Name of the root every zone baker parents its spawn points under.</summary>
        public const string SpawnPointsName = ZoneSceneBaker.SpawnPointsName;

        [MenuItem(MenuPath)]
        public static void WireActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (!NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager))
            {
                Debug.LogError(
                    $"Scene '{scene.name}' has no NetworkManager; run '{NetworkManagerSceneBaker.MenuPath}' first.",
                    manager);
                return;
            }

            WorldBootstrap bootstrap = EnsureInActiveScene();
            int spawnPoints = EnsureSpawnPointComponents(scene);
            int registered = RegisterMarkerPrefabs(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.SetDirty(manager.gameObject);

            if (bootstrap != null)
            {
                Selection.activeGameObject = bootstrap.gameObject;
                EditorGUIUtility.PingObject(bootstrap.gameObject);
            }

            Debug.Log($"Wired '{scene.name}': WorldBootstrap ready, {spawnPoints} spawn point(s) tagged, {registered} marker prefab(s) registered.");
        }

        /// <summary>
        /// Adds a <see cref="WorldBootstrap"/> to the active scene's NetworkManager if it has none,
        /// and returns the existing or new component. Returns null when the scene has no manager.
        /// </summary>
        public static WorldBootstrap EnsureInActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager))
                return null;

            WorldBootstrap bootstrap = manager.GetComponent<WorldBootstrap>();
            if (bootstrap == null)
                bootstrap = manager.gameObject.AddComponent<WorldBootstrap>();

            return bootstrap;
        }

        /// <summary>
        /// Adds a <see cref="PlayerSpawnPoint"/> to every <c>PlayerSpawn_*</c> object under a
        /// <c>SpawnPoints</c> root, marking the first one as the zone default when none is marked.
        /// </summary>
        /// <returns>How many spawn-point components the scene now has.</returns>
        public static int EnsureSpawnPointComponents(Scene scene)
        {
            int count = 0;
            bool hasDefault = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform spawnRoot in FindChildrenNamed(root.transform, SpawnPointsName))
                {
                    foreach (Transform child in spawnRoot)
                    {
                        if (child == null)
                            continue;

                        PlayerSpawnPoint point = child.GetComponent<PlayerSpawnPoint>();
                        if (point == null)
                            point = child.gameObject.AddComponent<PlayerSpawnPoint>();

                        count++;
                        if (point.IsDefault)
                            hasDefault = true;
                    }
                }
            }

            if (count > 0 && !hasDefault)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform spawnRoot in FindChildrenNamed(root.transform, SpawnPointsName))
                    {
                        if (spawnRoot.childCount > 0)
                        {
                            spawnRoot.GetChild(0).GetComponent<PlayerSpawnPoint>()?.SetDefault(true);
                            break;
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Registers every prefab referenced by the scene's markers into the manager's spawnable
        /// collection, so the server and any client loading the same scene agree on its prefab id.
        /// </summary>
        /// <returns>How many prefabs were newly registered.</returns>
        public static int RegisterMarkerPrefabs(NetworkManager manager)
        {
            if (manager == null)
                return 0;

            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            int registered = 0;

            // Zones spawn resource nodes from markers, and a marker with no prefab uses the shared
            // node prefab. Bake it as an asset and register it in the scene's collection so the
            // server and the client that loads this same scene agree on its prefab id.
            NetworkObject nodePrefab = ResourceNodeBaker.EnsureResourceNodePrefab();
            if (nodePrefab != null && ResourceNodePrefabFactory.Register(manager.SpawnablePrefabs, nodePrefab))
                registered++;

            foreach (ResourceNodeMarker marker in Object.FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.NodePrefab != null && ResourceNodePrefabFactory.Register(manager.SpawnablePrefabs, marker.NodePrefab))
                    registered++;
            }

            foreach (CraftingStationMarker marker in Object.FindObjectsByType<CraftingStationMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.StationPrefab != null && CraftingStationPrefabFactory.Register(manager.SpawnablePrefabs, marker.StationPrefab))
                    registered++;
            }

            foreach (EnemyMarker marker in Object.FindObjectsByType<EnemyMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.EnemyPrefab != null && TargetDummyPrefabFactory.Register(manager.SpawnablePrefabs, marker.EnemyPrefab))
                    registered++;
            }

            // Bank tellers are spawned from markers, so the shared teller prefab must be in the
            // collection or the server has nothing to instantiate.
            NetworkObject tellerPrefab = BankTellerBaker.EnsureBankTellerPrefab();
            if (tellerPrefab != null && BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, tellerPrefab))
                registered++;

            foreach (BankTellerMarker marker in Object.FindObjectsByType<BankTellerMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (marker.TellerPrefab != null && BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, marker.TellerPrefab))
                    registered++;
            }

            return registered;
        }

        private static System.Collections.Generic.IEnumerable<Transform> FindChildrenNamed(Transform root, string name)
        {
            if (root == null)
                yield break;

            if (root.name == name)
                yield return root;

            for (int i = 0; i < root.childCount; i++)
            {
                foreach (Transform found in FindChildrenNamed(root.GetChild(i), name))
                    yield return found;
            }
        }
    }
}
