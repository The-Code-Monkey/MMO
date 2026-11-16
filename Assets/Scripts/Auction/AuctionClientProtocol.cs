using System;
using System.Collections.Generic;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Wire DTOs and JSON codec for auction query responses. Public so the EditMode suite can pin
    /// the exact bytes each response encodes, the same way the other payload codecs are tested.
    /// </summary>
    public static class AuctionClientProtocol
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

        [Serializable]
        private sealed class SearchRowResults
        {
            public List<SearchRow> rows;
        }

        [Serializable]
        private sealed class SavedSearchResults
        {
            public List<AuctionQuery> queries;
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

        public static string EncodeSearchRows(IReadOnlyList<SearchRow> rows)
            => JsonUtility.ToJson(new SearchRowResults { rows = new List<SearchRow>(rows) });

        public static List<SearchRow> DecodeSearchRows(string json)
        {
            SearchRowResults results = string.IsNullOrEmpty(json)
                ? null
                : JsonUtility.FromJson<SearchRowResults>(json);
            return results?.rows ?? new List<SearchRow>();
        }

        public static string EncodeSavedSearches(IReadOnlyList<AuctionQuery> queries)
            => JsonUtility.ToJson(new SavedSearchResults { queries = new List<AuctionQuery>(queries) });

        public static List<AuctionQuery> DecodeSavedSearches(string json)
        {
            SavedSearchResults results = string.IsNullOrEmpty(json)
                ? null
                : JsonUtility.FromJson<SavedSearchResults>(json);
            return results?.queries ?? new List<AuctionQuery>();
        }

        public static AuctionQuery DecodeQuery(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new AuctionQuery();

            try
            {
                return JsonUtility.FromJson<AuctionQuery>(json) ?? new AuctionQuery();
            }
            catch (ArgumentException)
            {
                return new AuctionQuery();
            }
        }
    }
}
