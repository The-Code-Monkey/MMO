namespace MMO.Auction
{
    /// <summary>How the market browse orders its results.</summary>
    public enum AuctionSort
    {
        /// <summary>Newest listing first.</summary>
        Relevance,

        PriceAsc,
        PriceDesc,

        /// <summary>Newest listing first (alias kept for wire stability).</summary>
        Recent,

        /// <summary>Highest tier first.</summary>
        Tier,
    }
}
