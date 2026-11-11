using MMO.Inventory;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>Projects an authoritative listing onto the cache row the search kernel consumes.</summary>
    internal static class MarketIndexMapper
    {
        public static SearchRow ToRow(AuctionListingRecord listing)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
            return new SearchRow
            {
                ListingId = listing.ListingId,
                SellerAccountId = listing.SellerAccountId,
                SellerPlayerName = listing.SellerPlayerName,
                ItemName = listing.ItemName,
                Category = CategoryOf(def),
                Tier = def != null ? def.RequiredLevel : 0,
                AskingPrice = listing.PricePerUnit,
                StackCount = listing.StackCount,
                ListedAtTicks = listing.ListedAtTicks,
                ExpiresAtTicks = listing.ExpiresAtTicks,
                SellerZoneId = listing.SellerZoneId,
            };
        }

        public static bool IsActive(AuctionListingRecord listing, long nowTicks)
        {
            if (listing == null || listing.Status != AuctionListingStatus.Active)
                return false;

            return listing.ExpiresAtTicks <= 0 || listing.ExpiresAtTicks > nowTicks;
        }

        private static string CategoryOf(ItemDefinition def)
        {
            if (def == null)
                return "Other";

            if (def.EquipSlot != EquipmentSlot.None)
                return def.EquipSlot.ToString();

            if (def.ToolKind != ToolKind.None)
                return def.ToolKind.ToString();

            return "Other";
        }
    }
}
