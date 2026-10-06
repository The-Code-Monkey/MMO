using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Builds the camera rig a client looks through. The same code path produces the object in a
    /// baked scene, in a fresh process and in a test, so there is no "the scene has one and the
    /// runtime has another" divergence to chase.
    ///
    /// The rig is deliberately built switched off. A virtual camera that is enabled is a live
    /// camera as far as Cinemachine's brain is concerned, and a client that has joined but not yet
    /// received a player object must not be looking at anybody. Nothing turns it on except
    /// <see cref="LocalPlayerCamera"/> on the player this client owns.
    /// </summary>
    public static class LocalCameraRigFactory
    {
        public const string GameObjectName = "LocalCameraRig";

        /// <summary>
        /// How far above the player's origin the camera sits. The player object is a two metre
        /// capsule, so this is roughly shoulder height rather than floor level.
        /// </summary>
        public const float DefaultFollowHeight = 1.5f;

        /// <summary>How far behind the player the camera trails.</summary>
        public const float DefaultFollowDistance = 5f;

        /// <summary>Where the camera aims, in the player's local space — the capsule's middle.</summary>
        public const float DefaultAimHeight = 1f;

        /// <summary>
        /// Follow damping in seconds. Non-zero so a jittery replicated position reads as a camera
        /// easing along rather than a camera glued to network noise.
        /// </summary>
        public const float FollowDampingSeconds = 0.15f;

        /// <summary>Aim damping in seconds, horizontal and vertical alike.</summary>
        public const float AimDampingSeconds = 0.1f;

        /// <summary>
        /// How long the brain takes to ease onto the virtual camera when a player claims the rig.
        /// </summary>
        public const float BlendSeconds = 0.25f;

        /// <summary>
        /// Where the camera sits relative to its target. World space, not target space: a player
        /// that turns in place should not whip the camera round with it until the game gives the
        /// camera something to orbit.
        /// </summary>
        public static Vector3 DefaultFollowOffset => new Vector3(
            0f,
            DefaultFollowHeight,
            -DefaultFollowDistance);

        /// <summary>
        /// Builds the rig GameObject and returns its <see cref="LocalCameraRig"/>, switched off and
        /// following nothing.
        /// </summary>
        public static LocalCameraRig Create()
        {
            var go = new GameObject(GameObjectName);

            // Awake() runs the instant a component lands on an *active* object, and Cinemachine
            // registers a virtual camera with its brain from its own OnEnable. Build inactive,
            // wire everything, then activate last, so the rig is never briefly live on the way in.
            go.SetActive(false);

            try
            {
                Camera outputCamera = go.AddComponent<Camera>();

                // Nothing renders from a rig nobody has claimed: no static scene camera, and no
                // chance of a brief frame from the origin on the way to the player.
                outputCamera.enabled = false;

                CinemachineBrain brain = go.AddComponent<CinemachineBrain>();
                brain.DefaultBlend = new CinemachineBlendDefinition(
                    CinemachineBlendDefinition.Styles.EaseInOut, BlendSeconds);

                CinemachineCamera virtualCamera = go.AddComponent<CinemachineCamera>();

                // Cinemachine 3 composes a virtual camera out of one component per stage and adds
                // none of them for you: without a Body stage the rig would hold the last position
                // and without an Aim stage it would never turn to look at anybody.
                CinemachineFollow follow = go.AddComponent<CinemachineFollow>();
                follow.FollowOffset = DefaultFollowOffset;
                follow.TrackerSettings = FollowTrackingSettings();

                CinemachineRotationComposer aim = go.AddComponent<CinemachineRotationComposer>();
                aim.TargetOffset = new Vector3(0f, DefaultAimHeight, 0f);
                aim.Damping = new Vector2(AimDampingSeconds, AimDampingSeconds);

                // Last of all: an enabled virtual camera is live to the brain, and an unclaimed
                // rig has no business being live.
                virtualCamera.enabled = false;

                LocalCameraRig rig = go.AddComponent<LocalCameraRig>();

                go.SetActive(true);

                // Explicit even though Awake would do it: a rig assembled in code has to be usable
                // straight away, including from edit mode where Awake never runs.
                rig.Bind();
                return rig;
            }
            catch
            {
                Object.DestroyImmediate(go);
                throw;
            }
        }

        /// <summary>
        /// The rig already in the scene, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Only looks at active objects. The rig's GameObject stays active even while unclaimed —
        /// it is the components that are switched off — so an unclaimed rig is still findable and a
        /// scene-authored one is reused rather than duplicated.
        /// </remarks>
        public static LocalCameraRig Find() => Object.FindFirstObjectByType<LocalCameraRig>();

        /// <summary>
        /// The rig in the scene, building one if the scene has none. Idempotent: a second call
        /// returns the same rig and leaves whatever it is following alone.
        /// </summary>
        public static LocalCameraRig Ensure() => Find() ?? Create();

        /// <summary>
        /// Cinemachine's defaults, with the damping this project wants. Starting from
        /// <see cref="TrackerSettings.Default"/> rather than a fresh struct so a Cinemachine
        /// upgrade that adds fields to the struct keeps its own values.
        /// </summary>
        private static TrackerSettings FollowTrackingSettings()
        {
            TrackerSettings tracking = TrackerSettings.Default;
            tracking.PositionDamping = Vector3.one * FollowDampingSeconds;
            return tracking;
        }
    }
}
