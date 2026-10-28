namespace MMO.Auction
{
    /// <summary>Where a purchased item can be handed to its buyer.</summary>
    public enum AuctionDeliveryTarget
    {
        /// <summary>Straight into the buyer's bag on this zone.</summary>
        Bag,

        /// <summary>Into a claimable system mail, because the buyer is elsewhere or has no bag room.</summary>
        Mail,
    }

    /// <summary>
    /// Pure delivery decision for an auction hand-off. The item has exactly one holder: it goes to
    /// the bag when the buyer is here and has room, otherwise to their mailbox. Auction mail is a
    /// system delivery, so it bypasses the inbox cap and can always take the item.
    /// </summary>
    public static class AuctionDeliveryRules
    {
        public static AuctionDeliveryTarget Choose(bool buyerOnThisZone, bool hasBagSpace)
            => buyerOnThisZone && hasBagSpace
                ? AuctionDeliveryTarget.Bag
                : AuctionDeliveryTarget.Mail;
    }
}
