using System;
using System.Collections.Generic;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class RaidRulesTests
    {
        private static PartyRecord Party(string leaderId, int memberCount)
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = leaderId };
            for (int i = 0; i < memberCount; i++)
            {
                party.Members.Add(new PartyMemberRecord { AccountId = memberCount == 1 ? leaderId : ("m" + i), JoinedAtTicks = i });
            }
            return party;
        }

        [Test]
        public void CanAllocate_RequesterNotLeader_ReturnsNotLeader()
        {
            var party = new PartyRecord { PartyId = "p", LeaderId = "a" };
            party.Members.Add(new PartyMemberRecord { AccountId = "a" });

            Assert.AreEqual(RaidRule.NotLeader, RaidRules.CanAllocate(party, "b"));
        }

        [Test]
        public void CanAllocate_PartyBelowMinSize_ReturnsPartyTooSmall()
        {
            var party = Party("a", RaidRules.MinRaidSize - 1);

            Assert.AreEqual(RaidRule.PartyTooSmall, RaidRules.CanAllocate(party, "a"));
        }

        [Test]
        public void CanAllocate_PartyAlreadyHasRaid_ReturnsAlreadyInRaid()
        {
            var party = Party("a", RaidRules.MinRaidSize);
            party.RaidInstanceId = "r1";

            Assert.AreEqual(RaidRule.AlreadyInRaid, RaidRules.CanAllocate(party, "a"));
        }

        [Test]
        public void CanAllocate_MinSizeParty_ReturnsOk()
        {
            var party = Party("a", RaidRules.MinRaidSize);

            Assert.AreEqual(RaidRule.Ok, RaidRules.CanAllocate(party, "a"));
        }

        [Test]
        public void NeedsTransfer_LocalServerIsTheHost_TransfersNobody()
        {
            Assert.IsFalse(RaidRules.NeedsTransfer("r1", "server-a", "server-a"));
            Assert.IsTrue(RaidRules.NeedsTransfer("r1", "server-b", "server-a"));
        }

        #region Multi-party roster (#54)

        private static PartyRecord MemberParty(string partyId, string leaderId, params string[] accounts)
        {
            var party = new PartyRecord { PartyId = partyId, LeaderId = leaderId };
            foreach (string account in accounts)
                party.Members.Add(new PartyMemberRecord { AccountId = account, PlayerName = account });
            return party;
        }

        private static string[] Accounts(string prefix, int count)
        {
            var accounts = new string[count];
            for (int i = 0; i < count; i++)
                accounts[i] = prefix + i;
            return accounts;
        }

        private static RaidInstanceRecord MultiRaid(string seedPartyId, string leaderId, params string[] memberPartyIds)
        {
            var raid = new RaidInstanceRecord
            {
                InstanceId = "r1",
                PartyId = seedPartyId,
                SeedPartyId = seedPartyId,
                LeaderId = leaderId,
            };
            raid.MemberPartyIds.AddRange(memberPartyIds);
            return raid;
        }

        [Test]
        public void Roster_FlattensAndDeduplicatesAcrossMemberParties()
        {
            var p1 = MemberParty("p1", "a", "a", "b");
            var p2 = MemberParty("p2", "c", "c", "d");
            var raid = MultiRaid("p1", "a", "p1", "p2");

            List<string> roster = RaidRules.Roster(raid, new[] { p1, p2 });

            CollectionAssert.AreEquivalent(new[] { "a", "b", "c", "d" }, roster);
        }

        [Test]
        public void CanJoin_AnotherPartyWouldExceedMaxRaidSize_ReturnsRaidFull()
        {
            var p1 = MemberParty("p1", "a0", Accounts("a", 12));
            var p2 = MemberParty("p2", "m0", Accounts("m", 8));
            var joining = MemberParty("p3", "z0", Accounts("z", 6));
            var raid = MultiRaid("p1", "a0", "p1", "p2");

            Assert.AreEqual(RaidRule.RaidFull, RaidRules.CanJoin(raid, joining, new[] { p1, p2 }));
        }

        [Test]
        public void CanJoin_PartyAlreadyPointingAtARaid_ReturnsAlreadyInRaid()
        {
            var joining = MemberParty("p3", "z", "z");
            joining.RaidInstanceId = "other";
            var raid = MultiRaid("p1", "a", "p1");

            Assert.AreEqual(RaidRule.AlreadyInRaid, RaidRules.CanJoin(raid, joining, new[] { joining }));
        }

        [Test]
        public void CanJoin_WithinCapacity_ReturnsOk()
        {
            var p1 = MemberParty("p1", "a", "a", "b");
            var p2 = MemberParty("p2", "c", "c", "d");
            var joining = MemberParty("p3", "e", "e");
            var raid = MultiRaid("p1", "a", "p1", "p2");

            Assert.AreEqual(RaidRule.Ok, RaidRules.CanJoin(raid, joining, new[] { p1, p2 }));
        }

        [Test]
        public void CanPromote_NonLeader_ReturnsNotLeader()
        {
            var p1 = MemberParty("p1", "a", "a");
            var p2 = MemberParty("p2", "b", "b");
            var raid = MultiRaid("p1", "a", "p1", "p2");

            Assert.AreEqual(RaidRule.NotLeader, RaidRules.CanPromote(raid, new[] { p1, p2 }, "b", "a"));
        }

        [Test]
        public void CanPromote_TargetOffTheRoster_ReturnsNotInRaid()
        {
            var p1 = MemberParty("p1", "a", "a");
            var raid = MultiRaid("p1", "a", "p1");

            Assert.AreEqual(RaidRule.NotInRaid, RaidRules.CanPromote(raid, new[] { p1 }, "a", "ghost"));
        }

        [Test]
        public void CanPromote_LeaderPromotingAMember_ReturnsOk()
        {
            var p1 = MemberParty("p1", "a", "a");
            var p2 = MemberParty("p2", "b", "b");
            var raid = MultiRaid("p1", "a", "p1", "p2");

            Assert.AreEqual(RaidRule.Ok, RaidRules.CanPromote(raid, new[] { p1, p2 }, "a", "b"));
        }

        [Test]
        public void RemoveParty_SeedLeaving_KeepsRaidAliveAndReseeds()
        {
            var raid = MultiRaid("p1", "a", "p1", "p2");

            RaidInstanceRecord surviving = RaidRules.RemoveParty(raid, "p1");

            Assert.IsNotNull(surviving);
            Assert.AreEqual("p2", surviving.SeedPartyId);
            CollectionAssert.AreEqual(new[] { "p2" }, surviving.MemberPartyIds);
        }

        [Test]
        public void RemoveParty_LastPartyLeaving_Disbands()
        {
            var raid = MultiRaid("p1", "a", "p1");

            Assert.IsNull(RaidRules.RemoveParty(raid, "p1"));
        }

        [Test]
        public void ResolveLeader_SeedLeft_FallsToTheNewSeedPartysLeader()
        {
            var p1 = MemberParty("p1", "a", "a");
            var p2 = MemberParty("p2", "b", "b");
            var raid = MultiRaid("p1", "a", "p1", "p2");
            RaidRules.RemoveParty(raid, "p1");

            Assert.AreEqual("b", RaidRules.ResolveLeader(raid, new[] { p1, p2 }));
        }

        #endregion
    }
}
