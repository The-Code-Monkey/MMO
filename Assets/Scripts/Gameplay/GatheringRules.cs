using System;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The gathering loop's decision surface as pure functions: how close is close enough, when
    /// does the next tick land, how much durability is left after a harvest, and when a depleted
    /// node grows back.
    ///
    /// Every method is static, side-effect free and takes the clock as an argument for the same
    /// reason as <see cref="AbilityCooldownTracker"/>: an EditMode test can gather at
    /// <c>t = 100</c> and assert the next tick at <c>t = 101.2</c> in microseconds, without a
    /// server, a scene or a single frame of waiting. The two networked halves —
    /// <see cref="ResourceNode"/> and <see cref="PlayerGathering"/> — own none of this arithmetic;
    /// they validate through these functions and only then mutate replicated state.
    ///
    /// The clock is the server's (<c>Time.timeAsDouble</c>, the same clock the ability system
    /// runs on), so the tick timer and the respawn timer advance on authority time and never on
    /// whatever a client's stopwatch claims.
    ///
    /// <para>
    /// The tool gate is not here: it belongs to <see cref="ToolRules"/>, next to the tool data
    /// it reads, because "which kind of tool, at which tier" is a question about items and not
    /// about the gathering loop.
    /// </para>
    /// </summary>
    public static class GatheringRules
    {
        /// <summary>
        /// True when <paramref name="gathererPosition"/> is strictly closer to the node than
        /// <paramref name="reach"/>. "Strictly": a gatherer parked exactly on the boundary is out
        /// of reach, matching <see cref="PlayerCombat.IsWithinAttackRange"/> so the two interaction
        /// loops refuse a floating-point boundary the same way.
        /// </summary>
        public static bool IsWithinReach(Vector3 gathererPosition, Vector3 nodePosition, float reach)
        {
            return reach > 0f && Vector3.Distance(gathererPosition, nodePosition) < reach;
        }

        /// <summary>True when the server's clock has reached the tick booked for <paramref name="nextTickAt"/>.</summary>
        public static bool IsTickDue(double nextTickAt, double now)
        {
            return now >= nextTickAt;
        }

        /// <summary>
        /// When the tick after <paramref name="now"/> lands. A negative or zero interval collapses
        /// onto <paramref name="now"/> rather than moving the tick backwards — a misauthored node
        /// with a negative interval must not be able to freeze its own loop in the past.
        /// </summary>
        public static double ComputeNextTick(double now, float intervalSeconds)
        {
            return now + Math.Max(0d, intervalSeconds);
        }

        /// <summary>When a node depleted at <paramref name="now"/> is due to grow back.</summary>
        public static double ComputeRespawnAt(double now, float respawnSeconds)
        {
            return now + Math.Max(0d, respawnSeconds);
        }

        /// <summary>True when the server's clock has reached a respawn booked for <paramref name="respawnAt"/>.</summary>
        public static bool IsRespawnDue(double respawnAt, double now)
        {
            return now >= respawnAt;
        }

        /// <summary>
        /// Durability left after one harvest: one tick spent, never below zero, and a negative
        /// input reads as empty rather than as "about to wrap around".
        /// </summary>
        public static int ComputeRemaining(int remaining)
        {
            return Math.Max(0, remaining - 1);
        }
    }
}
