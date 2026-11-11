using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The raid push payload: it is ids-only by contract, rides the `raid` bus type, and survives the
    /// bus-envelope round trip so a receiving zone sees the same ids a reconcile pass would resolve.
    /// </summary>
    public sealed class SocialNotificationTests
    {
        [Test]
        public void ToEnvelope_UsesTheRaidBusTypeAndCarriesIdsOnly()
        {
            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.RaidAllocated,
                RaidId = "r1",
                PartyId = "p1",
                HostZoneId = "server-a",
                AccountIds = { "a", "b" },
            };

            SocialBusEnvelope envelope = SocialNotification.ToEnvelope(notification);

            Assert.AreEqual(SocialNotification.WireType, envelope.t);
            Assert.AreEqual("r1", envelope.target);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, envelope.accounts);
        }

        [Test]
        public void RoundTrip_ThroughTheBusEnvelope_PreservesTheRaidIdAndAccounts()
        {
            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.RaidAllocated,
                RaidId = "r1",
                AccountIds = { "a", "b" },
            };

            string json = JsonUtility.ToJson(SocialNotification.ToEnvelope(notification));
            SocialBusEnvelope parsed = JsonUtility.FromJson<SocialBusEnvelope>(json);
            SocialNotification restored = SocialNotification.FromEnvelope(parsed);

            Assert.AreEqual("r1", restored.RaidId);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, restored.AccountIds);
        }

        [Test]
        public void MarketChanged_UsesTheMarketBusTypeAndCarriesTheListingId()
        {
            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.MarketChanged,
                ListingId = "listing-1",
            };

            SocialBusEnvelope envelope = SocialNotification.ToEnvelope(notification);

            Assert.AreEqual(SocialNotification.MarketWireType, envelope.t);
            Assert.AreEqual("listing-1", envelope.target);
        }

        [Test]
        public void MarketChanged_RoundTrip_PreservesTheListingId()
        {
            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.MarketChanged,
                ListingId = "listing-1",
            };

            string json = JsonUtility.ToJson(SocialNotification.ToEnvelope(notification));
            SocialNotification restored = SocialNotification.FromEnvelope(JsonUtility.FromJson<SocialBusEnvelope>(json));

            Assert.AreEqual(SocialNotificationKind.MarketChanged, restored.Kind);
            Assert.AreEqual("listing-1", restored.ListingId);
        }

        [Test]
        public void FromEnvelope_Null_ReturnsNull()
        {
            Assert.IsNull(SocialNotification.FromEnvelope(null));
        }

        [Test]
        public void ToEnvelope_Null_ReturnsNull()
        {
            Assert.IsNull(SocialNotification.ToEnvelope(null));
        }
    }
}
