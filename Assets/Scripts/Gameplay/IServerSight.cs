using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The server's line-of-sight check, isolated behind an interface so the one impure part — a
    /// physics raycast — can be replaced in a test without spawning a wall.
    ///
    /// <para>
    /// <see cref="SightRules"/> owns the decision ("is the target in range, inside the cone, and
    /// not behind cover?") as pure arithmetic; this seam owns only the raw "does a ray reach the
    /// target?" question the rules consume. A live server supplies <see cref="PhysicsSight"/>; a
    /// test supplies a stub that answers true or false on command.
    /// </para>
    /// </summary>
    public interface IServerSight
    {
        /// <summary>
        /// True when nothing solid stands between <paramref name="eyePosition"/> and
        /// <paramref name="targetPosition"/>. The <paramref name="target"/> (may be null) is the
        /// object being aimed at: its own collider is not cover, so a ray that reaches it counts
        /// as clear even though it ends inside it.
        /// </summary>
        bool HasLineOfSight(Vector3 eyePosition, Vector3 targetPosition, GameObject target);
    }
}
