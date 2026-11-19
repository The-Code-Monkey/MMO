namespace MMO.Crafting
{
    /// <summary>
    /// Where a timed craft order is in its life. Every state holds a queue slot until the order is
    /// claimed and pruned; only <see cref="InProgress"/> can become anything else, which is what
    /// makes a cancel and a completion racing in the same tick resolve as exactly one outcome.
    /// </summary>
    public enum CraftOrderState
    {
        /// <summary>The escrow has been taken and the order is waiting for its due tick.</summary>
        InProgress = 0,

        /// <summary>Due, but the output has not been granted yet (or did not fit the bag).</summary>
        Completed = 1,

        /// <summary>Resolved and pruned from the list. A reload that finds it does nothing.</summary>
        Claimed = 2,

        /// <summary>Cancelled before it was due; its escrow is claimable, or already refunded.</summary>
        Cancelled = 3,
    }
}
