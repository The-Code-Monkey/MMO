using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The defaults are what a player gets on first launch, so they must be sane without a file.
    /// </summary>
    public sealed class SettingsModelTests
    {
        [Test]
        public void Defaults_ShowAllyEffects_AndUseMediumQuality()
        {
            SettingsModel settings = SettingsModel.Defaults();

            Assert.IsTrue(settings.ShowAllySpellEffects);
            Assert.AreEqual(2, settings.QualityLevel);
            Assert.AreEqual(1f, settings.MasterVolume);
            Assert.AreEqual(1f, settings.SfxVolume);
            Assert.AreEqual(SettingsRules.DefaultInterpolationMs, settings.NetworkInterpolationMs);
        }

        [Test]
        public void Clone_CopiesEveryValue_AndDoesNotAliasTheOriginal()
        {
            var original = new SettingsModel
            {
                QualityLevel = 4,
                ShowAllySpellEffects = false,
                MasterVolume = 0.4f,
                MusicVolume = 0.3f,
                SfxVolume = 0.2f,
                NetworkInterpolationMs = 150,
            };

            SettingsModel clone = original.Clone();

            Assert.AreNotSame(original, clone);
            Assert.AreEqual(original.QualityLevel, clone.QualityLevel);
            Assert.AreEqual(original.ShowAllySpellEffects, clone.ShowAllySpellEffects);
            Assert.AreEqual(original.MasterVolume, clone.MasterVolume);
            Assert.AreEqual(original.MusicVolume, clone.MusicVolume);
            Assert.AreEqual(original.SfxVolume, clone.SfxVolume);
            Assert.AreEqual(original.NetworkInterpolationMs, clone.NetworkInterpolationMs);

            clone.MasterVolume = 0f;
            Assert.AreEqual(0.4f, original.MasterVolume, "Editing the clone must not touch the source.");
        }
    }
}
