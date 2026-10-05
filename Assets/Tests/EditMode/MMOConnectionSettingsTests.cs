using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Defaults matter: a scene baked without touching the inspector has to come up
    /// listening on a sane port with sane limits.
    /// </summary>
    public sealed class MMOConnectionSettingsTests
    {
        [Test]
        public void Defaults_AreUsable()
        {
            var settings = new MMOConnectionSettings();

            Assert.AreEqual(MMOConnectionSettings.DefaultPort, settings.Port);
            Assert.AreEqual(MMOConnectionSettings.DefaultMaxClients, settings.MaxClients);
            Assert.AreEqual(MMOConnectionSettings.DefaultHandshakeTimeoutSeconds, settings.HandshakeTimeoutSeconds);
            Assert.IsFalse(settings.MaintenanceMode);
            Assert.IsEmpty(settings.AllowedPlayers);
            Assert.IsEmpty(settings.BlockedPlayers);
        }

        [Test]
        public void Defaults_AreDocumentedValues()
        {
            Assert.AreEqual((ushort)7770, MMOConnectionSettings.DefaultPort);
            Assert.AreEqual(64, MMOConnectionSettings.DefaultMaxClients);
            Assert.AreEqual(5f, MMOConnectionSettings.DefaultHandshakeTimeoutSeconds);
        }

        [Test]
        public void Overload_StoresEveryValue()
        {
            var settings = new MMOConnectionSettings(
                port: 1234,
                maxClients: 8,
                handshakeTimeoutSeconds: 2.5f,
                maintenanceMode: true,
                allowedPlayers: new[] { "Alice" },
                blockedPlayers: new[] { "Bob" });

            Assert.AreEqual((ushort)1234, settings.Port);
            Assert.AreEqual(8, settings.MaxClients);
            Assert.AreEqual(2.5f, settings.HandshakeTimeoutSeconds);
            Assert.IsTrue(settings.MaintenanceMode);
            CollectionAssert.AreEqual(new[] { "Alice" }, settings.AllowedPlayers);
            CollectionAssert.AreEqual(new[] { "Bob" }, settings.BlockedPlayers);
        }

        [Test]
        public void Overload_OptionalFlagsDefaultToOpenServer()
        {
            var settings = new MMOConnectionSettings(1, 2, 3f);

            Assert.IsFalse(settings.MaintenanceMode);
            Assert.IsEmpty(settings.AllowedPlayers);
            Assert.IsEmpty(settings.BlockedPlayers);
        }

        [Test]
        public void Overload_NullLists_BecomeEmptyNotNull()
        {
            var settings = new MMOConnectionSettings(1, 2, 3f, allowedPlayers: null, blockedPlayers: null);

            Assert.IsNotNull(settings.AllowedPlayers);
            Assert.IsNotNull(settings.BlockedPlayers);
        }
    }
}