using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server's movement authority as pure arithmetic: given the last position the server
    /// itself observed, how far could a legitimate mover have travelled, and where does the
    /// server put a reported position that could not have been reached.
    ///
    /// Nothing here reads a clock, allocates, or touches a scene, for the same reason
    /// <see cref="RiskRules"/> and <see cref="GatheringRules"/> are static: an EditMode test can
    /// judge a fifty-metre teleport in microseconds. The networked half — a server-only
    /// <c>PlayerMover</c> — owns none of this arithmetic; it integrates client *input* itself and
    /// only ever calls <see cref="IsPlausible"/> when a position-bearing message must be judged.
    ///
    /// The grace multiplier is what keeps high-latency legs honest: a client whose messages arrive
    /// a few frames late can still cover slightly more ground than the strict speed limit without
    /// being snapped back, while a teleport of any meaningful size stays far outside the budget.
    /// </summary>
    public static class MovementRules
    {
        /// <summary>
        /// Slack added to every movement budget, so floating-point noise and a legitimate
        /// frame of jitter never read as a teleport.
        /// </summary>
        public const float SnapTolerance = 0.01f;

        /// <summary>How much extra distance over the nominal speed limit is tolerated.</summary>
        public const float DefaultGrace = 1.25f;

        /// <summary>
        /// The furthest distance a legitimate mover may cover in <paramref name="deltaSeconds"/>
        /// at <paramref name="maxSpeed"/>. Non-positive inputs read as zero rather than as a
        /// negative budget, so a bad argument can never make every move look illegal.
        /// </summary>
        public static float MaxDistance(float maxSpeed, float deltaSeconds, float grace = DefaultGrace)
        {
            return Mathf.Max(0f, maxSpeed) * Mathf.Max(0f, deltaSeconds) * Mathf.Max(0f, grace) + SnapTolerance;
        }

        /// <summary>
        /// True when a reported position is reachable from the last server-authoritative position
        /// within the movement budget. The boundary itself is legal (a move that lands exactly on
        /// the limit is allowed); anything past it is rejected.
        /// </summary>
        public static bool IsPlausible(
            Vector3 lastServerPosition,
            Vector3 reportedPosition,
            float maxSpeed,
            float deltaSeconds,
            float grace = DefaultGrace)
        {
            float distance = Vector3.Distance(lastServerPosition, reportedPosition);
            return distance <= MaxDistance(maxSpeed, deltaSeconds, grace);
        }

        /// <summary>
        /// Where the server places an offender: the last position it knows to be legal, never a
        /// mid-air guess or a partial correction toward the reported one.
        /// </summary>
        public static Vector3 SnapBack(Vector3 lastServerPosition)
        {
            return lastServerPosition;
        }
    }
}
