using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    /// <summary>
    /// The one camera rig a client looks through: a perspective eye on an orbit arm pivoting at the
    /// character's neck. Dragging either mouse button orbits without turning the character; the
    /// heading is the mover's business. The pointer is captured only while the player is steering
    /// and released the moment they are not.
    ///
    /// <para>
    /// The rig is bound to the player this client <em>owns</em> through
    /// <see cref="LocalPlayerCameraTarget"/>; a tag search is only the fallback for a scene with no
    /// local player. Every client holds every avatar, so a tag search can find a stranger.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonCameraRig : MonoBehaviour
    {
        public static ThirdPersonCameraRig Instance { get; private set; }

        [Header("Targeting")]
        [SerializeField] private Transform target;

        /// <summary>Fallback for a scene with no local player; never the primary binding.</summary>
        [SerializeField] private string playerTag = "Player";

        [Header("Orbit")]
        [SerializeField] private float sensitivity = CameraOrbitMath.Sensitivity;
        [SerializeField] private float minPitch = CameraOrbitMath.MinPitch;
        [SerializeField] private float maxPitch = CameraOrbitMath.MaxPitch;
        [SerializeField] private float distance = CameraOrbitMath.DefaultDistance;

        /// <summary>Height of the body the pivot is measured against, in metres.</summary>
        [SerializeField] private float capsuleHeight = 2f;

        private Transform _localTarget;
        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private bool _driving;
        private bool _captured;

        /// <summary>The player this client owns, or null until one registers.</summary>
        public Transform Target => target;

        private void Awake()
        {
            // One rig survives zone loads and prevents duplicates, the same contract the old
            // orthographic rig had, so a scene's second copy cannot fight this one.
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            _camera = GetComponentInChildren<Camera>();
            if (_camera != null)
            {
                _camera.orthographic = false;
                _camera.transform.localPosition = new Vector3(0f, 0f, -distance);
                _camera.transform.localRotation = Quaternion.identity;
            }

            _yaw = CameraOrbitMath.Wrap(transform.eulerAngles.y);
            _pitch = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                ReleaseCursor();
                Instance = null;
            }
        }

        private void Update()
        {
            FindTargetIfNeeded();
            HandleCursor();
            HandleOrbit();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            transform.position = CameraOrbitMath.Pivot(target.position, capsuleHeight);
            transform.rotation = CameraOrbitMath.Rotation(_yaw, _pitch);
        }

        /// <summary>
        /// Binds the rig to <paramref name="player"/> — the local owner's transform — overriding the
        /// tag fallback. Called by the player prefab's <c>LocalPlayerCameraTarget</c>.
        /// </summary>
        public void SetLocalTarget(Transform player)
        {
            _localTarget = player;
            target = player;
        }

        /// <summary>Clears a local binding, e.g. when this client stops owning a player.</summary>
        public void ClearLocalTarget()
        {
            if (target == _localTarget)
                target = null;

            _localTarget = null;
        }

        private void HandleOrbit()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !_driving)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            _yaw = CameraOrbitMath.NextYaw(_yaw, delta.x, sensitivity);
            _pitch = CameraOrbitMath.NextPitch(_pitch, delta.y, sensitivity, minPitch, maxPitch);

            if (_camera != null)
                _camera.transform.localPosition = new Vector3(0f, 0f, -Mathf.Max(0f, distance));
        }

        private void HandleCursor()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                _driving = false;
                ReleaseCursor();
                return;
            }

            if (ClientInput.Current != null
                && (ClientInput.Current.GameplayBlocked || ClientInput.Current.IsRebinding))
            {
                _driving = false;
                ReleaseCursor();
                return;
            }

            bool pressed = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            if (pressed && !_driving && !IsPointerOverUi())
            {
                _driving = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _captured = true;
            }

            bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (_driving && (!held || escape))
            {
                _driving = false;
                ReleaseCursor();
            }
        }

        private void ReleaseCursor()
        {
            if (!_captured)
                return;

            _captured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool IsPointerOverUi()
        {
            EventSystem system = EventSystem.current;
            return system != null && system.IsPointerOverGameObject();
        }

        private void FindTargetIfNeeded()
        {
            if (_localTarget != null)
            {
                target = _localTarget;
                return;
            }

            if (target == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag(playerTag);
                if (player != null)
                    target = player.transform;
            }
        }
    }
}
