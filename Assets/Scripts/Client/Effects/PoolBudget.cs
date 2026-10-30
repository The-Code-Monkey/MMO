using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// How large a presentation pool is prewarmed for an expected number of simultaneous effects.
    ///
    /// Pure so the number a 50-player fight preallocates is a tested rule, not a number copied
    /// into each spawn site. Headroom covers the burst before lifetime expiry; the maximum stops
    /// a malformed expectation from allocating unbounded memory on the client.
    /// </summary>
    public static class PoolBudget
    {
        public const int DefaultMax = 256;

        /// <summary>
        /// Expected concurrency plus headroom, rounded up, clamped to <paramref name="max"/> and
        /// never below one.
        /// </summary>
        public static int Prewarm(int expectedConcurrent, float headroom = 1.5f, int max = DefaultMax)
        {
            int capacity = Mathf.CeilToInt(Mathf.Max(0, expectedConcurrent) * Mathf.Max(1f, headroom));
            return Mathf.Clamp(capacity, 1, Mathf.Max(1, max));
        }
    }
}
