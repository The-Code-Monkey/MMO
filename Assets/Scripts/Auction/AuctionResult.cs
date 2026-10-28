using System;

namespace MMO.Auction
{
    /// <summary>Result of an auction operation.</summary>
    [Serializable]
    public struct AuctionResult
    {
        public AuctionRule Rule;
        public string ListingId;
        public string BuyOrderId;
        public AuctionListingRecord Listing;
        public AuctionBuyOrderRecord BuyOrder;

        public AuctionResult(AuctionRule rule, string listingId = null, string buyOrderId = null, AuctionListingRecord listing = null, AuctionBuyOrderRecord buyOrder = null)
        {
            Rule = rule;
            ListingId = listingId;
            BuyOrderId = buyOrderId;
            Listing = listing;
            BuyOrder = buyOrder;
        }

        public static AuctionResult Ok(string listingId = null, string buyOrderId = null, AuctionListingRecord listing = null, AuctionBuyOrderRecord buyOrder = null)
            => new AuctionResult(AuctionRule.Ok, listingId, buyOrderId, listing, buyOrder);

        public static AuctionResult Fail(AuctionRule rule)
            => new AuctionResult(rule);
    }
}
