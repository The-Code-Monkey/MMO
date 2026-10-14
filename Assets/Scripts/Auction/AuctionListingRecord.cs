using System;
using System.Collections.Generic;
using UnityEngine;

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
        public long TotalPrice => PricePerUnit * StackCount;
        public long ListedAtTicks;
        public long ExpiresAtTicks;
        public AuctionListingStatus Status;
        public string BuyerAccountId;
        public string StorageVersion;
    }

    /// <summary>
    /// Represents a "want to buy" order - a player wants to buy an item at a specific price.
    /// Stored under "auction_buy_orders" collection.
    /// </summary>
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
        public long TotalValue => PricePerUnit * QuantityTotal;
        public long ListedAtTicks;
        public long ExpiresAtTicks;
        public AuctionBuyOrderStatus Status;
        public string StorageVersion;
    }

    /// <summary>
    /// Tracks a completed auction transaction for history/audit.
    /// </summary>
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

    /// <summary>
    /// Per-player index of their active listings, buy orders, and purchases.
    /// </summary>
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

    public enum AuctionListingStatus
    {
        Active = 0,
        Sold = 1,
        Cancelled = 2,
        Expired = 3,
    }

    public enum AuctionBuyOrderStatus
    {
        Active = 0,
        Filled = 1,
        Cancelled = 2,
        Expired = 3,
        PartiallyFilled = 4,
    }

    /// <summary>
    /// Result of an auction operation.
    /// </summary>
    [Serializable]
    public struct AuctionResult
    {
        public AuctionRule Rule;
        public string ListingId;
        public string BuyOrderId;
        public AuctionListingRecord Listing;
        public AuctionBuyOrderRecord BuyOrder;

        public AuctionResult(AuctionRule rule, string listingId = null, string buyOrderId = null, AuctionListingRecord listing = null, AuctionBuyOrderRecord buyOrder = null)
        {
            Rule = rule;
            ListingId = listingId;
            BuyOrderId = buyOrderId;
            Listing = listing;
            BuyOrder = buyOrder;
        }

        public static AuctionResult Ok(string listingId = null, string buyOrderId = null, AuctionListingRecord listing = null, AuctionBuyOrderRecord buyOrder = null)
            => new AuctionResult(AuctionRule.Ok, listingId, buyOrderId, listing, buyOrder);

        public static AuctionResult Fail(AuctionRule rule)
            => new AuctionResult(rule);
    }

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