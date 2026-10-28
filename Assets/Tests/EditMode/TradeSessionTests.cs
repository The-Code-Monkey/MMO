using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    public sealed class TradeSessionTests
    {
        private const string A = "account-a";
        private const string B = "account-b";

        private static ItemInstance Item(string id, int x = 0, int y = 0)
            => new()
            {
                ItemName = "Item " + id,
                InstanceId = id,
                GridX = x,
                GridY = y,
                Width = 1,
                Height = 1,
            };

        private static TradeSession BothLocked()
        {
            var session = new TradeSession(A, B);
            Assert.AreEqual(TradeRule.Ok, session.SetOffer(A, new[] { Item("a1") }));
            Assert.AreEqual(TradeRule.Ok, session.SetOffer(B, new[] { Item("b1") }));
            Assert.AreEqual(TradeRule.Ok, session.Lock(A));
            Assert.AreEqual(TradeRule.Ok, session.Lock(B));
            return session;
        }

        [Test]
        public void TradeSession_BothLocked_ProducesExchangeExactlyOnce()
        {
            TradeSession session = BothLocked();

            Assert.AreEqual(TradeState.BothLocked, session.State);
            Assert.AreEqual(TradeRule.Ok, session.TryBuildExchange(out TradeExchange exchange));
            Assert.AreEqual("b1", exchange.ToA[0].InstanceId);
            Assert.AreEqual("a1", exchange.ToB[0].InstanceId);

            session.Complete();

            Assert.AreEqual(TradeRule.AlreadyFinished, session.TryBuildExchange(out _),
                "A completed session must never produce a second exchange.");
        }

        [Test]
        public void TradeSession_SetOfferAfterLock_ClearsBothLocks()
        {
            var session = new TradeSession(A, B);
            session.SetOffer(A, new[] { Item("a1") });
            session.SetOffer(B, new[] { Item("b1") });
            session.Lock(A);

            Assert.AreEqual(TradeState.OneLocked, session.State);
            Assert.AreEqual(TradeRule.Ok, session.SetOffer(B, new[] { Item("b2") }));

            Assert.AreEqual(TradeState.Open, session.State, "Changing either offer invalidates both locks.");
            Assert.IsFalse(session.IsLocked(A));
        }

        [Test]
        public void TradeSession_DuplicateInstanceId_RefusesExchange()
        {
            var session = new TradeSession(A, B);
            session.SetOffer(A, new[] { Item("same") });
            session.SetOffer(B, new[] { Item("same") });
            session.Lock(A);
            session.Lock(B);

            Assert.AreEqual(TradeRule.DuplicateInstanceId, session.TryBuildExchange(out _));
        }

        [Test]
        public void TradeSession_Cancel_ProducesNoExchange()
        {
            TradeSession session = BothLocked();

            session.Cancel();

            Assert.AreEqual(TradeState.Cancelled, session.State);
            Assert.AreEqual(TradeRule.AlreadyFinished, session.TryBuildExchange(out _));
        }

        [Test]
        public void TradeSession_OtherAccountCannotLock()
        {
            var session = new TradeSession(A, B);
            session.SetOffer(A, new[] { Item("a1") });

            Assert.AreEqual(TradeRule.NotYourTrade, session.Lock("intruder"));
            Assert.AreEqual(TradeState.Open, session.State);
        }

        [Test]
        public void TradeSession_LockRefusesAnEmptyOffer()
        {
            var session = new TradeSession(A, B);

            Assert.AreEqual(TradeRule.NoOffer, session.Lock(A));
        }

        [Test]
        public void TradeRules_OffersStillHeld_FalseWhenItemLeftBag()
        {
            ItemInstance offered = Item("a1");
            var bag = new List<ItemInstance> { offered };

            Assert.IsTrue(TradeRules.OffersStillHeld(new[] { offered }, bag));

            bag.Clear();
            Assert.IsFalse(TradeRules.OffersStillHeld(new[] { offered }, bag));
        }

        [Test]
        public void TradeOfferSource_GetItemAt_UsesReplicatedBagCell()
        {
            var source = new TradeOfferSource();
            source.SetOffer(new[] { Item("a1", 2, 3) });

            Assert.AreEqual("a1", source.GetItemAt(2, 3).InstanceId);
            Assert.IsTrue(source.GetItemAt(0, 0).IsEmpty);
        }
    }
}
