using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Converts the linear 0–1 values a settings slider produces into the decibels an
    /// <c>AudioMixer</c> group expects. Pure so the curve is pinned without an audio device.
    /// </summary>
    public static class MixerVolume
    {
        /// <summary>The mixer's silence floor; matches Unity's default minimum volume.</summary>
        public const float MinimumDecibels = -80f;

        /// <summary>
        /// 1 → 0 dB, 0.5 → about −6 dB, 0 → the mixer's silence floor. Values outside 0–1 clamp.
        /// </summary>
        public static float ToDecibels(float linear)
        {
            if (linear <= 0f)
                return MinimumDecibels;

            return Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
        }
    }
}
