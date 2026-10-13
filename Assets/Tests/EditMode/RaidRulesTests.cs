using System;
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
    }
}
