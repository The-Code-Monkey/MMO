using System;
using System.Collections.Generic;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// <see cref="ChatService"/> against fakes: global/party are submitted to the bus and come back
    /// through <see cref="ChatService.RouteInbound"/>; zone short-circuits locally. No socket, no
    /// FishNet — the routing decision is the unit under test.
    /// </summary>
    public sealed class ChatRoutingTests
    {
        private FakeBus _bus;
        private FakeRoster _roster;
        private ChatService _chat;

        [SetUp]
        public void SetUp()
        {
            _bus = new FakeBus();
            _roster = new FakeRoster();
            _chat = new ChatService(_bus, _roster);
        }

        private LocalPlayerHandle AddLocal(string accountId, string name, string partyId = null)
        {
            var handle = new LocalPlayerHandle
            {
                AccountId = accountId,
                PlayerName = name,
                PartyId = partyId,
                DeliverChat = _ => { },
            };

            _roster.Handles.Add(handle);
            _roster.Names[accountId] = name;
            if (!string.IsNullOrEmpty(partyId))
                _roster.Parties[accountId] = partyId;

            return handle;
        }

        [Test]
        public void Send_Global_PublishesToTheBus()
        {
            ChatRule rule = _chat.Send("a", ChatRules.ChannelGlobal, "hello", 100);

            Assert.AreEqual(ChatRule.Ok, rule);
            Assert.AreEqual(1, _bus.Sent.Count, "Global chat must ride the service bus.");
            Assert.AreEqual("global", _bus.Sent[0].ch);
            Assert.AreEqual("hello", _bus.Sent[0].body);
        }

        [Test]
        public void Send_Zone_DeliversLocallyWithoutTouchingTheBus()
        {
            LocalPlayerHandle handle = AddLocal("a", "alice");
            string received = null;
            handle.DeliverChat = json => received = json;

            ChatRule rule = _chat.Send("a", ChatRules.ChannelZone, "hi zone", 100);

            Assert.AreEqual(ChatRule.Ok, rule);
            Assert.IsEmpty(_bus.Sent, "Zone chat never leaves the zone.");
            Assert.IsNotNull(received, "Every local player receives zone chat.");
        }

        [Test]
        public void Send_PartyWithoutAParty_IsRefused()
            => Assert.AreEqual(ChatRule.NoParty, _chat.Send("a", ChatRules.ChannelParty, "hi", 100));

        [Test]
        public void Send_InsideTheRateWindow_IsRateLimited()
        {
            _chat.Send("a", ChatRules.ChannelGlobal, "one", 100);

            Assert.AreEqual(ChatRule.RateLimited, _chat.Send("a", ChatRules.ChannelGlobal, "two", 100));
        }

        [Test]
        public void RouteInbound_Global_ReachesEveryLocalPlayer()
        {
            LocalPlayerHandle a = AddLocal("a", "alice");
            LocalPlayerHandle b = AddLocal("b", "bob");
            int delivered = 0;
            a.DeliverChat = _ => delivered++;
            b.DeliverChat = _ => delivered++;

            _chat.RouteInbound(new SocialBusEnvelope { t = "chat", ch = "global", from = "c", name = "cara", body = "hi" });

            Assert.AreEqual(2, delivered);
        }

        [Test]
        public void RouteInbound_Party_ReachesOnlyMembersOfThatParty()
        {
            LocalPlayerHandle a = AddLocal("a", "alice", "p1");
            LocalPlayerHandle b = AddLocal("b", "bob", "p2");
            int delivered = 0;
            a.DeliverChat = _ => delivered++;
            b.DeliverChat = _ => delivered++;

            _chat.RouteInbound(new SocialBusEnvelope { t = "chat", ch = "party", target = "p1", from = "c" });

            Assert.AreEqual(1, delivered, "Only the member of p1 should receive the party line.");
        }

        [Test]
        public void RouteInbound_Zone_ReachesOnlyThisZone()
        {
            LocalPlayerHandle a = AddLocal("a", "alice");
            int delivered = 0;
            a.DeliverChat = _ => delivered++;

            _chat.RouteInbound(new SocialBusEnvelope { t = "chat", ch = "zone", target = "other", from = "c" });
            Assert.AreEqual(0, delivered);

            _chat.RouteInbound(new SocialBusEnvelope { t = "chat", ch = "zone", target = _roster.LocalZoneId, from = "c" });
            Assert.AreEqual(1, delivered);
        }

        private sealed class FakeBus : ISocialBus
        {
            public readonly List<SocialBusEnvelope> Sent = new List<SocialBusEnvelope>();

            public bool IsConnected => true;

            public event Action<SocialBusEnvelope> EnvelopeReceived;

            public bool TrySend(SocialBusEnvelope envelope)
            {
                Sent.Add(envelope);
                return true;
            }

            public void Push(SocialBusEnvelope envelope) => EnvelopeReceived?.Invoke(envelope);
        }

        private sealed class FakeRoster : ILocalRoster
        {
            public readonly List<LocalPlayerHandle> Handles = new List<LocalPlayerHandle>();
            public readonly Dictionary<string, string> Parties = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

            public string LocalZoneId => "z1";

            public IReadOnlyList<LocalPlayerHandle> Local => Handles;

            public string PartyOf(string accountId)
                => Parties.TryGetValue(accountId ?? string.Empty, out string party) ? party : null;

            public string NameOf(string accountId)
                => Names.TryGetValue(accountId ?? string.Empty, out string name) ? name : accountId;
        }
    }
}
