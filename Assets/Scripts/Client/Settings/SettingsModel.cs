using System;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// The client's local preferences: video quality, audio levels, network smoothing and the
    /// ZvZ toggle that hides other players' spell effects.
    ///
    /// <para>
    /// Plain public fields on purpose — <see cref="JsonUtility"/> writes and reads them directly,
    /// and a settings panel binds to the same object without a second property layer. Nothing in
    /// this type touches Unity state or the network: <see cref="SettingsRules"/> clamps the values
    /// and <see cref="SettingsApplier"/> is the only code that applies them to the engine.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SettingsModel
    {
        /// <summary>Quality level index into <c>QualitySettings.names</c>.</summary>
        public int QualityLevel = 2;

        /// <summary>
        /// When false, effects cast by players other than this client are not drawn. Local
        /// effects always play; damage is unaffected because it is server-authoritative.
        /// </summary>
        public bool ShowAllySpellEffects = true;

        public float MasterVolume = 1f;
        public float MusicVolume = 0.7f;
        public float SfxVolume = 1f;

        /// <summary>Target interpolation delay in milliseconds, clamped by <see cref="SettingsRules"/>.</summary>
        public int NetworkInterpolationMs = 100;

        /// <summary>A fresh copy of the shipped defaults.</summary>
        public static SettingsModel Defaults() => new();

        /// <summary>Copies every value so a panel can edit a draft before applying it.</summary>
        public SettingsModel Clone()
        {
            return new SettingsModel
            {
                QualityLevel = QualityLevel,
                ShowAllySpellEffects = ShowAllySpellEffects,
                MasterVolume = MasterVolume,
                MusicVolume = MusicVolume,
                SfxVolume = SfxVolume,
                NetworkInterpolationMs = NetworkInterpolationMs,
            };
        }
    }
}
