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

        [ServerRpc]
        private void RpcRequestMoveTo(Vector3 destination, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
            {
                Debug.LogWarning($"[PlayerMover] RpcRequestMoveTo rejected: sender mismatch (sender={sender}, owner={Owner} on '{name}').");
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
            if (!IsServer || !_moveTarget.HasValue)
                return;

            Vector3 waypoint = _walk.IsEmpty ? _moveTarget.Value : _walk.NextWaypoint(transform.position);
            Vector3 toWaypoint = waypoint - transform.position;
            toWaypoint.y = 0f;

            float distance = toWaypoint.magnitude;
            if (distance <= 0.01f)
            {
                // Standing on the waypoint: the last corner of a route is the destination.
                _moveTarget = null;
                return;
            }

            // The step never overshoots the waypoint, so the player lands on a corner instead
            // of cutting it — a cut corner would put the capsule inside the hole a station
            // carves, which is the very thing the route exists to avoid.
            double delta = Time.deltaTime;
            float maxStep = MaxSpeed * (float)delta;
            if (maxStep > distance)
                delta *= distance / maxStep;

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
