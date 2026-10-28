namespace MMO.Auction
{
    /// <summary>Money math for auction records, kept pure so overflow and rounding have one owner.</summary>
    public static class AuctionPricing
    {
        public static long TotalPrice(long pricePerUnit, int stackCount)
            => pricePerUnit * stackCount;

        public static long TotalValue(long pricePerUnit, int quantity)
            => pricePerUnit * quantity;
    }
}
