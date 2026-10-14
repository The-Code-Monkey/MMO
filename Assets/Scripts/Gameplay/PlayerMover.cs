using System;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server-authoritative mover: the sole writer of a player's position. A client never sends
    /// a position it believes it stands at — it sends an <em>input</em> vector (<see cref="RequestMove"/>)
    /// and the server integrates it at <see cref="MaxSpeed"/> itself. The client cannot therefore
    /// teleport by lying, because there is no field in the input path for a lie to live in.
    ///
    /// <para>
    /// Any message that <em>does</em> carry a position — a future teleport, a correction, a bug —
    /// goes through <see cref="ApplyReportedPosition"/>, which judges it with the pure
    /// <see cref="MovementRules"/> and snaps the offender straight back to the last position the
    /// server itself observed. That is the belt to the input path's braces: even a rogue RPC cannot
    /// smuggle movement past the budget.
    /// </para>
    ///
    /// <para>
    /// The component works with or without a <see cref="CharacterController"/>: when one is present
    /// the move is collision-aware, and when it is not the transform is moved directly, so the
    /// minimum mover can be dropped on a bare capsule and still be tested.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMover : NetworkBehaviour
    {
        /// <summary>How fast the server will integrate input, in metres per second.</summary>
        [SerializeField]
        private float _maxSpeed = 6f;

        private CharacterController _controller;
        private Vector3 _lastAuthoritativePosition;
        private bool _hasAuthoritativePosition;

        /// <summary>The speed the server integrates at. Never negative.</summary>
        public float MaxSpeed => Mathf.Max(0f, _maxSpeed);

        /// <summary>
        /// The last position the server itself observed. Before the first server tick this reads
        /// the live transform, so a just-spawned player has a sane baseline to validate against.
        /// </summary>
        public Vector3 LastAuthoritativePosition =>
            _hasAuthoritativePosition ? _lastAuthoritativePosition : transform.position;

        public override void OnStartServer()
        {
            _controller = GetComponent<CharacterController>();
            CaptureAuthoritativePosition();
        }

        /// <summary>
        /// Owner-side intent: the input direction for this tick (x = strafe, y = forward), already
        /// clamped to the unit circle by the caller or by this method. Ownership is checked locally
        /// first and again on the server; a non-owner gets no RPC at all.
        /// </summary>
        public void RequestMove(Vector2 input)
        {
            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestMove(input);
        }

        /// <summary>
        /// The input path. It carries a direction, never a position, so there is nothing here for a
        /// cheating client to inflate — the server decides how far that input carries them.
        /// </summary>
        [ServerRpc]
        private void RpcRequestMove(Vector2 input, NetworkConnection sender = null)
        {
            // Belt and braces: FishNet already drops ServerRpcs from non-owners (RequireOwnership
            // is on), but the extra check documents that the server re-validates the intent.
            if (sender == null || !OwnerMatches(sender))
                return;

            ApplyInput(input, Time.deltaTime);
        }

        /// <summary>
        /// Server: integrate an input direction over <paramref name="deltaSeconds"/> and move.
        /// A direction longer than one is normalised so a diagonal cannot outrun a straight line.
        /// </summary>
        [Server]
        public void ApplyInput(Vector2 input, double deltaSeconds)
        {
            Vector3 direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            float step = MaxSpeed * (float)Math.Max(0d, deltaSeconds);
            MoveBy(direction * step);
        }

        /// <summary>
        /// Server: validate a position-bearing message against <see cref="MovementRules"/>. A
        /// position reachable within this tick's budget is applied; anything past it is a teleport
        /// and the mover is snapped back to the last authoritative position. Returns true when the
        /// reported position was accepted.
        /// </summary>
        [Server]
        public bool ApplyReportedPosition(Vector3 reportedPosition, double deltaSeconds)
        {
            Vector3 lastAuthoritative = LastAuthoritativePosition;
            if (!MovementRules.IsPlausible(lastAuthoritative, reportedPosition, MaxSpeed, (float)Math.Max(0d, deltaSeconds)))
            {
                SnapBack();
                return false;
            }

            SetPosition(reportedPosition);
            return true;
        }

        /// <summary>
        /// Server: a privileged reposition that <em>updates</em> the authoritative baseline rather
        /// than being judged against it. Reserved for abilities and respawns — the legitimate ways
        /// a player moves further than their legs allow — and never reachable from the input path.
        /// </summary>
        [Server]
        public void Teleport(Vector3 position)
        {
            SetPosition(position);
        }

        private void MoveBy(Vector3 motion)
        {
            if (_controller != null && _controller.enabled)
                _controller.Move(motion);
            else
                transform.position += motion;

            CaptureAuthoritativePosition();
        }

        private void SetPosition(Vector3 position)
        {
            if (_controller != null && _controller.enabled)
            {
                // A CharacterController caches its pose; toggling it is the supported way to place
                // it somewhere new without the controller dragging it back.
                _controller.enabled = false;
                transform.position = position;
                _controller.enabled = true;
            }
            else
            {
                transform.position = position;
            }

            CaptureAuthoritativePosition();
        }

        private void SnapBack()
        {
            SetPosition(LastAuthoritativePosition);
        }

        private void CaptureAuthoritativePosition()
        {
            _lastAuthoritativePosition = transform.position;
            _hasAuthoritativePosition = true;
        }
    }
}
