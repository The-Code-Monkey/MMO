using System;

namespace MMO.Auction
{
    /// <summary>
    /// Represents a single item listed on the auction house.
    /// The key is a composite of listing ID; stored under "auction_listings" collection.
    /// </summary>
    [Serializable]
    public sealed class AuctionListingRecord
    {
        public const string Collection = "auction_listings";

        public string ListingId;
        public string SellerAccountId;
        public string SellerPlayerName;
        public string ItemName;
        public int ItemWidth;
        public int ItemHeight;
        public int StackCount;
        public long PricePerUnit;
        public long TotalPrice => AuctionPricing.TotalPrice(PricePerUnit, StackCount);
        public long ListedAtTicks;
        public long ExpiresAtTicks;
        public AuctionListingStatus Status;
        public string BuyerAccountId;

        /// <summary>The zone the seller listed from; routes cross-zone delivery to the buyer's inbox.</summary>
        public string SellerZoneId;

        public string StorageVersion;
    }
}
