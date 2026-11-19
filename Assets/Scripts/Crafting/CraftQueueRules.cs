using System;
using System.Collections.Generic;

namespace MMO.Crafting
{
    /// <summary>
    /// The queue's whole decision surface, kept pure so EditMode tests can drive it with an
    /// explicit clock: the five-slot cap, the UTC deadline, the only transition out of
    /// <see cref="CraftOrderState.InProgress"/>, and which states a claim can resolve.
    ///
    /// <para>
    /// Time comes in as <see cref="DateTime"/> and deadlines are absolute UTC ticks. Nothing here
    /// reads the wall clock, so a test can move an order from due to not-due without waiting.
    /// </para>
    /// </summary>
    public static class CraftQueueRules
    {
        /// <summary>Every order holds one of these, whatever its state, until it is pruned.</summary>
        public const int MaxOrders = 5;

        /// <summary>Why a sixth order was refused, and how to free a slot.</summary>
        public const string QueueFullMessage =
            "Queue is full: claim or cancel an order to free a slot.";

        /// <summary>The absolute UTC deadline for an order started at <paramref name="nowUtc"/>.</summary>
        public static long DueAtUtcTicks(DateTime nowUtc, float durationSeconds)
            => nowUtc.AddSeconds(Math.Max(0d, durationSeconds)).Ticks;

        /// <summary>
        /// Seconds left, from the server's clock, never negative. A non-running order reports zero:
        /// a completed order is not counting down towards anything.
        /// </summary>
        public static double RemainingSeconds(CraftOrder order, DateTime nowUtc)
        {
            if (order == null || order.State != CraftOrderState.InProgress)
                return 0d;

            long remainingTicks = order.DueUtcTicks - nowUtc.Ticks;
            return remainingTicks <= 0 ? 0d : TimeSpan.FromTicks(remainingTicks).TotalSeconds;
        }

        /// <summary>True when another order can be accepted; claimed orders are pruned, not counted.</summary>
        public static bool HasFreeSlot(IReadOnlyList<CraftOrder> orders)
            => orders == null || orders.Count < MaxOrders;

        public static CraftOrder Find(IReadOnlyList<CraftOrder> orders, string orderId)
        {
            if (orders == null || string.IsNullOrEmpty(orderId))
                return null;

            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i] != null && string.Equals(orders[i].OrderId, orderId, StringComparison.Ordinal))
                    return orders[i];
            }

            return null;
        }

        /// <summary>
        /// InProgress -> Completed, and only when the due tick has been reached. Every completion
        /// goes through here, so a second caller in the same tick sees a non-InProgress order and
        /// refuses rather than transitioning twice.
        /// </summary>
        public static bool TryComplete(CraftOrder order, DateTime nowUtc)
        {
            if (order == null || order.State != CraftOrderState.InProgress)
                return false;

            if (nowUtc.Ticks < order.DueUtcTicks)
                return false;

            order.State = CraftOrderState.Completed;
            return true;
        }

        /// <summary>
        /// InProgress -> Cancelled, and only before the due tick. Completion and cancellation share
        /// the "only InProgress can move" rule, so a cancel racing a completion in the same tick
        /// wins or loses as a whole — never both a refund and a grant.
        /// </summary>
        public static bool TryCancel(CraftOrder order, DateTime nowUtc)
        {
            if (order == null || order.State != CraftOrderState.InProgress)
                return false;

            if (nowUtc.Ticks >= order.DueUtcTicks)
                return false;

            order.State = CraftOrderState.Cancelled;
            return true;
        }

        /// <summary>
        /// True when a reload found a Completed order: the grant may have been interrupted, so the
        /// server tick finishes it without waiting for another deadline.
        /// </summary>
        public static bool NeedsResume(CraftOrder order)
            => order != null && order.State == CraftOrderState.Completed;

        /// <summary>True when a claim can resolve the order: grant its output or refund its escrow.</summary>
        public static bool IsClaimable(CraftOrder order)
            => order != null &&
               (order.State == CraftOrderState.Completed || order.State == CraftOrderState.Cancelled);

        /// <summary>Marks a resolved order so a reload prunes it instead of granting again.</summary>
        public static void MarkClaimed(CraftOrder order)
        {
            if (order != null)
                order.State = CraftOrderState.Claimed;
        }

        /// <summary>
        /// Load-time content drift rule: any surviving order whose recipe — or any escrowed
        /// ingredient — no longer resolves becomes <see cref="CraftOrderState.Cancelled"/> with its
        /// escrow untouched, so the normal claim path can return the materials. A null resolver
        /// counts as "cannot resolve". Returns how many orders were cancelled.
        /// </summary>
        public static int CancelOnContentDrift(
            List<CraftOrder> orders,
            Func<string, bool> recipeResolves,
            Func<string, bool> itemResolves)
        {
            if (orders == null)
                return 0;

            int cancelled = 0;
            for (int i = 0; i < orders.Count; i++)
            {
                CraftOrder order = orders[i];
                if (order == null || order.State == CraftOrderState.Claimed)
                    continue;

                bool unresolved =
                    string.IsNullOrEmpty(order.RecipeName) ||
                    recipeResolves == null ||
                    !recipeResolves(order.RecipeName);

                if (!unresolved && order.Escrow != null)
                {
                    for (int e = 0; e < order.Escrow.Count; e++)
                    {
                        CraftEscrowItem item = order.Escrow[e];
                        if (item.IsEmpty)
                            continue;

                        if (itemResolves == null || !itemResolves(item.ItemName))
                        {
                            unresolved = true;
                            break;
                        }
                    }
                }

                if (!unresolved)
                    continue;

                order.State = CraftOrderState.Cancelled;
                cancelled++;
            }

            return cancelled;
        }

        /// <summary>Removes null and claimed entries, freeing their queue slots. Returns how many went.</summary>
        public static int PruneClaimed(List<CraftOrder> orders)
        {
            if (orders == null)
                return 0;

            return orders.RemoveAll(order => order == null || order.State == CraftOrderState.Claimed);
        }
    }
}
