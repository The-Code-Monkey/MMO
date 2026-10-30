using FishNet.Component.Transforming;
using FishNet.Managing;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The one place a <see cref="SettingsModel"/> becomes engine state: quality level and the
    /// FishNet interpolation delay every <see cref="NetworkTransform"/> uses.
    ///
    /// Audio bus volumes join this class in the audio milestone, once a mixer exists; the model
    /// already carries them so this API does not change when they do.
    /// </summary>
    public static class SettingsApplier
    {
        /// <summary>
        /// Sanitizes and applies <paramref name="settings"/>, returning the values that were
        /// actually applied. A null model applies the defaults.
        /// </summary>
        public static SettingsModel Apply(
            SettingsModel settings,
            NetworkManager networkManager = null,
            int qualityLevelCount = -1)
        {
            int qualityCount = qualityLevelCount >= 0 ? qualityLevelCount : QualitySettings.names.Length;
            SettingsModel sanitized = SettingsRules.Sanitize(
                (settings ?? SettingsModel.Defaults()).Clone(), qualityCount);

            QualitySettings.SetQualityLevel(sanitized.QualityLevel, applyExpensiveChanges: true);
            ApplyNetworkInterpolation(sanitized, networkManager);

            return sanitized;
        }

        /// <summary>
        /// Pushes <see cref="SettingsModel.NetworkInterpolationMs"/> into every spawned
        /// <see cref="NetworkTransform"/>, clamped to the tick range the smoother accepts.
        /// </summary>
        public static void ApplyNetworkInterpolation(SettingsModel settings, NetworkManager networkManager = null)
        {
            if (settings == null)
                return;

            networkManager ??= Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null || networkManager.TimeManager == null)
                return;

            ushort ticks = SettingsRules.NetworkInterpolationTicks(
                settings.NetworkInterpolationMs, networkManager.TimeManager.TickRate);

            NetworkTransform[] transforms = Object.FindObjectsByType<NetworkTransform>(FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null)
                    transforms[i].SetInterpolation(ticks);
            }
        }
    }
}
