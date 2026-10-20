using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Puts a copy of <c>Assets/Prefabs/Camera/CameraRig.prefab</c> — the scene's single
    /// <see cref="PersistentCameraController"/> and its child camera — into the active scene so it
    /// can be authored and tuned in the inspector, the same way <see cref="PlayerPrefabBaker"/>
    /// makes the player object a reusable asset.
    ///
    /// <para>
    /// The rig is not optional in the sense that matters: it is the one thing a client looks
    /// through, and it is deliberately *not* on the player prefab, so a zone scene without one has
    /// no camera at all. Zone scenes get it by construction from
    /// <see cref="ZoneSceneBaker"/>, which calls <see cref="EnsureInActiveScene"/> while it
    /// assembles the scene; this menu is for hand-authored scenes.
    /// </para>
    ///
    /// <para>
    /// The main menu is the deliberate exception: it never carries a rig. The controller survives
    /// scene loads (it is <c>DontDestroyOnLoad</c>) and re-finds the player by tag, so a menu that
    /// shipped its own would be a second instance racing the zone's for the same singleton slot.
    /// </para>
    /// </summary>
    public static class LocalCameraBaker
    {
        public const string MenuPath = "Tools/MMO/Create Camera Rig";

        public const string GameObjectName = "CameraRig";

        public const string PrefabAssetPath = "Assets/Prefabs/Camera/CameraRig.prefab";

        [MenuItem(MenuPath)]
        public static void CreateCameraRigInScene()
        {
            PersistentCameraController rig = EnsureInActiveScene();
            if (rig == null)
                return;

            Selection.activeGameObject = rig.gameObject;
            EditorGUIUtility.PingObject(rig.gameObject);
        }

        /// <summary>
        /// The scene's camera rig, creating one if the scene has none.
        /// Idempotent: a second call reuses the existing rig rather than adding a second camera.
        /// </summary>
        public static PersistentCameraController EnsureInActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (TryFind(scene, out PersistentCameraController existing))
                return existing;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
            if (prefab == null)
            {
                Debug.LogError(
                    $"Camera rig prefab is missing from '{PrefabAssetPath}'. Zone scenes would " +
                    "bake with no camera at all; restore the prefab or re-create it.");
                return null;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            // Click-to-move raycasts through Camera.main, and a rig whose cameras were left
            // untagged looks correct and then never turns a click into a move.
            TagCamerasAsMainCamera(rig);

            // Registered rather than left dangling so undo does not destroy a rig the user owns.
            Undo.RegisterCreatedObjectUndo(rig, "Create Camera Rig");
            EditorSceneManager.MarkSceneDirty(scene);

            return rig.GetComponent<PersistentCameraController>();
        }

        /// <summary>
        /// True when the scene has a root GameObject named "CameraRig" carrying a
        /// <see cref="PersistentCameraController"/>.
        /// </summary>
        /// <remarks>
        /// Name as well as component, mirroring <see cref="NetworkManagerSceneBaker.TryFind"/>: the
        /// rig has to be recognisable in the hierarchy, not merely present.
        /// </remarks>
        public static bool TryFind(Scene scene, out PersistentCameraController rig)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != GameObjectName)
                    continue;

                PersistentCameraController found = root.GetComponent<PersistentCameraController>();
                if (found != null)
                {
                    rig = found;
                    return true;
                }
            }

            rig = null;
            return false;
        }

        /// <summary>
        /// Checks a scene for an authored camera rig.
        /// </summary>
        /// <remarks>
        /// Deliberately not a build gate. The rig is a client convenience, a fresh process can
        /// build one for itself, and a dedicated server scene has nothing to look through — so a
        /// missing rig must not stop a build the way a missing NetworkManager does.
        /// </remarks>
        /// <returns>False with a human readable <paramref name="error"/> when it is missing.</returns>
        public static bool Validate(Scene scene, out string error)
        {
            if (!TryFind(scene, out PersistentCameraController rig))
            {
                error = $"Scene '{scene.name}' has no GameObject named " +
                        $"\"{GameObjectName}\" with a PersistentCameraController. " +
                        $"Run '{MenuPath}' to author one.";
                return false;
            }

            // A rig whose child camera was deleted looks fine in the hierarchy and then silently
            // renders nothing, which is worse than not having one at all.
            if (rig.GetComponentInChildren<Camera>() == null)
            {
                error = $"'{GameObjectName}' in scene '{scene.name}' is missing its Camera. " +
                        $"Delete it and run '{MenuPath}' again.";
                return false;
            }

            // Camera.main is how click-to-move finds the ray origin.
            if (Camera.main == null)
            {
                error = $"'{GameObjectName}' in scene '{scene.name}' has no camera tagged " +
                        "MainCamera, so click-to-move has nothing to raycast through.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Tags every camera under the rig as MainCamera so <c>Camera.main</c> resolves.
        /// </summary>
        public static void TagCamerasAsMainCamera(GameObject rig)
        {
            foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
                camera.gameObject.tag = "MainCamera";
        }
    }
}
