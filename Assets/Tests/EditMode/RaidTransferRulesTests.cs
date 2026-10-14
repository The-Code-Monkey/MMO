using System;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Pure rules for the raid return transfer decision. EditMode-tested per agents.md.
    /// </summary>
    public sealed class RaidTransferRulesTests
    {
        private static RaidReturnAnchor Anchor(string accountId, bool returned = false)
            => new RaidReturnAnchor
            {
                AccountId = accountId,
                OriginHost = "10.0.0.1",
                OriginPort = 7770,
                OriginServerId = "server-a",
                Returned = returned
            };

        [Test]
        public void CanTransferBack_Ok_WhenAllConditionsMet()
        {
            var anchor = Anchor("a");
            var rule = RaidTransferRules.CanTransferBack("a", "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.Ok, rule);
        }

        [Test]
        public void CanTransferBack_NoMember_WhenAccountIdIsNull()
        {
            var anchor = Anchor("a");
            var rule = RaidTransferRules.CanTransferBack(null, "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.NoMember, rule);
        }

        [Test]
        public void CanTransferBack_NoMember_WhenAccountIdIsEmpty()
        {
            var anchor = Anchor("a");
            var rule = RaidTransferRules.CanTransferBack("", "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.NoMember, rule);
        }

        [Test]
        public void CanTransferBack_MemberAlreadyLeft_WhenNotLocal()
        {
            var anchor = Anchor("a");
            var rule = RaidTransferRules.CanTransferBack("a", "server-a", anchor, memberStillLocal: false);

            Assert.AreEqual(RaidTransferRule.MemberAlreadyLeft, rule);
        }

        [Test]
        public void CanTransferBack_NoAnchor_WhenAnchorAccountIdIsNull()
        {
            var anchor = default(RaidReturnAnchor);
            var rule = RaidTransferRules.CanTransferBack("a", "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.NoAnchor, rule);
        }

        [Test]
        public void CanTransferBack_NoAnchor_WhenAnchorAccountIdIsEmpty()
        {
            var anchor = new RaidReturnAnchor { AccountId = "" };
            var rule = RaidTransferRules.CanTransferBack("a", "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.NoAnchor, rule);
        }

        [Test]
        public void CanTransferBack_AlreadyReturned_WhenAnchorReturnedIsTrue()
        {
            var anchor = Anchor("a", returned: true);
            var rule = RaidTransferRules.CanTransferBack("a", "server-a", anchor, memberStillLocal: true);

            Assert.AreEqual(RaidTransferRule.AlreadyReturned, rule);
        }
    }
}