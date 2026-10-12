using System;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>The guild rules, isolated — ranks, invites, succession and the validation branches.</summary>
    public sealed class GuildRulesTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static SocialIndexRecord Index(string accountId, string guildId = null)
            => new SocialIndexRecord { AccountId = accountId, GuildId = guildId };

        private static GuildRecord Guild(string leaderId, params (string id, GuildRank rank)[] members)
        {
            var guild = new GuildRecord { GuildId = "g1", Name = "Test", LeaderId = leaderId };
            long tick = 1;
            foreach ((string id, GuildRank rank) member in members)
            {
                guild.Members.Add(new GuildMemberRecord
                {
                    AccountId = member.id,
                    Rank = (int)member.rank,
                    JoinedAtTicks = tick++,
                });
            }

            return guild;
        }

        [Test]
        public void IsValidName_RejectsBlankAndOverlongNames()
        {
            Assert.IsFalse(GuildRules.IsValidName(null));
            Assert.IsFalse(GuildRules.IsValidName("   "));
            Assert.IsFalse(GuildRules.IsValidName(new string('x', GuildRules.MaxNameLength + 1)));
            Assert.IsTrue(GuildRules.IsValidName("Raiders"));
        }

        [Test]
        public void CanCreate_WhenAlreadyInAGuild_IsRefused()
        {
            GuildRule result = GuildRules.CanCreate("a", "Raiders", Index("a", "g1"));

            Assert.AreEqual(GuildRule.AlreadyInGuild, result);
        }

        [Test]
        public void CanCreate_WithABadName_IsRefused()
        {
            Assert.AreEqual(GuildRule.InvalidName, GuildRules.CanCreate("a", " ", Index("a")));
        }

        [Test]
        public void CanCreate_FreePlayerWithAGoodName_IsAllowed()
        {
            Assert.AreEqual(GuildRule.Ok, GuildRules.CanCreate("a", "Raiders", Index("a")));
        }

        [Test]
        public void CanInvite_MemberRank_IsRefused()
        {
            GuildRule result = GuildRules.CanInvite(
                "b", Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Member)), Index("c"), targetOnline: true);

            Assert.AreEqual(GuildRule.NotOfficer, result);
        }

        [Test]
        public void CanInvite_OfficerInvitesFreeTarget_IsAllowed()
        {
            GuildRule result = GuildRules.CanInvite(
                "b", Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Officer)), Index("c"), targetOnline: true);

            Assert.AreEqual(GuildRule.Ok, result);
        }

        [Test]
        public void CanInvite_TargetAlreadyInAGuild_IsRefused()
        {
            GuildRule result = GuildRules.CanInvite(
                "a", Guild("a", ("a", GuildRank.Leader)), Index("b", "g2"), targetOnline: true);

            Assert.AreEqual(GuildRule.TargetAlreadyInGuild, result);
        }

        [Test]
        public void CanInvite_OfflineTarget_IsRefused()
        {
            GuildRule result = GuildRules.CanInvite(
                "a", Guild("a", ("a", GuildRank.Leader)), Index("b"), targetOnline: false);

            Assert.AreEqual(GuildRule.TargetOffline, result);
        }

        [Test]
        public void CanAccept_NoPendingInvite_IsRefused()
        {
            Assert.AreEqual(GuildRule.NoPendingInvite, GuildRules.CanAccept(Guild("a", ("a", GuildRank.Leader)), Index("b"), Now));
        }

        [Test]
        public void CanAccept_LiveInvite_IsAllowed()
        {
            var index = Index("b");
            index.PendingGuildInvite = new PendingGuildInvite
            {
                GuildId = "g1",
                ExpiresAtTicks = Now.AddMinutes(1).UtcDateTime.Ticks,
            };

            Assert.AreEqual(GuildRule.Ok, GuildRules.CanAccept(Guild("a", ("a", GuildRank.Leader)), index, Now));
        }

        [Test]
        public void Remove_LastMember_Disbands()
        {
            Assert.IsNull(GuildRules.Remove(Guild("a", ("a", GuildRank.Leader)), "a"));
        }

        [Test]
        public void Remove_LeaderLeaving_PromotesEarliestOfficer()
        {
            GuildRecord guild = Guild(
                "a",
                ("a", GuildRank.Leader),
                ("b", GuildRank.Member),
                ("c", GuildRank.Officer));

            GuildRecord remaining = GuildRules.Remove(guild, "a");

            Assert.AreEqual("c", remaining.LeaderId, "An officer outranks the earliest plain member.");
            Assert.AreEqual(GuildRank.Leader, GuildRules.RankOf(GuildRules.FindMember(remaining, "c")));
        }

        [Test]
        public void Remove_LeaderLeavingWithoutOfficers_PromotesEarliestMember()
        {
            GuildRecord guild = Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Member), ("c", GuildRank.Member));

            GuildRecord remaining = GuildRules.Remove(guild, "a");

            Assert.AreEqual("b", remaining.LeaderId);
        }

        [Test]
        public void CanKick_EqualRank_IsRefused()
        {
            GuildRule result = GuildRules.CanKick(
                Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Officer), ("c", GuildRank.Officer)), "b", "c");

            Assert.AreEqual(GuildRule.NotOfficer, result);
        }

        [Test]
        public void CanKick_LeaderKicksOfficer_IsAllowed()
        {
            GuildRule result = GuildRules.CanKick(
                Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Officer)), "a", "b");

            Assert.AreEqual(GuildRule.Ok, result);
        }

        [Test]
        public void CanPromote_LeaderPromotesMember_IsAllowed()
        {
            GuildRule result = GuildRules.CanPromote(
                Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Member)), "a", "b");

            Assert.AreEqual(GuildRule.Ok, result);
        }

        [Test]
        public void CanPromote_TargetAlreadyOfficer_IsRefused()
        {
            GuildRule result = GuildRules.CanPromote(
                Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Officer)), "a", "b");

            Assert.AreEqual(GuildRule.AlreadyAtRank, result);
        }

        [Test]
        public void CanPromote_NonLeader_IsRefused()
        {
            GuildRule result = GuildRules.CanPromote(
                Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Officer), ("c", GuildRank.Member)), "b", "c");

            Assert.AreEqual(GuildRule.NotLeader, result);
        }

        [Test]
        public void Promote_AdvancesAMemberOneRank()
        {
            GuildRecord guild = Guild("a", ("a", GuildRank.Leader), ("b", GuildRank.Member));

            GuildRules.Promote(guild, "b");

            Assert.AreEqual(GuildRank.Officer, GuildRules.RankOf(GuildRules.FindMember(guild, "b")));
        }
    }
}
