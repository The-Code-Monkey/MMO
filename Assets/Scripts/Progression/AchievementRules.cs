using UnityEngine;

namespace MMO.Progression
{
    /// <summary>The result of applying one event to one achievement's counter.</summary>
    public readonly struct AchievementProgress
    {
        /// <summary>True when the counter reached the threshold on this evaluation.</summary>
        public bool Completed { get; }

        /// <summary>The clamped counter after the event; never above the threshold.</summary>
        public int Value { get; }

        public AchievementProgress(bool completed, int value)
        {
            Completed = completed;
            Value = value;
        }
    }

    /// <summary>
    /// Achievement arithmetic as pure functions: monotonic counters, clamping and the
    /// grant-once guard. A replayed, duplicated or out-of-order event therefore cannot move a
    /// counter backwards or pay a reward twice.
    /// </summary>
    public static class AchievementRules
    {
        /// <summary>
        /// Adds <paramref name="increment"/> to <paramref name="current"/>, clamped to
        /// <c>0..threshold</c>. A non-positive increment is a no-op, so a malformed event cannot
        /// reset progress, and an overshoot clamps to the threshold rather than growing forever.
        /// </summary>
        public static AchievementProgress Progress(int threshold, int current, int increment)
        {
            int target = Mathf.Max(1, threshold);
            int value = Mathf.Clamp(current + Mathf.Max(0, increment), 0, target);
            return new AchievementProgress(value >= target, value);
        }

        /// <summary>True only on the transition into completion, so a replay never re-grants.</summary>
        public static bool ShouldGrant(bool wasComplete, bool nowComplete)
            => nowComplete && !wasComplete;
    }
}
