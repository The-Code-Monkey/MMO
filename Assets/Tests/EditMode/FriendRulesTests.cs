using System.Collections.Generic;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>Pure friend rules: the state machine, the guards and the reconcile predicate.</summary>
    public sealed class FriendRulesTests
    {
        private static FriendListRecord Record(params FriendEntry[] entries)
            => new FriendListRecord { Entries = new List<FriendEntry>(entries) };

        private static FriendEntry Entry(string accountId, byte state)
            => new FriendEntry { AccountId = accountId, State = state };

        [Test]
        public void CanRequest_ToSelf_IsRefused()
            => Assert.AreEqual(FriendRule.SelfRequest, FriendRules.CanRequest("a", "a", Record(), Record()));

        [Test]
        public void CanRequest_WhenAlreadyAccepted_IsRefused()
            => Assert.AreEqual(FriendRule.AlreadyFriends,
                FriendRules.CanRequest("a", "b", Record(Entry("b", FriendState.Accepted)), Record()));

        [Test]
        public void CanRequest_WhenAPendingRequestExistsEitherWay_IsRefused()
        {
            Assert.AreEqual(FriendRule.RequestAlreadyPending,
                FriendRules.CanRequest("a", "b", Record(Entry("b", FriendState.PendingOut)), Record()));
            Assert.AreEqual(FriendRule.RequestAlreadyPending,
                FriendRules.CanRequest("a", "b", Record(), Record(Entry("a", FriendState.PendingIn))));
        }

        [Test]
        public void CanRequest_WhenStrangers_IsOk()
            => Assert.AreEqual(FriendRule.Ok, FriendRules.CanRequest("a", "b", Record(), Record()));

        [Test]
        public void ApplyRequest_ThenAccept_ConvergesToAccepted()
        {
            FriendListRecord target = Record();
            FriendListRecord requester = Record();

            FriendRules.ApplyRequest(target, "a", "alice", 1);
            FriendRules.ApplyPendingOut(requester, "b", 1);

            Assert.AreEqual(FriendState.PendingIn, FriendRules.Find(target, "a").State);
            Assert.AreEqual(FriendState.PendingOut, FriendRules.Find(requester, "b").State);

            FriendRules.ApplyAccept(target, "a", 2);
            FriendRules.ApplyAccept(requester, "b", 2);

            Assert.AreEqual(FriendRule.Ok, FriendRules.CanRemove(target, "a"));
            Assert.AreEqual(FriendRule.Ok, FriendRules.CanRemove(requester, "b"));
        }

        [Test]
        public void CanAccept_OnlyFromPendingIn()
        {
            Assert.AreEqual(FriendRule.NotPending, FriendRules.CanAccept(Record(), "a"));
            Assert.AreEqual(FriendRule.Ok,
                FriendRules.CanAccept(Record(Entry("a", FriendState.PendingIn)), "a"));
            Assert.AreEqual(FriendRule.NotPending,
                FriendRules.CanAccept(Record(Entry("a", FriendState.PendingOut)), "a"));
        }

        [Test]
        public void Remove_DropsTheEntry()
        {
            FriendListRecord record = Record(Entry("b", FriendState.Accepted));

            FriendRules.Remove(record, "b");

            Assert.IsNull(FriendRules.Find(record, "b").AccountId);
        }

        [Test]
        public void NeedsRepair_OnlyWhenOneSideAcceptedAndTheOtherDidNot()
        {
            FriendListRecord mine = Record(Entry("b", FriendState.Accepted));
            FriendListRecord theirsAccepted = Record(Entry("a", FriendState.Accepted));
            FriendListRecord theirsMissing = Record();

            Assert.IsFalse(FriendRules.NeedsRepair(mine, theirsAccepted, "a", "b"));
            Assert.IsTrue(FriendRules.NeedsRepair(mine, theirsMissing, "a", "b"));
        }

        [Test]
        public void NeedsRepair_IsFalseWhenThisSideHasNotAccepted()
        {
            FriendListRecord mine = Record(Entry("b", FriendState.PendingOut));

            Assert.IsFalse(FriendRules.NeedsRepair(mine, Record(), "a", "b"));
        }
    }
}
