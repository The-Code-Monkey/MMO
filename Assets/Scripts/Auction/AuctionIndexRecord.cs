using System;
using System.Collections.Generic;

namespace MMO.Auction
{
    /// <summary>Per-player index of active listings, buy orders, and purchases.</summary>
    [Serializable]
    public sealed class AuctionIndexRecord
    {
        public const string Collection = "auction_index";

        public string AccountId;
        public List<string> ActiveListingIds = new List<string>();
        public List<string> ActiveBuyOrderIds = new List<string>();
        public List<string> PurchaseHistoryIds = new List<string>();
        public string StorageVersion;
    }
}
