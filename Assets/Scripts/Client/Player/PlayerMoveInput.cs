using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The owner-side walking driver: reads W/S/Q/E, A/D and the mouse buttons, and streams them to
    /// the server as <em>intent</em>. It never moves or turns the transform — <see cref="PlayerMover"/>
    /// owns the position and the heading — so the client cannot walk further, faster, or rounder than
    /// the server integrates.
    ///
    /// <para>
    /// The keys are sent raw (local axes, a turn axis and a face-camera flag) rather than as a world
    /// direction: only the server knows the heading, so only the server can decide whether "forward"
    /// means along the heading or along the camera.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerMoveInput : NetworkBehaviour
    {
        /// <summary>How often a held direction is re-stated, in seconds.</summary>
        public const float SendIntervalSeconds = 0.05f;

        private PlayerMover _mover;
        private Vector2 _lastInput;
        private int _lastTurn;
        private bool _lastFaceCamera;
        private float _lastCameraYaw;
        private bool _wasMoving;
        private float _nextSendTime;

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();
        }

        private void Update()
        {
            // A dedicated server owns no local player, and a remote copy must not send its owner's
            // intents: this is the local player's keyboard, and only its owner may speak for it.
            if (!IsOwner || (IsServerStarted && !IsClientStarted))
                return;

            Vector2 input = new Vector2(
                Axis(Held(GameAction.StrafeRight), Held(GameAction.StrafeLeft)),
                Axis(Held(GameAction.MoveForward), Held(GameAction.MoveBack)));
            int turn = Axis(Held(GameAction.TurnRight), Held(GameAction.TurnLeft));
            bool faceCamera = Held(GameAction.FaceCamera);
            bool jump = Pressed(GameAction.Jump);

            // The classic shortcut: both buttons held runs forward.
            if (faceCamera && Held(GameAction.OrbitCamera))
                input.y = 1f;

            float cameraYaw = CameraYaw();
            bool moving = input.sqrMagnitude > 0f || turn != 0 || faceCamera;

            bool changed = (input - _lastInput).sqrMagnitude > 0.0001f
                           || turn != _lastTurn
                           || faceCamera != _lastFaceCamera
                           || jump
                           || (faceCamera && Mathf.Abs(cameraYaw - _lastCameraYaw) > 0.1f);

            // Idle stays quiet; a release or a jump still has something to say.
            if (!moving && !_wasMoving && !jump)
                return;

            if (!changed && Time.unscaledTime < _nextSendTime)
                return;

            _mover?.RequestMoveIntent(input, turn, faceCamera, cameraYaw, jump);
            _lastInput = input;
            _lastTurn = turn;
            _lastFaceCamera = faceCamera;
            _lastCameraYaw = cameraYaw;
            _wasMoving = moving;
            _nextSendTime = Time.unscaledTime + SendIntervalSeconds;
        }

        private static bool Held(GameAction action)
            => ClientInput.Current != null && ClientInput.Current.IsHeld(action);

        private static bool Pressed(GameAction action)
            => ClientInput.Current != null && ClientInput.Current.WasPressed(action);

        private static int Axis(bool positive, bool negative)
            => (positive ? 1 : 0) - (negative ? 1 : 0);

        /// <summary>
        /// The camera's horizontal facing. Until the orbit rig exists this is the scene camera's own
        /// yaw; without a camera it falls back to the character's heading so input still works.
        /// </summary>
        private float CameraYaw()
        {
            Camera camera = Camera.main;
            return camera != null ? camera.transform.eulerAngles.y : transform.eulerAngles.y;
        }
    }
}
