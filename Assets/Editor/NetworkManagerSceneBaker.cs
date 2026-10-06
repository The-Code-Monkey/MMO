using FishNet.Managing;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Materialises the object <see cref="NetworkManagerFactory"/> builds into the active
    /// scene, so the scene is saved with a NetworkManager already in it — including the player
    /// prefab registration and PlayerSpawner that make a connecting client receive a player
    /// object, and the target dummy registration and spawner that put a server-spawned enemy
    /// in the world when the server starts.
    /// </summary>
    public static class NetworkManagerSceneBaker
    {
        public const string MenuPath = "Tools/MMO/Create NetworkManager In Scene";

        [MenuItem(MenuPath)]
        public static void CreateNetworkManagerInScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool alreadyExisted = TryFind(scene, out _);

            NetworkManager manager = CreateInActiveScene(new MMOConnectionSettings());

            // Registering undo against an object that was already there would destroy something
            // the user owns the moment they hit undo.
            if (!alreadyExisted)
                Undo.RegisterCreatedObjectUndo(manager.gameObject, "Create NetworkManager");

            EditorSceneManager.MarkSceneDirty(scene);

            Selection.activeGameObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);
        }

        /// <summary>
        /// Returns the scene's NetworkManager, creating one if the scene has none, and wires the
        /// player object into it so every admitted client is spawned one, plus the target dummy
        /// that the server instantiates and spawns when it starts listening.
        /// Idempotent: a second call reuses the existing object and does not duplicate the wiring.
        /// </summary>
        public static NetworkManager CreateInActiveScene(MMOConnectionSettings settings)
        {
            Scene scene = SceneManager.GetActiveScene();

            if (TryFind(scene, out NetworkManager existing))
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);

                // Already in the scene, so it has no prefab wiring of ours to inherit; give it one.
                PlayerPrefabBaker.WirePlayerPrefab(existing);
                TargetDummyBaker.WireTargetDummy(existing);
                return existing;
            }

            // Bake first so the factory registers the saved assets rather than building second,
            // scene-local objects that would then be serialized into the scene.
            NetworkObject playerPrefab = PlayerPrefabBaker.EnsurePlayerPrefab();
            NetworkObject targetDummyPrefab = TargetDummyBaker.EnsureTargetDummyPrefab();

            NetworkManager manager = NetworkManagerFactory.Create(
                settings, out _, playerPrefab, targetDummyPrefab);

            // A freshly constructed GameObject already lands in the active scene, so only
            // move it when it genuinely ended up somewhere else.
            if (manager.gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(manager.gameObject, scene);

            return manager;
        }

        /// <summary>
        /// True when the scene has a root GameObject named "NetworkManager" carrying a
        /// FishNet NetworkManager. Shared with the build guard so the menu, the validator
        /// and the build cannot disagree.
        /// </summary>
        public static bool TryFind(Scene scene, out NetworkManager manager)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != NetworkManagerFactory.GameObjectName)
                    continue;

                manager = root.GetComponent<NetworkManager>();
                if (manager != null)
                    return true;
            }

            manager = null;
            return false;
        }

        /// <summary>
        /// Checks a scene for the object the build guard insists on.
        /// </summary>
        /// <returns>False with a human readable <paramref name="error"/> when it is missing.</returns>
        public static bool Validate(Scene scene, out string error)
        {
            if (TryFind(scene, out _))
            {
                error = null;
                return true;
            }

            error = $"Scene '{scene.name}' has no GameObject named " +
                    $"\"{NetworkManagerFactory.GameObjectName}\" with a FishNet NetworkManager. " +
                    $"Run '{MenuPath}'.";
            return false;
        }
    }
}