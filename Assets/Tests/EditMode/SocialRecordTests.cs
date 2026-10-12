using System;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The wire shapes the social system persists have to round-trip through <c>JsonUtility</c>
    /// unchanged — a dropped member or a lost leader would silently corrupt a roster on the next
    /// reconciliation. Presence freshness is pinned here too because it is the one piece of logic
    /// that lives on the record rather than in the rules.
    /// </summary>
    public sealed class SocialRecordTests
    {
        [Test]
        public void PartyRecord_RoundTripsMembersAndRaidHook()
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = "a", RaidInstanceId = "raid-1" };
            party.Members.Add(new PartyMemberRecord { AccountId = "a", PlayerName = "Alice", ZoneId = "z1", JoinedAtTicks = 11 });
            party.Members.Add(new PartyMemberRecord { AccountId = "b", PlayerName = "Bob", ZoneId = "z2", JoinedAtTicks = 22 });

            PartyRecord copy = JsonUtility.FromJson<PartyRecord>(JsonUtility.ToJson(party));

            Assert.AreEqual("p1", copy.PartyId);
            Assert.AreEqual("a", copy.LeaderId);
            Assert.AreEqual("raid-1", copy.RaidInstanceId);
            Assert.AreEqual(2, copy.Members.Count);
            Assert.AreEqual("Bob", copy.Members[1].PlayerName);
            Assert.AreEqual(22, copy.Members[1].JoinedAtTicks);
        }

        [Test]
        public void SocialIndexRecord_RoundTripsBothPendingInvites()
        {
            var index = new SocialIndexRecord
            {
                AccountId = "a",
                PartyId = "p1",
                GuildId = "g1",
                PendingPartyInvite = new PendingPartyInvite
                {
                    FromAccountId = "b",
                    FromPlayerName = "Bob",
                    PartyId = "p1",
                    ExpiresAtTicks = 123,
                },
                PendingGuildInvite = new PendingGuildInvite
                {
                    FromAccountId = "c",
                    FromPlayerName = "Cara",
                    GuildId = "g1",
                    ExpiresAtTicks = 456,
                },
            };

            SocialIndexRecord copy = JsonUtility.FromJson<SocialIndexRecord>(JsonUtility.ToJson(index));

            Assert.AreEqual("p1", copy.PartyId);
            Assert.AreEqual("g1", copy.GuildId);
            Assert.AreEqual("Bob", copy.PendingPartyInvite.FromPlayerName);
            Assert.AreEqual(123, copy.PendingPartyInvite.ExpiresAtTicks);
            Assert.AreEqual("Cara", copy.PendingGuildInvite.FromPlayerName);
            Assert.AreEqual(456, copy.PendingGuildInvite.ExpiresAtTicks);
        }

        [Test]
        public void GuildRecord_RoundTripsRanksAndMembers()
        {
            var guild = new GuildRecord { GuildId = "g1", Name = "Raiders", LeaderId = "a" };
            guild.Members.Add(new GuildMemberRecord { AccountId = "a", PlayerName = "Alice", Rank = (int)GuildRank.Leader, JoinedAtTicks = 1 });
            guild.Members.Add(new GuildMemberRecord { AccountId = "b", PlayerName = "Bob", Rank = (int)GuildRank.Officer, JoinedAtTicks = 2 });

            GuildRecord copy = JsonUtility.FromJson<GuildRecord>(JsonUtility.ToJson(guild));

            Assert.AreEqual("Raiders", copy.Name);
            Assert.AreEqual(2, copy.Members.Count);
            Assert.AreEqual(GuildRank.Officer, GuildRules.RankOf(copy.Members[1]));
        }

        [Test]
        public void PresenceRecord_FreshHeartbeat_IsOnline()
        {
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            PresenceRecord record = PresenceRecord.Online("a", "Alice", "z1", now);

            Assert.IsTrue(record.IsHeartbeatFresh(now));
            Assert.IsTrue(record.IsHeartbeatFresh(now.AddSeconds(89)));
        }

        [Test]
        public void PresenceRecord_StaleHeartbeat_ReadsOffline()
        {
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            PresenceRecord record = PresenceRecord.Online("a", "Alice", "z1", now);

            Assert.IsFalse(record.IsHeartbeatFresh(now.AddSeconds(91)),
                "A crashed zone must let its players go stale after the heartbeat window.");
        }

        [Test]
        public void PresenceRecord_ExplicitlyOffline_IsNeverFresh()
        {
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var record = new PresenceRecord { AccountId = "a", Online = false, HeartbeatAtTicks = now.UtcDateTime.Ticks };

            Assert.IsFalse(record.IsHeartbeatFresh(now));
        }

        [Test]
        public void PresenceRecord_RoundTrips()
        {
            var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            PresenceRecord record = PresenceRecord.Online("a", "Alice", "z1", now);

            PresenceRecord copy = JsonUtility.FromJson<PresenceRecord>(JsonUtility.ToJson(record));

            Assert.AreEqual("Alice", copy.PlayerName);
            Assert.AreEqual("z1", copy.ZoneId);
            Assert.IsTrue(copy.IsHeartbeatFresh(now));
        }
    }
}
