using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>
    /// Translates the browse bar's raw text into a validated <see cref="AuctionQuery"/>, and a
    /// <see cref="SearchRow"/> into the listing shape the existing row binder already renders.
    ///
    /// <para>
    /// Parsing lives here, not in the view, for the same reason the search kernel is pure: a blank
    /// price box means "no bound" (0 / <c>long.MaxValue</c>), not "zero gold", and a negative page
    /// means the first page. Those defaults are exactly the bugs a UI tends to get wrong, so they
    /// are asserted in EditMode.
    /// </para>
    /// </summary>
    public static class AuctionQueryMapper
    {
        public static AuctionQuery Build(
            string text,
            string category,
            string minTier,
            string maxTier,
            string seller,
            string minPrice,
            string maxPrice,
            AuctionSort sortBy,
            int page,
            int pageSize = AuctionSavedSearchRules.DefaultPageSize)
        {
            return new AuctionQuery
            {
                Text = (text ?? string.Empty).Trim(),
                Category = (category ?? string.Empty).Trim(),
                MinTier = ParseInt(minTier, 0),
                MaxTier = ParseInt(maxTier, int.MaxValue),
                SellerId = (seller ?? string.Empty).Trim(),
                MinPrice = ParseLong(minPrice, 0),
                MaxPrice = ParseLong(maxPrice, long.MaxValue),
                SortBy = sortBy,
                Page = page < 0 ? 0 : page,
                PageSize = pageSize <= 0 ? AuctionSavedSearchRules.DefaultPageSize : pageSize,
            };
        }

        /// <summary>
        /// The category names a browse filter can offer: wearable/tool categories the market index
        /// derives, plus the "Other" bucket. Sorted by name for a stable picker.
        /// </summary>
        public static IReadOnlyList<string> Categories()
        {
            var categories = new List<string>();

            foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
            {
                if (slot != EquipmentSlot.None)
                    categories.Add(slot.ToString());
            }

            foreach (ToolKind kind in Enum.GetValues(typeof(ToolKind)))
            {
                if (kind != ToolKind.None)
                    categories.Add(kind.ToString());
            }

            categories.Add("Other");
            categories.Sort(StringComparer.Ordinal);
            return categories;
        }

        /// <summary>
        /// Presents a search row through the existing listing row binder. The row is read-only
        /// market data; buying still goes through the listing id and the server's own purchase path.
        /// </summary>
        public static AuctionListingRecord ToListingRecord(SearchRow row)
        {
            if (row == null)
                return null;

            return new AuctionListingRecord
            {
                ListingId = row.ListingId,
                SellerAccountId = row.SellerAccountId,
                SellerPlayerName = row.SellerPlayerName,
                ItemName = row.ItemName,
                StackCount = row.StackCount,
                PricePerUnit = row.AskingPrice,
                ListedAtTicks = row.ListedAtTicks,
                ExpiresAtTicks = row.ExpiresAtTicks,
                Status = AuctionListingStatus.Active,
                SellerZoneId = row.SellerZoneId,
            };
        }

        private static int ParseInt(string value, int fallback)
            => int.TryParse((value ?? string.Empty).Trim(), out int parsed) && parsed >= 0 ? parsed : fallback;

        private static long ParseLong(string value, long fallback)
            => long.TryParse((value ?? string.Empty).Trim(), out long parsed) && parsed >= 0 ? parsed : fallback;
    }
}
