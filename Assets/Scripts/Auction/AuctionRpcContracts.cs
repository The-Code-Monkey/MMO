using System;

namespace MMO.Auction
{
    /// <summary>Wire payload for the atomic auction-buy RPC.</summary>
    [Serializable]
    internal class BuyListingPayload
    {
        public string buyerAccountId;
        public string listingId;
        public string itemName;
        public string itemInstanceId;
        public int itemWidth;
        public int itemHeight;
        public int itemDurability;
        public string attemptId;
    }

    /// <summary>Wire response for the atomic auction-buy RPC.</summary>
    [Serializable]
    internal class BuyListingResponse
    {
        public bool success;
        public string errorCode;
        public AuctionListingRecord listing;
        public AuctionTransactionRecord transaction;
        public string deliveryMailId;
    }

    /// <summary>Wire payload for the atomic buy-order fulfillment RPC.</summary>
    [Serializable]
    internal class FulfillBuyOrderPayload
    {
        public string sellerAccountId;
        public string buyOrderId;
        public int quantity;
        public string itemName;
        public string itemInstanceId;
        public int itemWidth;
        public int itemHeight;
        public int itemDurability;
        public string attemptId;
    }

    /// <summary>Wire response for the atomic buy-order fulfillment RPC.</summary>
    [Serializable]
    internal class FulfillBuyOrderResponse
    {
        public bool success;
        public string errorCode;
        public AuctionBuyOrderRecord buyOrder;
        public AuctionTransactionRecord transaction;
        public string deliveryMailId;
    }
}
