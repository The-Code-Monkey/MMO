using System;

namespace MMO.Auction
{
    /// <summary>A "want to buy" order, stored under "auction_buy_orders".</summary>
    [Serializable]
    public sealed class AuctionBuyOrderRecord
    {
        public const string Collection = "auction_buy_orders";

        public string BuyOrderId;
        public string BuyerAccountId;
        public string BuyerPlayerName;
        public string ItemName;
        public int ItemWidth;
        public int ItemHeight;
        public int QuantityRemaining;
        public int QuantityTotal;
        public long PricePerUnit;
        public long TotalValue => AuctionPricing.TotalValue(PricePerUnit, QuantityTotal);
        public long ListedAtTicks;
        public long ExpiresAtTicks;
        public AuctionBuyOrderStatus Status;
        public string StorageVersion;
    }
}
