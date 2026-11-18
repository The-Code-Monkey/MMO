using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure arithmetic for the jump: the impulse that reaches a given apex under the server's
    /// gravity. Kept here so the feel is a number a test can pin rather than a magic constant in
    /// the mover's tick.
    /// </summary>
    public static class JumpMath
    {
        /// <summary>Downward acceleration, in metres per second squared. Snappier than Earth.</summary>
        public const float Gravity = -20f;

        /// <summary>How high a full jump reaches, in metres.</summary>
        public const float JumpHeight = 1.1f;

        /// <summary>The upward speed that reaches <paramref name="height"/> under <paramref name="gravity"/>.</summary>
        public static float ImpulseSpeed(float height, float gravity)
            => Mathf.Sqrt(2f * Mathf.Abs(gravity) * Mathf.Max(0f, height));

        /// <summary>The apex a given upward speed reaches under <paramref name="gravity"/>.</summary>
        public static float ApexHeight(float impulseSpeed, float gravity)
        {
            float g = Mathf.Abs(gravity);
            return g <= 0f ? 0f : impulseSpeed * impulseSpeed / (2f * g);
        }
    }
}
