using System;
using System.Collections.Generic;
using MMO.Crafting;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The queue's pure decisions, driven with an explicit clock: the five-slot cap, the UTC
    /// deadline, the single transition out of InProgress, and the claim/prune rules that make a
    /// reload idempotent.
    /// </summary>
    public sealed class CraftQueueRulesTests
    {
        private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void DueAtUtcTicks_AddsTheDurationToTheAbsoluteStart()
        {
            long due = CraftQueueRules.DueAtUtcTicks(Start, 30f);

            Assert.AreEqual(Start.AddSeconds(30).Ticks, due);
        }

        [Test]
        public void RemainingSeconds_CountsDownAndNeverGoesNegative()
        {
            CraftOrder order = OrderAt(Start.AddSeconds(30));

            Assert.AreEqual(30d, CraftQueueRules.RemainingSeconds(order, Start), 0.001d);
            Assert.AreEqual(10d, CraftQueueRules.RemainingSeconds(order, Start.AddSeconds(20)), 0.001d);
            Assert.AreEqual(0d, CraftQueueRules.RemainingSeconds(order, Start.AddSeconds(31)), 0.001d);
        }

        [Test]
        public void RemainingSeconds_IsZeroForAnOrderThatIsNotRunning()
        {
            CraftOrder order = OrderAt(Start.AddSeconds(30));
            CraftQueueRules.TryComplete(order, Start.AddSeconds(30));

            Assert.AreEqual(0d, CraftQueueRules.RemainingSeconds(order, Start));
        }

        [Test]
        public void TryComplete_RefusesBeforeTheDueTick()
        {
            CraftOrder order = OrderAt(Start.AddSeconds(30));

            Assert.IsFalse(CraftQueueRules.TryComplete(order, Start.AddSeconds(29)));
            Assert.AreEqual(CraftOrderState.InProgress, order.State);
        }

        [Test]
        public void TryComplete_TransitionsAtTheDueTickAndOnlyOnce()
        {
            CraftOrder order = OrderAt(Start.AddSeconds(30));

            Assert.IsTrue(CraftQueueRules.TryComplete(order, Start.AddSeconds(30)));
            Assert.AreEqual(CraftOrderState.Completed, order.State);

            Assert.IsFalse(CraftQueueRules.TryComplete(order, Start.AddSeconds(30)),
                "a second caller in the same tick must not transition the order again");
        }

        [Test]
        public void HasFreeSlot_RefusesTheSixthOrderAndCountsEveryState()
        {
            var orders = new List<CraftOrder>();
            Assert.IsTrue(CraftQueueRules.HasFreeSlot(orders));

            for (int i = 0; i < CraftQueueRules.MaxOrders; i++)
                orders.Add(OrderAt(Start.AddSeconds(i + 1)));

            Assert.AreEqual(5, CraftQueueRules.MaxOrders);
            Assert.IsFalse(CraftQueueRules.HasFreeSlot(orders));
        }

        [Test]
        public void IsClaimable_OnlyCompletedAndCancelledOrders()
        {
            CraftOrder order = OrderAt(Start);
            Assert.IsFalse(CraftQueueRules.IsClaimable(order), "an in-progress order is not claimable");

            order.State = CraftOrderState.Completed;
            Assert.IsTrue(CraftQueueRules.IsClaimable(order));

            order.State = CraftOrderState.Cancelled;
            Assert.IsTrue(CraftQueueRules.IsClaimable(order));

            order.State = CraftOrderState.Claimed;
            Assert.IsFalse(CraftQueueRules.IsClaimable(order));
        }

        [Test]
        public void MarkClaimedThenPrune_FreesTheSlot()
        {
            CraftOrder order = OrderAt(Start);
            var orders = new List<CraftOrder> { order };

            CraftQueueRules.MarkClaimed(order);

            Assert.AreEqual(1, CraftQueueRules.PruneClaimed(orders));
            Assert.IsEmpty(orders);
        }

        [Test]
        public void Find_ReturnsTheOrderByItsIdAndNothingElse()
        {
            CraftOrder first = OrderAt(Start);
            CraftOrder second = OrderAt(Start);
            var orders = new List<CraftOrder> { first, second };

            Assert.AreSame(second, CraftQueueRules.Find(orders, second.OrderId));
            Assert.IsNull(CraftQueueRules.Find(orders, "missing"));
            Assert.IsNull(CraftQueueRules.Find(orders, null));
        }

        private static CraftOrder OrderAt(DateTime due)
        {
            CraftOrder order = CraftOrder.Create("Iron Ingot", due.Ticks, null);
            order.State = CraftOrderState.InProgress;
            return order;
        }
    }
}
