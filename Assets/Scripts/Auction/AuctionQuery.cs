using System;

namespace MMO.Auction
{
    /// <summary>
    /// A player-authored market query: filters, a sort mode, and a page window. Serializable so a
    /// saved search can be persisted and replayed to reproduce a result set.
    /// </summary>
    [Serializable]
    public sealed class AuctionQuery
    {
        public string Text = string.Empty;
        public string Category = string.Empty;
        public int MinTier;
        public int MaxTier = int.MaxValue;
        public string SellerId = string.Empty;
        public long MinPrice;
        public long MaxPrice = long.MaxValue;
        public AuctionSort SortBy = AuctionSort.Relevance;
        public int Page;
        public int PageSize = 20;
    }
}
