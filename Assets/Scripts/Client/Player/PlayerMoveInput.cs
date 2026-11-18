using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The owner-side walking driver: reads W/S/Q/E, turns them into a camera-relative direction
    /// and streams that direction to the server as <em>intent</em>. It never moves the transform —
    /// <see cref="PlayerMover"/> owns the position — so the client cannot walk further or faster
    /// than the server integrates.
    ///
    /// <para>
    /// Sends are rate-limited and edge-triggered: a held key is re-stated a few times a second so
    /// the server keeps integrating after a packet, and a release goes out immediately as a zero so
    /// the character stops even if the release is the only thing that changed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerMoveInput : NetworkBehaviour
    {
        /// <summary>How often a held direction is re-stated, in seconds.</summary>
        public const float SendIntervalSeconds = 0.05f;

        private PlayerMover _mover;
        private Vector2 _lastSent;
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

            Vector2 local = MoveIntentMath.ApplyMultipliers(ReadInput());
            Vector2 world = MoveIntentMath.ToWorld(local, CameraYaw());

            bool changed = (world - _lastSent).sqrMagnitude > 0.0001f;
            bool due = Time.unscaledTime >= _nextSendTime;
            if (!changed && !due)
                return;

            // Idle stays quiet: an unchanged zero has nothing to say to the server.
            if (world.sqrMagnitude <= 0f && _lastSent.sqrMagnitude <= 0f)
                return;

            _mover?.RequestMoveIntent(world);
            _lastSent = world;
            _nextSendTime = Time.unscaledTime + SendIntervalSeconds;
        }

        /// <summary>
        /// The keyboard as local axes: +y forward, +x right. A blocked or rebinding input endpoint
        /// reads as no input, so opening a panel stops the character instead of leaving it walking.
        /// </summary>
        private static Vector2 ReadInput()
        {
            ClientInput input = ClientInput.Current;
            if (input == null)
                return Vector2.zero;

            float forward = (input.IsHeld(GameAction.MoveForward) ? 1f : 0f)
                            - (input.IsHeld(GameAction.MoveBack) ? 1f : 0f);
            float strafe = (input.IsHeld(GameAction.StrafeRight) ? 1f : 0f)
                           - (input.IsHeld(GameAction.StrafeLeft) ? 1f : 0f);
            return new Vector2(strafe, forward);
        }

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
