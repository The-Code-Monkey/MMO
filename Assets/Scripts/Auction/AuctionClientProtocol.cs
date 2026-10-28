using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>Wire DTOs and JSON codec for auction query responses.</summary>
    internal static class AuctionClientProtocol
    {
        [Serializable]
        private sealed class ListingResults
        {
            public List<AuctionListingRecord> listings;
        }

        [Serializable]
        private sealed class TransactionHistory
        {
            public List<AuctionTransactionRecord> transactions;
        }

        [Serializable]
        private sealed class BuyOrderResults
        {
            public List<AuctionBuyOrderRecord> buyOrders;
        }

        public static string EncodeListings(IReadOnlyList<AuctionListingRecord> listings)
            => JsonUtility.ToJson(new ListingResults { listings = new List<AuctionListingRecord>(listings) });

        public static List<AuctionListingRecord> DecodeListings(string json)
        {
            ListingResults results = string.IsNullOrEmpty(json)
                ? null
                : JsonUtility.FromJson<ListingResults>(json);
            return results?.listings ?? new List<AuctionListingRecord>();
        }

        public static string EncodeHistory(IReadOnlyList<AuctionTransactionRecord> transactions)
            => JsonUtility.ToJson(new TransactionHistory { transactions = new List<AuctionTransactionRecord>(transactions) });

        public static List<AuctionTransactionRecord> DecodeHistory(string json)
        {
            TransactionHistory results = string.IsNullOrEmpty(json)
                ? null
                : JsonUtility.FromJson<TransactionHistory>(json);
            return results?.transactions ?? new List<AuctionTransactionRecord>();
        }

        public static string EncodeBuyOrders(IReadOnlyList<AuctionBuyOrderRecord> buyOrders)
            => JsonUtility.ToJson(new BuyOrderResults { buyOrders = new List<AuctionBuyOrderRecord>(buyOrders) });

        public static List<AuctionBuyOrderRecord> DecodeBuyOrders(string json)
        {
            BuyOrderResults results = string.IsNullOrEmpty(json)
                ? null
                : JsonUtility.FromJson<BuyOrderResults>(json);
            return results?.buyOrders ?? new List<AuctionBuyOrderRecord>();
        }
    }
}
