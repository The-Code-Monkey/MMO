using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Settings come from a file a player can edit, so every illegal value is clamped rather
    /// than trusted. These tests pin the clamps and the millisecond-to-tick conversion.
    /// </summary>
    public sealed class SettingsRulesTests
    {
        [Test]
        public void Sanitize_ClampsQualityToAvailableLevels()
        {
            var settings = new SettingsModel { QualityLevel = 99 };

            SettingsRules.Sanitize(settings, qualityLevelCount: 6);

            Assert.AreEqual(5, settings.QualityLevel);
        }

        [Test]
        public void Sanitize_ClampsNegativeQualityToLowest()
        {
            var settings = new SettingsModel { QualityLevel = -4 };

            SettingsRules.Sanitize(settings, qualityLevelCount: 6);

            Assert.AreEqual(0, settings.QualityLevel);
        }

        [Test]
        public void Sanitize_NoQualityLevels_FallsBackToZero()
        {
            var settings = new SettingsModel { QualityLevel = 3 };

            SettingsRules.Sanitize(settings, qualityLevelCount: 0);

            Assert.AreEqual(0, settings.QualityLevel);
        }

        [Test]
        public void Sanitize_ClampsVolumesToUnitRange()
        {
            var settings = new SettingsModel
            {
                MasterVolume = 3f,
                MusicVolume = -1f,
                SfxVolume = 0.25f,
            };

            SettingsRules.Sanitize(settings, qualityLevelCount: 6);

            Assert.AreEqual(1f, settings.MasterVolume);
            Assert.AreEqual(0f, settings.MusicVolume);
            Assert.AreEqual(0.25f, settings.SfxVolume);
        }

        [Test]
        public void Sanitize_ClampsInterpolationToSafeRange()
        {
            var tooLow = new SettingsModel { NetworkInterpolationMs = 1 };
            var tooHigh = new SettingsModel { NetworkInterpolationMs = 5000 };

            SettingsRules.Sanitize(tooLow, qualityLevelCount: 6);
            SettingsRules.Sanitize(tooHigh, qualityLevelCount: 6);

            Assert.AreEqual(SettingsRules.MinInterpolationMs, tooLow.NetworkInterpolationMs);
            Assert.AreEqual(SettingsRules.MaxInterpolationMs, tooHigh.NetworkInterpolationMs);
        }

        [Test]
        public void Sanitize_NullModel_ReturnsDefaults()
        {
            SettingsModel settings = SettingsRules.Sanitize(null, qualityLevelCount: 6);

            Assert.IsNotNull(settings);
            Assert.AreEqual(2, settings.QualityLevel);
        }

        [Test]
        public void NetworkInterpolationTicks_ConvertsMillisecondsAtTickRate()
        {
            Assert.AreEqual((ushort)6, SettingsRules.NetworkInterpolationTicks(100, tickRate: 60));
        }

        [Test]
        public void NetworkInterpolationTicks_ZeroTicksRate_FloorsAtOne()
        {
            Assert.AreEqual((ushort)1, SettingsRules.NetworkInterpolationTicks(100, tickRate: 0));
        }

        [Test]
        public void NetworkInterpolationTicks_ClampsHugeDelayToMaximum()
        {
            Assert.AreEqual((ushort)64, SettingsRules.NetworkInterpolationTicks(60000, tickRate: 60));
        }
    }
}
