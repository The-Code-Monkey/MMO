using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The arithmetic behind client settings: what values are legal and how a millisecond
    /// interpolation target becomes a FishNet tick count.
    ///
    /// Pure by design, so a corrupt settings file or an out-of-range slider can be pinned in an
    /// EditMode test without a scene, an audio device or a running network.
    /// </summary>
    public static class SettingsRules
    {
        public const int DefaultInterpolationMs = 100;
        public const int MinInterpolationMs = 50;
        public const int MaxInterpolationMs = 250;

        /// <summary>
        /// Clamps every value to what the engine can actually apply.
        /// A null model becomes the defaults; the model is mutated and returned.
        /// </summary>
        public static SettingsModel Sanitize(SettingsModel model, int qualityLevelCount)
        {
            model ??= SettingsModel.Defaults();

            int maxQuality = Mathf.Max(0, qualityLevelCount - 1);
            model.QualityLevel = Mathf.Clamp(model.QualityLevel, 0, maxQuality);
            model.MasterVolume = Mathf.Clamp01(model.MasterVolume);
            model.MusicVolume = Mathf.Clamp01(model.MusicVolume);
            model.SfxVolume = Mathf.Clamp01(model.SfxVolume);
            model.NetworkInterpolationMs = Mathf.Clamp(
                model.NetworkInterpolationMs, MinInterpolationMs, MaxInterpolationMs);

            return model;
        }

        /// <summary>
        /// Converts an interpolation delay to FishNet's tick count, floored at one tick because
        /// <c>NetworkTransform.SetInterpolation</c> treats zero as one anyway.
        /// </summary>
        public static ushort NetworkInterpolationTicks(int milliseconds, ushort tickRate)
        {
            if (tickRate == 0)
                return 1;

            int ticks = Mathf.RoundToInt(milliseconds / 1000f * tickRate);
            return (ushort)Mathf.Clamp(ticks, 1, 64);
        }
    }
}
