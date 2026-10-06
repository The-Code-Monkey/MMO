using Unity.Cinemachine;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The one camera rig a client looks through: a Unity <see cref="UnityEngine.Camera"/> driven
    /// by a Cinemachine <see cref="CinemachineBrain"/>, plus a <see cref="CinemachineCamera"/> —
    /// the "virtual camera" — that decides where the output camera goes and what it points at.
    ///
    /// There is exactly one rig per process and it starts <em>unclaimed</em>: both the output camera
    /// and the virtual camera are disabled, so the scene has no live camera at all until somebody
    /// points it somewhere. Only a player this client owns ever claims it, via
    /// <see cref="LocalPlayerCamera"/>. That restriction is the whole reason the rig lives outside
    /// the player prefab — a vcam parented to the prefab would be spawned once per player on every
    /// client, and the last one to arrive would decide what everyone is looking at.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera _outputCamera;
        [SerializeField] private CinemachineBrain _brain;
        [SerializeField] private CinemachineCamera _virtualCamera;

        /// <summary>The camera this rig drives. Disabled while the rig is unclaimed.</summary>
        public Camera OutputCamera => _outputCamera;

        /// <summary>
        /// The component that picks a live virtual camera and blends to it. Kept so a scene
        /// inspector can tune the blend without digging through the package.
        /// </summary>
        public CinemachineBrain Brain => _brain;

        /// <summary>The virtual camera. Disabled while the rig is unclaimed.</summary>
        public CinemachineCamera VirtualCamera => _virtualCamera;

        /// <summary>What the virtual camera is following, or null when it is unclaimed.</summary>
        public Transform FollowTarget => _virtualCamera != null ? _virtualCamera.Follow : null;

        /// <summary>What the virtual camera is aiming at, or null when it is unclaimed.</summary>
        public Transform LookAtTarget => _virtualCamera != null ? _virtualCamera.LookAt : null;

        /// <summary>
        /// True while a player is driving this rig. Cinemachine only offers a brain the virtual
        /// cameras that are enabled, so this being false is what makes the rig invisible to
        /// Cinemachine rather than merely pointing somewhere unhelpful.
        /// </summary>
        public bool IsClaimed => _virtualCamera != null && _virtualCamera.isActiveAndEnabled;

        /// <summary>
        /// Points the rig at <paramref name="target"/> and switches the camera on, so this client
        /// looks through <paramref name="target"/> from here on.
        ///
        /// Idempotent, and safe to call from several players: the last caller wins, which is the
        /// behaviour a respawn needs (the new player object takes over from the old one) and never
        /// a race, because <see cref="LocalPlayerCamera"/> only ever calls this for the object this
        /// client owns.
        /// </summary>
        public void Claim(Transform target)
        {
            if (target == null || _virtualCamera == null || _outputCamera == null)
                return;

            // Targets first, then the switches. Enabling the virtual camera runs its OnEnable,
            // which hands it to Cinemachine's brain, and that must not happen before it has a
            // target or the first frame is composed around nothing.
            _virtualCamera.Follow = target;
            _virtualCamera.LookAt = target;

            _outputCamera.enabled = true;
            _virtualCamera.enabled = true;
        }

        /// <summary>
        /// Gives the camera back: the rig stops following anyone and switches the output camera
        /// off. Called when the owning player stops being ours — a despawn, a disconnect, or
        /// ownership moving elsewhere — so the view does not stay glued to a stale capsule.
        /// </summary>
        public void Release()
        {
            if (_virtualCamera == null || _outputCamera == null)
                return;

            // Off before the targets are dropped: a disabled virtual camera is out of the brain's
            // running entirely, whereas a live one with a null target keeps composing frames.
            _virtualCamera.enabled = false;
            _outputCamera.enabled = false;

            _virtualCamera.Follow = null;
            _virtualCamera.LookAt = null;
        }

        /// <summary>
        /// Resolves the three Cinemachine parts from this GameObject.
        ///
        /// The brain drives the camera it sits on and can only activate a virtual camera that is
        /// its own, so all three always live together and one rule — "the components on this
        /// object" — is the only thing that can wire them. Called by
        /// <see cref="LocalCameraRigFactory"/> in code and by Awake for a rig assembled in the
        /// editor, which means edit mode and play mode cannot end up with different wiring.
        /// </summary>
        public void Bind()
        {
            _outputCamera = GetComponent<Camera>();
            _brain = GetComponent<CinemachineBrain>();
            _virtualCamera = GetComponent<CinemachineCamera>();
        }

        private void Awake() => Bind();
    }
}
