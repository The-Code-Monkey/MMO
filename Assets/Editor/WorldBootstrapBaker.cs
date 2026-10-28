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
            int spawnPoints = PlayerSpawnPointAuthor.EnsureComponents(scene, SpawnPointsName);
            int registered = MarkerPrefabRegistrar.Register(manager);

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
            => PlayerSpawnPointAuthor.EnsureComponents(scene, SpawnPointsName);

        /// <summary>Registers every prefab referenced by the scene's markers.</summary>
        public static int RegisterMarkerPrefabs(NetworkManager manager)
            => MarkerPrefabRegistrar.Register(manager);

    }
}
