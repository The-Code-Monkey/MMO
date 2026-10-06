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
        /// How high above the horizon the camera sits, measured from directly behind the player
        /// (0° — a chase camera) to directly over their head (90° — straight down). 70° is a
        /// mainly top-down view that still shows the ground ahead of the player, and it is a
        /// constant: the angle is baked into a fixed world-space offset rather than tracked
        /// from anything the player does.
        /// </summary>
        public const float DefaultFollowPitchDegrees = 70f;

        /// <summary>
        /// Straight-line distance from the player to the camera. At 70° this lands the camera
        /// about 5.6 metres above and 2 metres behind the player's origin — far enough back
        /// that the "behind" half of the angle is meaningful rather than a camera hovering
        /// almost on top of the capsule.
        /// </summary>
        public const float DefaultFollowDistance = 6f;

        /// <summary>
        /// How far above the player's origin the camera sits: the pitch turned into a height.
        /// </summary>
        public static float DefaultFollowHeight =>
            DefaultFollowDistance * Mathf.Sin(DefaultFollowPitchDegrees * Mathf.Deg2Rad);

        /// <summary>
        /// How far behind the player the camera trails, measured along the ground plane.
        /// </summary>
        public static float DefaultFollowBackDistance =>
            DefaultFollowDistance * Mathf.Cos(DefaultFollowPitchDegrees * Mathf.Deg2Rad);

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
        /// Where the camera sits relative to its target: the pitch and distance above, as a
        /// single fixed vector. World space, not target space — a player that turns in place,
        /// runs at the camera or runs away from it never changes this vector, so the camera's
        /// rotation never whips around. The follow stage is explicitly bound to
        /// <see cref="BindingMode.WorldSpace"/> to keep it that way.
        /// </summary>
        public static Vector3 DefaultFollowOffset => new Vector3(
            0f,
            DefaultFollowHeight,
            -DefaultFollowBackDistance);

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
        /// Cinemachine's defaults, with the settings this project wants. Starting from
        /// <see cref="TrackerSettings.Default"/> rather than a fresh struct so a Cinemachine
        /// upgrade that adds fields to the struct keeps its own values.
        /// </summary>
        private static TrackerSettings FollowTrackingSettings()
        {
            TrackerSettings tracking = TrackerSettings.Default;

            // Stated rather than inherited: WorldSpace is what makes the rotation fixed. Any
            // LockToTarget* mode would rotate the offset with the player, so running towards or
            // away from the camera would swing it round — the quick camera movement this rig
            // exists to avoid. (WorldSpace is also Cinemachine's own default today; pinning it
            // means a package upgrade cannot silently change the camera's behaviour.)
            tracking.BindingMode = BindingMode.WorldSpace;

            tracking.PositionDamping = Vector3.one * FollowDampingSeconds;
            return tracking;
        }
    }
}
