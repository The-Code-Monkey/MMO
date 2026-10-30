using UnityEngine;

namespace MMO.Client
{
    /// <summary>The three states the player's locomotion blend tree understands.</summary>
    public enum PlayerLocomotionState
    {
        Idle,
        Walk,
        Run
    }

    /// <summary>
    /// Pure mapping from replicated movement speed to animation state. Kept out of the visual
    /// component so the thresholds can be covered without an Animator or a live scene.
    /// </summary>
    public static class PlayerLocomotionMath
    {
        public const float DefaultWalkThreshold = 0.1f;
        public const float DefaultRunThreshold = 0.65f;

        /// <summary>Movement speed as a 0..1 fraction of the mover's top speed.</summary>
        public static float NormalizeSpeed(float speed, float maxSpeed)
        {
            if (maxSpeed <= 0f)
                return 0f;

            return Mathf.Clamp01(Mathf.Max(0f, speed) / maxSpeed);
        }

        /// <summary>
        /// Resolves the state a normalised speed belongs to. The run threshold is below 1 so a
        /// mount at exactly the base top speed still reads as running rather than walking.
        /// </summary>
        public static PlayerLocomotionState Resolve(
            float speed,
            float maxSpeed,
            float walkThreshold = DefaultWalkThreshold,
            float runThreshold = DefaultRunThreshold)
        {
            float normalised = NormalizeSpeed(speed, maxSpeed);
            if (normalised < walkThreshold)
                return PlayerLocomotionState.Idle;

            return normalised < runThreshold
                ? PlayerLocomotionState.Walk
                : PlayerLocomotionState.Run;
        }
    }
}
