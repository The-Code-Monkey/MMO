using System;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server-authoritative mover: the sole writer of a player's position. A client never sends
    /// a position it believes it stands at — it sends a <em>destination</em> (<see cref="RequestMoveTo"/>)
    /// and the server validates it against the zone NavMesh and walks the player there itself. The
    /// client cannot therefore teleport by lying, because the only thing it may request is "walk
    /// towards this point", and the server decides how the player gets there.
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
        private Vector3? _moveTarget;
        private readonly WalkPath _walk = new();
        private Vector2 _directInput;
        private float _speedMultiplier = 1f;
        private float _auraSpeedMultiplier = 1f;
        private bool _flying;

        /// <summary>
        /// The speed the server integrates at: the baked base speed scaled by the mount and aura
        /// multipliers. Never negative; a stun or root drives the aura multiplier to zero.
        /// </summary>
        public float MaxSpeed =>
            Mathf.Max(0f, _maxSpeed) *
            Mathf.Max(0f, _speedMultiplier) *
            Mathf.Max(0f, _auraSpeedMultiplier);

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
        /// Owner-side intent: walk to a world position. The destination is validated on the server
        /// (it must land on the baked NavMesh) and the server drives the movement, so a client
        /// cannot teleport by lying about the target.
        /// </summary>
        public void RequestMoveTo(Vector3 destination)
        {
            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestMoveTo(destination);
        }

        /// <summary>
        /// Owner-side intent: keep walking in a camera-relative direction while the key is held.
        /// The server integrates it at its own speed; no position ever leaves the client.
        /// </summary>
        public void RequestMoveIntent(Vector2 direction)
        {
            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestMoveIntent(direction, TimeManager != null ? TimeManager.Tick : 0u);
        }

        /// <summary>
        /// The direct-drive half of the input path. A held key supersedes a destination chosen
        /// earlier — otherwise the nav walk and the keyboard would fight over the next step — and
        /// the direction is clamped so a diagonal cannot outrun a straight line.
        /// </summary>
        [ServerRpc]
        private void RpcRequestMoveIntent(Vector2 direction, uint clientTick, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            // Flight paths by destination through the air; a ground walking intent has nothing to
            // say about it.
            if (_flying)
                return;

            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            _moveTarget = null;
            _walk.Clear();
            _directInput = direction;
        }

        [ServerRpc]
        private void RpcRequestMoveTo(Vector3 destination, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
            {
                Debug.LogWarning($"[PlayerMover] RpcRequestMoveTo rejected: sender mismatch (sender={sender}, owner={Owner} on '{name}').");
                return;
            }

            // A flying mount paths through the air, where there is no NavMesh to sample: the
            // destination is the point itself, and the server still integrates the walk.
            // shortcut: flight ignores obstacles (no 3D NavMesh); add volume pathing if flight
            // must avoid walls.
            if (_flying)
            {
                _moveTarget = destination;
                _walk.Clear();
                return;
            }

            if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[PlayerMover] RpcRequestMoveTo: NavMesh.SamplePosition failed for {destination} (player at {transform.position}). The zone NavMesh is not loaded.");
                return;
            }

            _moveTarget = hit.position;
            BuildWalkPath(hit.position);
        }

        /// <summary>
        /// Server: work out the route to the destination across the zone NavMesh.
        ///
        /// <para>
        /// The route is what makes a crafting station solid: the station carves a hole in that
        /// NavMesh, and a route planned over a holed mesh bends around the hole instead of
        /// running through it. Without a complete route — no mesh, or an unreachable
        /// destination — the mover keeps walking straight, exactly as it did before.
        /// </para>
        /// </summary>
        private void BuildWalkPath(Vector3 destination)
        {
            _walk.Clear();

            // The player's transform sits at the capsule's middle, well above the mesh, so the
            // start has to be sampled onto the NavMesh before a route can be planned.
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit start, 2f, NavMesh.AllAreas))
                return;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start.position, destination, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete)
                return;

            _walk.SetCorners(path.corners);
        }

        private void Update()
        {
            if (!IsServer)
                return;

            // A held key is the freshest description of this tick: integrate it before the route,
            // which an intent already cancelled.
            if (_directInput.sqrMagnitude > 0f && !_flying)
            {
                ApplyInput(_directInput, Time.deltaTime);
                return;
            }

            if (!_moveTarget.HasValue)
                return;

            Vector3 waypoint = _walk.IsEmpty ? _moveTarget.Value : _walk.NextWaypoint(transform.position);
            Vector3 toWaypoint = waypoint - transform.position;
            if (!_flying)
                toWaypoint.y = 0f;

            float distance = toWaypoint.magnitude;
            if (distance <= 0.01f)
            {
                // Standing on the waypoint: the last corner of a route is the destination.
                _moveTarget = null;
                GetComponent<Persistence.PlayerSaveTrigger>()?.MarkDirtyOnArrival();
                return;
            }

            // The step never overshoots the waypoint, so the player lands on a corner instead
            // of cutting it — a cut corner would put the capsule inside the hole a station
            // carves, which is the very thing the route exists to avoid.
            double delta = Time.deltaTime;
            float maxStep = MaxSpeed * (float)delta;
            if (maxStep > distance)
                delta *= distance / maxStep;

            // Flight integrates the full 3D vector; ApplyInput is the ground path and is X/Z only.
            if (_flying)
            {
                MoveBy(toWaypoint.normalized * (MaxSpeed * (float)delta));
                return;
            }

            ApplyInput(new Vector2(toWaypoint.x, toWaypoint.z).normalized, delta);
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
        /// Server: scale the mover's speed, the way a mount's ground or flight multiplier does.
        /// The client never calls this, so a mount cannot make itself faster.
        /// </summary>
        [Server]
        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// Server: the aura-driven speed multiplier, kept separate from the mount multiplier so a
        /// root or a slow cannot clobber the mount's setting (and vice versa).
        /// </summary>
        [Server]
        public void SetAuraSpeedMultiplier(float multiplier)
        {
            _auraSpeedMultiplier = Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// Server: switch between ground movement (X/Z, NavMesh-pathed) and volumetric movement
        /// (X/Y/Z, straight to the destination). There is no gravity simulation to toggle; flight
        /// simply stops clamping the vertical axis.
        /// </summary>
        [Server]
        public void SetFlight(bool flying)
        {
            if (_flying == flying)
                return;

            _flying = flying;
            _moveTarget = null;
            _walk.Clear();
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

        /// <summary>
        /// Server: place the player at the position the server has decided is authoritative —
        /// the restore path a <see cref="MMO.Net.WorldBootstrap"/> uses when a character re-joins at
        /// its saved position or arrives in a new zone. Updates the anti-cheat baseline exactly
        /// like <see cref="Teleport"/>, so the restored position is not treated as a teleport.
        /// </summary>
        [Server]
        public void SetAuthoritativePosition(Vector3 position)
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
