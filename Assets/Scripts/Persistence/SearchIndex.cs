using System;
using System.Collections.Generic;

namespace MMO.Persistence
{
    /// <summary>
    /// One searchable row in the realm-wide market index. It is a cache: the authoritative
    /// <c>auction_listings</c> record is the truth, and a stale row is a UI nuisance, never a
    /// settlement risk. Category/tier are pre-computed by the auction service from the item
    /// definition so the search kernel stays a pure filter/sort over plain values.
    /// </summary>
    [Serializable]
    public sealed class SearchRow
    {
        public string ListingId;
        public string SellerAccountId;
        public string SellerPlayerName;
        public string ItemName;

        /// <summary>Filter key derived from the item's equip slot or tool kind, or "Other".</summary>
        public string Category;

        /// <summary>Filter key derived from the item's required level (the tier band).</summary>
        public int Tier;

        /// <summary>Price per unit, in currency units.</summary>
        public long AskingPrice;

        public int StackCount;

        public long ListedAtTicks;
        public long ExpiresAtTicks;

        /// <summary>The zone the seller's listing record lives on; routes a cross-zone buy.</summary>
        public string SellerZoneId;

        /// <summary>When the row was written; the rebuild-from-truth reconcile uses it to detect drift.</summary>
        public long IndexedAtTicks;
    }

    /// <summary>
    /// The realm-wide search index: collection "market_search_index", key = <see cref="RealmKey"/>,
    /// owned by the shared service account. One object hosts every zone's search rows; the
    /// authoritative listings stay per-cluster.
    /// </summary>
    [Serializable]
    public sealed class MarketSearchIndex
    {
        public const string Collection = "market_search_index";
        public const string RealmKey = "realm";

        public List<SearchRow> Rows = new List<SearchRow>();

        /// <summary>When the last full rebuild from the authoritative listings ran; drives staleness.</summary>
        public long ReconciledAtTicks;

        /// <summary>The backend's revision hash for optimistic concurrency; not index data.</summary>
        public string StorageVersion;
    }
}
