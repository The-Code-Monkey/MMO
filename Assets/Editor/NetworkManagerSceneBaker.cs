using FishNet.Managing;
using MMO.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Materialises the object <see cref="NetworkManagerFactory"/> builds into the active
    /// scene, so the scene is saved with a NetworkManager already in it.
    /// </summary>
    public static class NetworkManagerSceneBaker
    {
        public const string MenuPath = "Tools/MMO/Create NetworkManager In Scene";

        [MenuItem(MenuPath)]
        public static void CreateNetworkManagerInScene()
        {
            NetworkManager manager = CreateInActiveScene(new MMOConnectionSettings());
            Undo.RegisterCreatedObjectUndo(manager.gameObject, "Create NetworkManager");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Selection.activeGameObject = manager.gameObject;
            EditorGUIUtility.PingObject(manager.gameObject);
        }

        /// <summary>
        /// Returns the scene's NetworkManager, creating one if the scene has none.
        /// Idempotent: a second call reuses the existing object.
        /// </summary>
        public static NetworkManager CreateInActiveScene(MMOConnectionSettings settings)
        {
            Scene scene = SceneManager.GetActiveScene();

            if (TryFind(scene, out NetworkManager existing))
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return existing;
            }

            NetworkManager created = NetworkManagerFactory.Create(settings, out _);

            // A freshly constructed GameObject already lands in the active scene, so only
            // move it when it genuinely ended up somewhere else.
            if (created.gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(created.gameObject, scene);

            return created;
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