using System;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Every party decision, isolated. The rules are pure and NetworkBehaviour-free precisely so an
    /// EditMode test can drive each branch — the same functions the server runs before a backend
    /// write (AGENTS.md: server validation gets isolated unit tests).
    /// </summary>
    public sealed class PartyRulesTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static SocialIndexRecord Index(string accountId, string partyId = null)
            => new SocialIndexRecord { AccountId = accountId, PartyId = partyId };

        private static PartyRecord Party(string leaderId, params string[] members)
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = leaderId };
            long tick = 1;
            foreach (string member in members)
                party.Members.Add(new PartyMemberRecord { AccountId = member, JoinedAtTicks = tick++ });

            return party;
        }

        [Test]
        public void CanInvite_Self_IsRefused()
        {
            PartyRule result = PartyRules.CanInvite("a", null, Index("a"), targetOnline: true);

            Assert.AreEqual(PartyRule.SelfInvite, result);
        }

        [Test]
        public void CanInvite_OfflineTarget_IsRefused()
        {
            PartyRule result = PartyRules.CanInvite("a", null, Index("b"), targetOnline: false);

            Assert.AreEqual(PartyRule.TargetOffline, result);
        }

        [Test]
        public void CanInvite_TargetAlreadyInParty_IsRefused()
        {
            PartyRule result = PartyRules.CanInvite("a", null, Index("b", "other"), targetOnline: true);

            Assert.AreEqual(PartyRule.TargetAlreadyInParty, result);
        }

        [Test]
        public void CanInvite_FullParty_IsRefused()
        {
            PartyRecord party = Party("a", "a", "b", "c", "d", "e");

            PartyRule result = PartyRules.CanInvite("a", party, Index("f"), targetOnline: true);

            Assert.AreEqual(PartyRule.PartyFull, result);
        }

        [Test]
        public void CanInvite_InviterNotActuallyAMember_IsRefused()
        {
            PartyRecord party = Party("b", "b");

            PartyRule result = PartyRules.CanInvite("a", party, Index("f"), targetOnline: true);

            Assert.AreEqual(PartyRule.NotInParty, result);
        }

        [Test]
        public void CanInvite_FreeTarget_IsAllowed()
        {
            PartyRule result = PartyRules.CanInvite("a", null, Index("b"), targetOnline: true);

            Assert.AreEqual(PartyRule.Ok, result);
        }

        [Test]
        public void CanAccept_NoInvite_ReadsAsNoParty()
        {
            PartyRule result = PartyRules.CanAccept(Party("a", "a"), Index("b"), Now);

            Assert.AreEqual(PartyRule.NoParty, result);
        }

        [Test]
        public void CanAccept_ExpiredInvite_IsRefused()
        {
            var index = Index("b");
            index.PendingPartyInvite = new PendingPartyInvite { PartyId = "p1", ExpiresAtTicks = Now.UtcDateTime.Ticks - 1 };

            PartyRule result = PartyRules.CanAccept(Party("a", "a"), index, Now);

            Assert.AreEqual(PartyRule.InviteExpired, result);
        }

        [Test]
        public void CanAccept_FullParty_IsRefused()
        {
            var index = Index("f");
            index.PendingPartyInvite = new PendingPartyInvite
            {
                PartyId = "p1",
                ExpiresAtTicks = Now.AddMinutes(1).UtcDateTime.Ticks,
            };

            PartyRule result = PartyRules.CanAccept(Party("a", "a", "b", "c", "d", "e"), index, Now);

            Assert.AreEqual(PartyRule.PartyFull, result);
        }

        [Test]
        public void CanAccept_LiveInviteAndRoom_IsAllowed()
        {
            var index = Index("f");
            index.PendingPartyInvite = new PendingPartyInvite
            {
                PartyId = "p1",
                ExpiresAtTicks = Now.AddMinutes(1).UtcDateTime.Ticks,
            };

            PartyRule result = PartyRules.CanAccept(Party("a", "a"), index, Now);

            Assert.AreEqual(PartyRule.Ok, result);
        }

        [Test]
        public void Remove_LastMemberLeaving_DisbandsTheParty()
        {
            PartyRecord party = Party("a", "a");

            Assert.IsNull(PartyRules.Remove(party, "a"), "The last member leaving must disband the party.");
        }

        [Test]
        public void Remove_LeaderLeaving_PromotesTheEarliestRemainingMember()
        {
            var party = new PartyRecord { PartyId = "p", LeaderId = "a" };
            party.Members.Add(new PartyMemberRecord { AccountId = "a", JoinedAtTicks = 1 });
            party.Members.Add(new PartyMemberRecord { AccountId = "b", JoinedAtTicks = 3 });
            party.Members.Add(new PartyMemberRecord { AccountId = "c", JoinedAtTicks = 2 });

            PartyRecord remaining = PartyRules.Remove(party, "a");

            Assert.AreEqual("c", remaining.LeaderId, "The earliest-joined remaining member takes over.");
        }

        [Test]
        public void Remove_NonMember_IsANoOp()
        {
            PartyRecord party = Party("a", "a", "b");

            PartyRecord remaining = PartyRules.Remove(party, "z");

            Assert.AreSame(party, remaining);
            Assert.AreEqual(2, remaining.Members.Count);
        }

        [Test]
        public void CanKick_NonLeader_IsRefused()
        {
            PartyRule result = PartyRules.CanKick(Party("a", "a", "b"), "b", "a");

            Assert.AreEqual(PartyRule.NotLeader, result);
        }

        [Test]
        public void CanKick_LeaderKicksMember_IsAllowed()
        {
            PartyRule result = PartyRules.CanKick(Party("a", "a", "b"), "a", "b");

            Assert.AreEqual(PartyRule.Ok, result);
        }

        [Test]
        public void CanKick_LeaderKickingSelf_IsRefused()
        {
            PartyRule result = PartyRules.CanKick(Party("a", "a", "b"), "a", "a");

            Assert.AreEqual(PartyRule.NotInParty, result);
        }

        [Test]
        public void FindMember_ReportsAbsenceAsADefaultRecord()
        {
            Assert.IsNull(PartyRules.FindMember(Party("a", "a"), "z").AccountId);
            Assert.AreEqual("a", PartyRules.FindMember(Party("a", "a"), "a").AccountId);
        }
    }
}
