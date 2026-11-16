using System;
using System.Collections.Generic;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>
    /// The pure kernel behind the realm-wide market cache. The authoritative
    /// <c>auction_listings</c> records stay the truth; these rules only keep
    /// <see cref="SearchRow"/>s converging on them, so a stale or lost row is a UI nuisance and
    /// never a settlement risk.
    /// </summary>
    public static class MarketIndexRules
    {
        /// <summary>Adds the row, or replaces the row with the same listing id, stamping it.</summary>
        public static void Upsert(IList<SearchRow> rows, SearchRow row, long nowTicks)
        {
            if (rows == null || row == null)
                return;

            SearchRow stamped = Copy(row, nowTicks);
            int existing = IndexOf(rows, row.ListingId);
            if (existing >= 0)
                rows[existing] = stamped;
            else
                rows.Add(stamped);
        }

        /// <summary>Drops the row by listing id. Returns false when it was not indexed.</summary>
        public static bool Remove(IList<SearchRow> rows, string listingId)
        {
            int existing = IndexOf(rows, listingId);
            if (existing < 0)
                return false;

            rows.RemoveAt(existing);
            return true;
        }

        public static SearchRow Find(IReadOnlyList<SearchRow> rows, string listingId)
        {
            if (rows == null || string.IsNullOrEmpty(listingId))
                return null;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].ListingId == listingId)
                    return rows[i];
            }

            return null;
        }

        /// <summary>
        /// Recomputes the cache from the authoritative rows. Unchanged truth rows keep their
        /// <see cref="SearchRow.IndexedAtTicks"/> so the stamp records when the row last moved
        /// rather than when a reconcile last swept past it. Expired truth rows are dropped.
        /// </summary>
        public static List<SearchRow> Reconcile(
            IReadOnlyList<SearchRow> current, IReadOnlyList<SearchRow> truth, long nowTicks)
        {
            var result = new List<SearchRow>();
            if (truth == null)
                return result;

            for (int i = 0; i < truth.Count; i++)
            {
                SearchRow row = truth[i];
                if (row == null || string.IsNullOrEmpty(row.ListingId))
                    continue;

                if (row.ExpiresAtTicks > 0 && row.ExpiresAtTicks <= nowTicks)
                    continue;

                SearchRow previous = Find(current, row.ListingId);
                result.Add(previous != null && SameListing(previous, row)
                    ? previous
                    : Copy(row, nowTicks));
            }

            return result;
        }

        /// <summary>Whether a full rebuild is due, based on the last reconcile stamp.</summary>
        public static bool IsStale(long reconciledAtTicks, long nowTicks, long maxAgeTicks)
            => reconciledAtTicks <= 0 || maxAgeTicks <= 0 || nowTicks - reconciledAtTicks > maxAgeTicks;

        private static int IndexOf(IList<SearchRow> rows, string listingId)
        {
            if (rows == null || string.IsNullOrEmpty(listingId))
                return -1;

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].ListingId == listingId)
                    return i;
            }

            return -1;
        }

        private static SearchRow Copy(SearchRow row, long indexedAtTicks) => new SearchRow
        {
            ListingId = row.ListingId,
            SellerAccountId = row.SellerAccountId,
            SellerPlayerName = row.SellerPlayerName,
            ItemName = row.ItemName,
            Category = row.Category,
            Tier = row.Tier,
            AskingPrice = row.AskingPrice,
            StackCount = row.StackCount,
            ListedAtTicks = row.ListedAtTicks,
            ExpiresAtTicks = row.ExpiresAtTicks,
            SellerZoneId = row.SellerZoneId,
            IndexedAtTicks = indexedAtTicks,
        };

        private static bool SameListing(SearchRow a, SearchRow b)
            => a.SellerAccountId == b.SellerAccountId
                && a.SellerPlayerName == b.SellerPlayerName
                && a.ItemName == b.ItemName
                && a.Category == b.Category
                && a.Tier == b.Tier
                && a.AskingPrice == b.AskingPrice
                && a.StackCount == b.StackCount
                && a.ListedAtTicks == b.ListedAtTicks
                && a.ExpiresAtTicks == b.ExpiresAtTicks
                && a.SellerZoneId == b.SellerZoneId;
    }
}
