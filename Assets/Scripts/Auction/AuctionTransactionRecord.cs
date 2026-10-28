using System;

namespace MMO.Auction
{
    /// <summary>A completed auction transaction, stored for history and audit.</summary>
    [Serializable]
    public sealed class AuctionTransactionRecord
    {
        public const string Collection = "auction_transactions";

        public string TransactionId;
        public string ListingId;
        public string BuyOrderId;
        public string SellerAccountId;
        public string BuyerAccountId;
        public string ItemName;
        public int StackCount;
        public long PricePerUnit;
        public long TotalPrice;
        public long CompletedAtTicks;
        public string StorageVersion;
    }
}
