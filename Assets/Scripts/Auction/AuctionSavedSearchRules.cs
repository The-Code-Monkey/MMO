using System.Collections.Generic;

namespace MMO.Auction
{
    /// <summary>The outcome of adding or removing a saved search.</summary>
    public enum SavedSearchRule
    {
        Ok = 0,
        Full = 1,
        Duplicate = 2,
        NotFound = 3,

        /// <summary>The backend write could not be completed after retries.</summary>
        Conflict = 4,

        /// <summary>The query or account was missing.</summary>
        Invalid = 5,
    }

    /// <summary>
    /// Saved searches as pure list arithmetic: a hard cap, duplicate detection, and normalisation so
    /// the same filters saved twice compare equal regardless of whitespace or page.
    ///
    /// <para>
    /// A saved search is a filter template, not a position in the results — page is always reset and
    /// the page size normalised — so replaying one shows the first page, which is what a player
    /// expects from a chip.
    /// </para>
    /// </summary>
    public static class AuctionSavedSearchRules
    {
        /// <summary>How many searches one account may keep.</summary>
        public const int MaxSavedSearches = 10;

        /// <summary>Default page size for a saved/replayed query.</summary>
        public const int DefaultPageSize = 20;

        /// <summary>Strips incidental values and trims the free-text fields.</summary>
        public static AuctionQuery Normalize(AuctionQuery query)
        {
            AuctionQuery source = query ?? new AuctionQuery();

            // A copy, not the caller's instance: comparison must not rewrite the query a view is
            // still editing, and a saved chip must not share state with the live filter bar.
            return new AuctionQuery
            {
                Text = (source.Text ?? string.Empty).Trim(),
                Category = (source.Category ?? string.Empty).Trim(),
                MinTier = source.MinTier,
                MaxTier = source.MaxTier,
                SellerId = (source.SellerId ?? string.Empty).Trim(),
                MinPrice = source.MinPrice,
                MaxPrice = source.MaxPrice,
                SortBy = source.SortBy,
                Page = 0,
                PageSize = source.PageSize <= 0 ? DefaultPageSize : source.PageSize,
            };
        }

        /// <summary>True when two queries filter the same market the same way, ignoring paging.</summary>
        public static bool SameQuery(AuctionQuery left, AuctionQuery right)
        {
            if (left == null || right == null)
                return left == right;

            AuctionQuery a = Normalize(left);
            AuctionQuery b = Normalize(right);

            return a.Text == b.Text
                && a.Category == b.Category
                && a.MinTier == b.MinTier
                && a.MaxTier == b.MaxTier
                && a.SellerId == b.SellerId
                && a.MinPrice == b.MinPrice
                && a.MaxPrice == b.MaxPrice
                && a.SortBy == b.SortBy;
        }

        /// <summary>Whether <paramref name="query"/> can be added to the list as it stands.</summary>
        public static SavedSearchRule CanAdd(IReadOnlyList<AuctionQuery> saved, AuctionQuery query)
        {
            if (saved == null)
                return SavedSearchRule.Ok;

            if (saved.Count >= MaxSavedSearches)
                return SavedSearchRule.Full;

            for (int i = 0; i < saved.Count; i++)
            {
                if (SameQuery(saved[i], query))
                    return SavedSearchRule.Duplicate;
            }

            return SavedSearchRule.Ok;
        }

        /// <summary>
        /// Appends the normalised query when it fits and is not a duplicate. The list is mutated in
        /// place — it is the same <c>List</c> the record serializes.
        /// </summary>
        public static SavedSearchRule Add(List<AuctionQuery> saved, AuctionQuery query)
        {
            saved ??= new List<AuctionQuery>();

            SavedSearchRule rule = CanAdd(saved, query);
            if (rule != SavedSearchRule.Ok)
                return rule;

            saved.Add(Normalize(query));
            return SavedSearchRule.Ok;
        }

        /// <summary>Removes the entry at <paramref name="index"/>; out-of-range is NotFound.</summary>
        public static SavedSearchRule RemoveAt(List<AuctionQuery> saved, int index)
        {
            if (saved == null || index < 0 || index >= saved.Count)
                return SavedSearchRule.NotFound;

            saved.RemoveAt(index);
            return SavedSearchRule.Ok;
        }
    }
}
