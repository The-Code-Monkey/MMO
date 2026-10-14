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
    /// Tracks a completed auction transaction for history/audit.
    /// </summary>
    [Serializable]
    public sealed class AuctionTransactionRecord
    {
        public const string Collection = "auction_transactions";

        public string TransactionId;
        public string ListingId;
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
    /// Per-player index of their active listings and purchases.
    /// </summary>
    [Serializable]
    public sealed class AuctionIndexRecord
    {
        public const string Collection = "auction_index";

        public string AccountId;
        public List<string> ActiveListingIds = new List<string>();
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

    /// <summary>
    /// Result of an auction operation.
    /// </summary>
    [Serializable]
    public struct AuctionResult
    {
        public AuctionRule Rule;
        public string ListingId;
        public AuctionListingRecord Listing;

        public AuctionResult(AuctionRule rule, string listingId = null, AuctionListingRecord listing = null)
        {
            Rule = rule;
            ListingId = listingId;
            Listing = listing;
        }

        public static AuctionResult Ok(string listingId = null, AuctionListingRecord listing = null)
            => new AuctionResult(AuctionRule.Ok, listingId, listing);

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
    }
}