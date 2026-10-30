using MMO.Gameplay;
using FishNet.Object;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Drives the cosmetic character model from the replicated player transform. The server owns
    /// position; this component only reads how far the root moved and feeds the Animator, so a
    /// client can never animate itself into a different gameplay state.
    ///
    /// It lives on a child of the networked player, never on the NetworkObject itself: animation
    /// is not authoritative state and must not shift the prefab's NetworkBehaviour indices.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterVisual : MonoBehaviour
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int DeadParameter = Animator.StringToHash("Dead");

        [SerializeField]
        private float _speedDamping = 12f;

        [SerializeField]
        private float _turnDegreesPerSecond = 720f;

        // The source FBX faces -Z. Locomotion aims the visual's +Z down the travel direction, so
        // the model needs half a turn to keep its face, not its back, leading.
        [SerializeField]
        private float _modelYawOffset = 180f;

        private Animator _animator;
        private Transform _root;
        private PlayerMover _mover;
        private PlayerStats _stats;
        private NetworkObject _networkObject;
        private Vector3 _lastRootPosition;
        private bool _dead;
        private float _speed;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _root = transform.parent;
            _mover = GetComponentInParent<PlayerMover>();
            _stats = GetComponentInParent<PlayerStats>();
            _networkObject = GetComponentInParent<NetworkObject>();
        }

        private void OnEnable()
        {
            if (_stats != null)
                _stats.VitalsChanged += OnVitalsChanged;

            if (_root != null)
                _lastRootPosition = _root.position;
        }

        private void Start()
        {
            // A dedicated server has no screen: stop evaluating the rig entirely.
            if (_networkObject != null && _networkObject.IsServerStarted && !_networkObject.IsClientStarted)
            {
                if (_animator != null)
                    _animator.enabled = false;

                enabled = false;
            }
        }

        private void OnDisable()
        {
            if (_stats != null)
                _stats.VitalsChanged -= OnVitalsChanged;
        }

        private void Update()
        {
            if (_animator == null || _root == null)
                return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            Vector3 displacement = _root.position - _lastRootPosition;
            _lastRootPosition = _root.position;

            var horizontal = new Vector3(displacement.x, 0f, displacement.z);
            float speed = horizontal.magnitude / deltaTime;
            float maxSpeed = _mover != null ? Mathf.Max(0.01f, _mover.MaxSpeed) : 6f;
            float targetSpeed = PlayerLocomotionMath.NormalizeSpeed(speed, maxSpeed);
            _speed = Mathf.Lerp(_speed, targetSpeed, 1f - Mathf.Exp(-_speedDamping * deltaTime));
            _animator.SetFloat(SpeedParameter, _speed);

            if (!_dead && horizontal.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(horizontal, Vector3.up) * Quaternion.Euler(0f, _modelYawOffset, 0f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    _turnDegreesPerSecond * deltaTime);
            }
        }

        private void OnVitalsChanged()
        {
            if (_stats == null || _animator == null)
                return;

            bool dead = _stats.Health <= 0;
            if (dead == _dead)
                return;

            _dead = dead;
            _animator.SetBool(DeadParameter, dead);
        }
    }
}
