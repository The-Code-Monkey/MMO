using UnityEngine;
using UnityEngine.InputSystem;
using MMO.Gameplay;

namespace MMO.Input
{
    /// <summary>
    /// Owner-side input driver: reads the PlayerMovement action map and forwards the move
    /// vector to the local player's <see cref="PlayerMover"/>. This component must be on the
    /// same GameObject as the PlayerMover (or a child) and only acts for the local owner.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerMoverInput : MonoBehaviour
    {
        private PlayerMover _mover;
        private InputAction _moveAction;

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();

            var asset = Resources.Load<InputActionAsset>("Input/PlayerMovement");
            if (asset == null)
            {
                Debug.LogError("[PlayerMoverInput] PlayerMovement.inputactions not found in Resources/Input/");
                return;
            }

            _moveAction = asset.FindActionMap("Player").FindAction("Move");
            _moveAction.Enable();
        }

        private void OnDestroy()
        {
            if (_moveAction != null)
            {
                _moveAction.Disable();
                _moveAction.Dispose();
            }
        }

        private void Update()
        {
            if (_mover == null || _moveAction == null)
                return;

            var netObj = _mover.NetworkObject;
            if (netObj == null || !netObj.IsOwner)
                return;

            Vector2 input = _moveAction.ReadValue<Vector2>();
            if (input.sqrMagnitude > 0f)
                _mover.RequestMove(input);
        }
    }
}