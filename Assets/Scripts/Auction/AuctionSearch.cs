using System;
using System.Collections.Generic;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>How the market browse orders its results.</summary>
    public enum AuctionSort
    {
        /// <summary>Newest listing first.</summary>
        Relevance,

        PriceAsc,
        PriceDesc,

        /// <summary>Newest listing first (alias kept for wire stability).</summary>
        Recent,

        /// <summary>Highest tier first.</summary>
        Tier,
    }

    /// <summary>
    /// A player-authored market query: filters, a sort mode, and a page window. Serializable so a
    /// saved search can be persisted and replayed to reproduce a result set.
    /// </summary>
    [Serializable]
    public sealed class AuctionQuery
    {
        public string Text = string.Empty;
        public string Category = string.Empty;
        public int MinTier;
        public int MaxTier = int.MaxValue;
        public string SellerId = string.Empty;
        public long MinPrice;
        public long MaxPrice = long.MaxValue;
        public AuctionSort SortBy = AuctionSort.Relevance;
        public int Page;
        public int PageSize = 20;
    }

    /// <summary>
    /// The single pure kernel behind both the shared-realm search (#82) and the richer filters/sort
    /// (#102). It filters and paginates <see cref="SearchRow"/>s; expiry is re-checked against the
    /// truth on purchase, so a row that slipped past here is only ever a stale result.
    /// </summary>
    public static class AuctionSearch
    {
        /// <summary>Filters, sorts, then pages. Always returns a fresh list; never throws on null.</summary>
        public static IReadOnlyList<SearchRow> Apply(
            IReadOnlyList<SearchRow> all, AuctionQuery query, long nowTicks)
        {
            if (all == null)
                return Array.Empty<SearchRow>();

            var matches = new List<SearchRow>();
            for (int i = 0; i < all.Count; i++)
            {
                if (Matches(all[i], query, nowTicks))
                    matches.Add(all[i]);
            }

            matches.Sort((a, b) => Compare(a, b, query));

            int start = query.Page * query.PageSize;
            if (start >= matches.Count)
                return Array.Empty<SearchRow>();

            int count = Math.Min(query.PageSize, matches.Count - start);
            return matches.GetRange(start, count);
        }

        /// <summary>Whether a row satisfies every non-empty filter and is still listed.</summary>
        public static bool Matches(SearchRow row, AuctionQuery query, long nowTicks)
        {
            if (row == null)
                return false;

            if (!string.IsNullOrEmpty(query.Text) && !Contains(row.ItemName, query.Text))
                return false;

            if (!string.IsNullOrEmpty(query.Category)
                && !string.Equals(row.Category, query.Category, StringComparison.OrdinalIgnoreCase))
                return false;

            if (query.MinTier > 0 && row.Tier < query.MinTier)
                return false;

            if (query.MaxTier < int.MaxValue && row.Tier > query.MaxTier)
                return false;

            if (!string.IsNullOrEmpty(query.SellerId)
                && !string.Equals(row.SellerAccountId, query.SellerId, StringComparison.Ordinal))
                return false;

            if (query.MinPrice > 0 && row.AskingPrice < query.MinPrice)
                return false;

            if (query.MaxPrice < long.MaxValue && row.AskingPrice > query.MaxPrice)
                return false;

            if (row.ExpiresAtTicks > 0 && row.ExpiresAtTicks <= nowTicks)
                return false;

            return true;
        }

        /// <summary>
        /// The sort key, with the id tie-break that keeps paging stable: two rows equal on the
        /// chosen key always order by <see cref="SearchRow.ListingId"/>, so a page boundary never
        /// duplicates or skips a row.
        /// </summary>
        public static int Compare(SearchRow a, SearchRow b, AuctionQuery query)
        {
            int c;
            switch (query.SortBy)
            {
                case AuctionSort.PriceAsc:
                    c = a.AskingPrice.CompareTo(b.AskingPrice);
                    break;
                case AuctionSort.PriceDesc:
                    c = b.AskingPrice.CompareTo(a.AskingPrice);
                    break;
                case AuctionSort.Tier:
                    c = b.Tier.CompareTo(a.Tier);
                    break;
                default:
                    c = b.ListedAtTicks.CompareTo(a.ListedAtTicks);
                    break;
            }

            if (c != 0)
                return c;

            return string.CompareOrdinal(a.ListingId, b.ListingId);
        }

        private static bool Contains(string value, string text)
            => value != null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
