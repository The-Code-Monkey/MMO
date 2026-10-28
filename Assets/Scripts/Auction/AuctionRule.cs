namespace MMO.Auction
{
    public enum AuctionRule
    {
        Ok = 0,
        BackendUnavailable = 1,
        NotFound = 2,
        NotOwner = 3,
        InvalidPrice = 4,
        ItemNotFound = 5,
        InsufficientFunds = 6,
        ListingNotActive = 7,
        AlreadySold = 8,
        MaxListingsReached = 9,
        Conflict = 10,
        InvalidStackCount = 11,
        BuyOrderNotActive = 12,
        MaxBuyOrdersReached = 13,
    }
}
