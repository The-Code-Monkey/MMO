namespace MMO.Auction
{
    /// <summary>Maps auction backend error codes to local rule results.</summary>
    public static class AuctionErrorRules
    {
        public static AuctionRule Map(string errorCode)
        {
            switch (errorCode)
            {
                case "INSUFFICIENT_FUNDS": return AuctionRule.InsufficientFunds;
                case "ALREADY_SOLD": return AuctionRule.AlreadySold;
                case "NOT_ACTIVE": return AuctionRule.ListingNotActive;
                case "BUY_ORDER_NOT_ACTIVE": return AuctionRule.BuyOrderNotActive;
                case "DELIVERY_FAILED": return AuctionRule.Conflict;
                case "NOT_FOUND": return AuctionRule.NotFound;
                default: return AuctionRule.BackendUnavailable;
            }
        }
    }
}
