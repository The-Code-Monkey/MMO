using MMO.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Puts a <see cref="LocalCameraRig"/> in the active scene so it can be authored and tuned in
    /// the inspector, the same way <see cref="PlayerPrefabBaker"/> makes the player object a
    /// reusable asset.
    ///
    /// The rig is not required for the camera to work: <see cref="LocalCameraRigFactory.Ensure"/>
    /// builds one on demand when the owning client's player object claims it. Putting one in the
    /// scene is how a designer changes the lens, the offset or the blend without those choices being
    /// invisible inside a factory.
    /// </summary>
    public static class LocalCameraBaker
    {
        public const string MenuPath = "Tools/MMO/Create Local Camera";

        [MenuItem(MenuPath)]
        public static void CreateLocalCameraInScene()
        {
            LocalCameraRig rig = EnsureInActiveScene();
            if (rig == null)
                return;

            Selection.activeGameObject = rig.gameObject;
            EditorGUIUtility.PingObject(rig.gameObject);
        }

        /// <summary>
        /// The scene's camera rig, creating one if the scene has none.
        /// Idempotent: a second call reuses the existing rig rather than adding a second camera.
        /// </summary>
        public static LocalCameraRig EnsureInActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            if (TryFind(scene, out LocalCameraRig existing))
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return existing;
            }

            LocalCameraRig rig = LocalCameraRigFactory.Create();

            // A freshly constructed GameObject already lands in the active scene, so only move it
            // when it genuinely ended up somewhere else.
            if (rig.gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(rig.gameObject, scene);

            // Registered rather than left dangling so undo does not destroy a rig the user owns.
            Undo.RegisterCreatedObjectUndo(rig.gameObject, "Create Local Camera");
            EditorSceneManager.MarkSceneDirty(scene);

            return rig;
        }

        /// <summary>
        /// True when the scene has a root GameObject named "LocalCameraRig" carrying a
        /// <see cref="LocalCameraRig"/>.
        /// </summary>
        /// <remarks>
        /// Name as well as component, mirroring <see cref="NetworkManagerSceneBaker.TryFind"/>: the
        /// rig has to be recognisable in the hierarchy, not merely present.
        /// </remarks>
        public static bool TryFind(Scene scene, out LocalCameraRig rig)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != LocalCameraRigFactory.GameObjectName)
                    continue;

                LocalCameraRig found = root.GetComponent<LocalCameraRig>();
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
        /// Deliberately not a build gate. The rig is a client convenience, a fresh process builds one
        /// for itself, and a dedicated server scene has nothing to look through — so a missing rig
        /// must not stop a build the way a missing NetworkManager does.
        /// </summary>
        /// <returns>False with a human readable <paramref name="error"/> when it is missing.</returns>
        public static bool Validate(Scene scene, out string error)
        {
            if (TryFind(scene, out LocalCameraRig rig))
            {
                // A rig whose Cinemachine parts were deleted looks fine in the hierarchy and then
                // silently never drives anything, which is worse than not having one at all.
                if (rig.OutputCamera == null || rig.Brain == null || rig.VirtualCamera == null)
                {
                    error = $"'{LocalCameraRigFactory.GameObjectName}' in scene '{scene.name}' is " +
                            "missing its Camera, CinemachineBrain or CinemachineCamera. " +
                            $"Delete it and run '{MenuPath}' again.";
                    return false;
                }

                error = null;
                return true;
            }

            error = $"Scene '{scene.name}' has no GameObject named " +
                    $"\"{LocalCameraRigFactory.GameObjectName}\" with a LocalCameraRig. " +
                    $"Run '{MenuPath}' to author one; a fresh process builds its own if none is here.";
            return false;
        }
    }
}
