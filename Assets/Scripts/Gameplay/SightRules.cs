using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server's line-of-sight decision as pure functions: whether a target is close enough,
    /// whether it sits inside the attacker's facing cone, and — combining the two with the
    /// server's own raycast — whether the attacker can actually see it.
    ///
    /// The raycast itself is I/O and lives behind the <see cref="IServerSight"/> seam, so this
    /// type stays free of physics and a scene: an EditMode test can assert that a blocked raycast
    /// vetoes an otherwise valid shot without ever spawning a wall.
    /// </summary>
    public static class SightRules
    {
        /// <summary>Squared-length below which a vector is treated as zero and its direction undefined.</summary>
        private const float DirectionEpsilonSqr = 1e-8f;

        /// <summary>
        /// The one verdict every attack and ability consults: the target is visible only when it
        /// is in range, inside the facing cone, and the server's raycast to it is clear. A single
        /// blocked ray is enough to refuse, exactly like being out of range.
        /// </summary>
        public static bool CanSee(bool withinRange, bool withinCone, bool raycastClear)
        {
            return withinRange && withinCone && raycastClear;
        }

        /// <summary>
        /// True when the target is strictly closer than <paramref name="range"/>. "Strictly":
        /// a target parked exactly on the boundary is out of reach, matching the combat, gathering
        /// and looting reach tests so one boundary reads the same everywhere.
        /// </summary>
        public static bool WithinRange(Vector3 eyePosition, Vector3 targetPosition, float range)
        {
            return range > 0f && Vector3.Distance(eyePosition, targetPosition) < range;
        }

        /// <summary>
        /// True when <paramref name="targetPosition"/> lies inside the cone of
        /// <paramref name="halfAngleDegrees"/> around <paramref name="forward"/>.
        ///
        /// A target at the eye itself counts as visible (there is no direction to be outside of),
        /// and a zero <paramref name="forward"/> cannot see anything beyond the eye: a caster with
        /// no facing has no cone. A half-angle of 180° or more sees everything.
        /// </summary>
        public static bool WithinCone(
            Vector3 eyePosition,
            Vector3 forward,
            Vector3 targetPosition,
            float halfAngleDegrees)
        {
            Vector3 toTarget = targetPosition - eyePosition;
            if (toTarget.sqrMagnitude <= DirectionEpsilonSqr)
                return true;

            if (forward.sqrMagnitude <= DirectionEpsilonSqr)
                return false;

            float cosToTarget = Vector3.Dot(forward.normalized, toTarget.normalized);
            float cosHalfAngle = Mathf.Cos(Mathf.Clamp(halfAngleDegrees, 0f, 180f) * Mathf.Deg2Rad);

            // Dot products can drift a hair past the theoretical bounds; clamp so the comparison
            // at exactly 0° or 180° is decided by the angle, not by float rounding.
            return Mathf.Clamp(cosToTarget, -1f, 1f) >= cosHalfAngle;
        }
    }
}
