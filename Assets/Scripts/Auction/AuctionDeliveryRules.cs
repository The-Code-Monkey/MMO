using MMO.Social;

namespace MMO.Auction
{
    /// <summary>Where a purchased item can be handed to its buyer.</summary>
    public enum AuctionDeliveryTarget
    {
        /// <summary>Straight into the buyer's bag on this zone.</summary>
        Bag,

        /// <summary>Into a claimable mail, because the buyer is elsewhere or has no bag room.</summary>
        Mail,

        /// <summary>Neither the bag nor the inbox can take it; refuse before the item moves.</summary>
        Reject,
    }

    /// <summary>
    /// Pure delivery decision for an auction hand-off. The item has exactly one holder: it goes to
    /// the bag, else to the mailbox, and when neither has room the transfer is refused before any
    /// gold or item leaves its owner.
    /// </summary>
    public static class AuctionDeliveryRules
    {
        public static AuctionDeliveryTarget Choose(bool buyerOnThisZone, bool hasBagSpace, int inboxCount)
        {
            if (buyerOnThisZone && hasBagSpace)
                return AuctionDeliveryTarget.Bag;

            return MailRules.CanSend(inboxCount)
                ? AuctionDeliveryTarget.Mail
                : AuctionDeliveryTarget.Reject;
        }
    }
}
