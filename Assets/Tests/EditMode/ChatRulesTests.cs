using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>Pure chat rules — no bus, no roster, no Unity. One assertion per branch.</summary>
    public sealed class ChatRulesTests
    {
        [Test]
        public void CanSend_EmptyBody_IsRefused()
            => Assert.AreEqual(ChatRule.Empty, ChatRules.CanSend("   ", 100, 0, true, ChatRules.ChannelGlobal));

        [Test]
        public void CanSend_OverlongBody_IsRefused()
        {
            string tooLong = new string('x', ChatRules.MaxBodyLength + 1);

            Assert.AreEqual(ChatRule.TooLong, ChatRules.CanSend(tooLong, 100, 0, true, ChatRules.ChannelGlobal));
        }

        [Test]
        public void CanSend_InsideTheRateWindow_IsRateLimited()
            => Assert.AreEqual(ChatRule.RateLimited,
                ChatRules.CanSend("hi", 1.0, 1.5, true, ChatRules.ChannelGlobal));

        [Test]
        public void CanSend_PartyWithoutAParty_IsRefused()
            => Assert.AreEqual(ChatRule.NoParty,
                ChatRules.CanSend("hi", 100, 0, false, ChatRules.ChannelParty));

        [Test]
        public void CanSend_Valid_IsOk()
            => Assert.AreEqual(ChatRule.Ok,
                ChatRules.CanSend("hi", 100, 0, false, ChatRules.ChannelGlobal));

        [Test]
        public void ChannelName_MapsEveryChannel()
        {
            Assert.AreEqual("global", ChatRules.ChannelName(ChatRules.ChannelGlobal));
            Assert.AreEqual("zone", ChatRules.ChannelName(ChatRules.ChannelZone));
            Assert.AreEqual("party", ChatRules.ChannelName(ChatRules.ChannelParty));
            Assert.IsNull(ChatRules.ChannelName(99));
        }

        [Test]
        public void Receives_Global_ReachesEveryone()
            => Assert.IsTrue(ChatRules.Receives(new SocialBusEnvelope { ch = "global" }, "z1", null));

        [Test]
        public void Receives_Zone_NeedsAMatchingZone()
        {
            var envelope = new SocialBusEnvelope { ch = "zone", target = "z2" };

            Assert.IsFalse(ChatRules.Receives(envelope, "z1", null));
            Assert.IsTrue(ChatRules.Receives(envelope, "z2", null));
        }

        [Test]
        public void Receives_Party_NeedsAMatchingParty()
        {
            var envelope = new SocialBusEnvelope { ch = "party", target = "p1" };

            Assert.IsFalse(ChatRules.Receives(envelope, "z1", null));
            Assert.IsFalse(ChatRules.Receives(envelope, "z1", "p2"));
            Assert.IsTrue(ChatRules.Receives(envelope, "z1", "p1"));
        }
    }
}
