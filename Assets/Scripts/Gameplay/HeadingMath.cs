using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure arithmetic for the character's heading: turning at a fixed rate and keeping the angle in
    /// one turn's worth of degrees. The mover owns the heading; nothing else writes it.
    /// </summary>
    public static class HeadingMath
    {
        /// <summary>Degrees per second A and D turn the character.</summary>
        public const float DegreesPerSecond = 120f;

        /// <summary>
        /// The next heading after turning by <paramref name="turnAxis"/> (-1 left, +1 right) for
        /// <paramref name="deltaSeconds"/>. A non-positive delta leaves the heading alone.
        /// </summary>
        public static float NextYaw(float currentYaw, float turnAxis, float deltaSeconds)
        {
            float step = Mathf.Clamp(turnAxis, -1f, 1f) * DegreesPerSecond * Mathf.Max(0f, deltaSeconds);
            return Normalize(currentYaw + step);
        }

        /// <summary>The same heading expressed in 0..360 degrees.</summary>
        public static float Normalize(float yawDegrees)
        {
            float wrapped = yawDegrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
