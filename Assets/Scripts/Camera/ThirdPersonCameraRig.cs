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

        [Header("Zoom and clip")]
        [SerializeField] private float zoomStep = CameraOrbitMath.ZoomStep;
        [SerializeField] private float zoomSmoothTime = CameraOrbitMath.ZoomSmoothTime;
        [SerializeField] private float clipRadius = CameraOrbitMath.ClipRadius;
        [SerializeField] private LayerMask clipMask;
        [SerializeField] private float avatarHideDistance = CameraOrbitMath.AvatarHideDistance;

        /// <summary>Shared buffer for the clip spherecast, so the frame does not allocate a hit array.</summary>
        private static readonly RaycastHit[] ClipHits = new RaycastHit[8];

        private Transform _localTarget;
        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private float _targetDistance;
        private float _currentDistance;
        private float _distanceVelocity;
        private Renderer[] _avatarRenderers;
        private bool _avatarHidden;
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

            // The arm may come closer than a 0.3 m near plane allows, so the eye renders from 5 cm.
            if (clipMask.value == 0)
                clipMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Water", "UI");

            _targetDistance = _currentDistance = Mathf.Clamp(
                distance, CameraOrbitMath.MinDistance, CameraOrbitMath.MaxDistance);

            _camera = GetComponentInChildren<Camera>();
            if (_camera != null)
            {
                _camera.orthographic = false;
                _camera.nearClipPlane = 0.05f;
                _camera.transform.localPosition = new Vector3(0f, 0f, -_currentDistance);
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
            HandleZoom();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 pivot = CameraOrbitMath.Pivot(target.position, capsuleHeight);
            Quaternion rotation = CameraOrbitMath.Rotation(_yaw, _pitch);

            transform.position = pivot;
            transform.rotation = rotation;

            _currentDistance = CameraOrbitMath.DampDistance(
                _currentDistance,
                _targetDistance,
                ref _distanceVelocity,
                Time.deltaTime,
                zoomSmoothTime);

            float arm = ClipArm(pivot, rotation, _currentDistance);

            if (_camera != null)
                _camera.transform.localPosition = new Vector3(0f, 0f, -arm);

            UpdateAvatar(arm);
        }

        /// <summary>
        /// Shortens the arm when geometry sits between the pivot and the eye, so the camera never
        /// ends up on the far side of a wall. The local avatar is skipped: it is not scenery.
        /// </summary>
        private float ClipArm(Vector3 pivot, Quaternion rotation, float desired)
        {
            Vector3 back = rotation * Vector3.back;
            int count = Physics.SphereCastNonAlloc(
                pivot, clipRadius, back, ClipHits, desired, clipMask, QueryTriggerInteraction.Ignore);

            float nearest = desired;
            for (int i = 0; i < count; i++)
            {
                Collider hit = ClipHits[i].collider;
                if (hit == null)
                    continue;

                if (target != null && hit.transform.root == target.root)
                    continue;

                if (ClipHits[i].distance < nearest)
                    nearest = ClipHits[i].distance;
            }

            return nearest < desired
                ? CameraOrbitMath.ClipDistance(desired, nearest, clipRadius)
                : desired;
        }

        /// <summary>
        /// Hides the local avatar once the eye is inside it. Remote avatars are untouched: this rig
        /// only ever looks at the player this client owns.
        /// </summary>
        private void UpdateAvatar(float arm)
        {
            if (target == null)
                return;

            if (_avatarRenderers == null)
                _avatarRenderers = target.GetComponentsInChildren<Renderer>(true);

            bool hide = arm < avatarHideDistance;
            if (hide == _avatarHidden)
                return;

            _avatarHidden = hide;
            for (int i = 0; i < _avatarRenderers.Length; i++)
            {
                if (_avatarRenderers[i] != null)
                    _avatarRenderers[i].enabled = !hide;
            }
        }

        /// <summary>
        /// Binds the rig to <paramref name="player"/> — the local owner's transform — overriding the
        /// tag fallback. Called by the player prefab's <c>LocalPlayerCameraTarget</c>.
        /// </summary>
        public void SetLocalTarget(Transform player)
        {
            _localTarget = player;
            target = player;
            _avatarRenderers = null;
            _avatarHidden = false;
        }

        /// <summary>Clears a local binding, e.g. when this client stops owning a player.</summary>
        public void ClearLocalTarget()
        {
            if (target == _localTarget)
                target = null;

            _localTarget = null;
            _avatarRenderers = null;
            _avatarHidden = false;
        }

        private void HandleOrbit()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !_driving)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            _yaw = CameraOrbitMath.NextYaw(_yaw, delta.x, sensitivity);
            _pitch = CameraOrbitMath.NextPitch(_pitch, delta.y, sensitivity, minPitch, maxPitch);
        }

        /// <summary>
        /// The wheel moves the arm's target; the damping happens in LateUpdate. A raw scroll of
        /// +/-120 is one notch on a wheel, while a trackpad reports small values that are already
        /// notches.
        /// </summary>
        private void HandleZoom()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;

            float notches = Mathf.Abs(scroll) >= 1f ? scroll / 120f : scroll;
            _targetDistance = CameraOrbitMath.ApplyZoom(_targetDistance, notches, zoomStep);
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
