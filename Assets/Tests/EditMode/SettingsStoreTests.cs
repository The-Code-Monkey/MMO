using System.IO;
using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The store owns the one guarantee a preferences file needs: a missing or corrupt file
    /// becomes working defaults, never an exception that stops the client.
    /// </summary>
    public sealed class SettingsStoreTests
    {
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "mmo-settings-tests-" + System.Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_directory, SettingsStore.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Load_MissingFile_ReturnsDefaults()
        {
            SettingsModel settings = new SettingsStore(_path).Load(qualityLevelCount: 6);

            Assert.IsTrue(settings.ShowAllySpellEffects);
            Assert.AreEqual(2, settings.QualityLevel);
        }

        [Test]
        public void Save_ThenLoad_RoundTripsEveryValue()
        {
            var store = new SettingsStore(_path);
            var saved = new SettingsModel
            {
                QualityLevel = 4,
                ShowAllySpellEffects = false,
                MasterVolume = 0.25f,
                MusicVolume = 0.5f,
                SfxVolume = 0.75f,
                NetworkInterpolationMs = 150,
            };

            store.Save(saved, qualityLevelCount: 6);
            SettingsModel loaded = store.Load(qualityLevelCount: 6);

            Assert.AreEqual(4, loaded.QualityLevel);
            Assert.IsFalse(loaded.ShowAllySpellEffects);
            Assert.AreEqual(0.25f, loaded.MasterVolume);
            Assert.AreEqual(0.5f, loaded.MusicVolume);
            Assert.AreEqual(0.75f, loaded.SfxVolume);
            Assert.AreEqual(150, loaded.NetworkInterpolationMs);
        }

        [Test]
        public void Load_CorruptFile_ReturnsDefaults()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_path, "{ this is not json");

            SettingsModel settings = new SettingsStore(_path).Load(qualityLevelCount: 6);

            Assert.IsTrue(settings.ShowAllySpellEffects);
            Assert.AreEqual(SettingsRules.DefaultInterpolationMs, settings.NetworkInterpolationMs);
        }

        [Test]
        public void LoadFromJson_ClampsOutOfRangeValues()
        {
            SettingsModel settings = SettingsStore.LoadFromJson(
                "{\"QualityLevel\":99,\"MasterVolume\":4,\"NetworkInterpolationMs\":5}",
                qualityLevelCount: 3);

            Assert.AreEqual(2, settings.QualityLevel);
            Assert.AreEqual(1f, settings.MasterVolume);
            Assert.AreEqual(SettingsRules.MinInterpolationMs, settings.NetworkInterpolationMs);
        }

        [Test]
        public void ToJson_DoesNotMutateTheCaller()
        {
            var settings = new SettingsModel { QualityLevel = 99, MasterVolume = 2f };

            SettingsStore.ToJson(settings, qualityLevelCount: 6);

            Assert.AreEqual(99, settings.QualityLevel);
            Assert.AreEqual(2f, settings.MasterVolume);
        }

        [Test]
        public void LoadFromJson_EmptyString_ReturnsDefaults()
        {
            SettingsModel settings = SettingsStore.LoadFromJson(string.Empty, qualityLevelCount: 6);

            Assert.AreEqual(2, settings.QualityLevel);
        }
    }
}
