using System;
using MMO.Auction;
using MMO.Inventory;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>Text and colour formatting shared by auction rows.</summary>
    public static class AuctionUiFormat
    {
        public static string DisplayName(string itemName)
        {
            ItemDefinition def = ItemDefinitionRegistry.Resolve(itemName);
            return def != null ? def.ItemName : itemName;
        }

        public static string ListingPrice(AuctionListingRecord listing)
            => $"{listing.TotalPrice:N0} gold ({listing.PricePerUnit:N0} each)";

        public static string StackLabel(int stackCount)
            => stackCount > 1 ? $"x{stackCount}" : string.Empty;

        public static string BuyOrderPrice(AuctionBuyOrderRecord order)
            => $"{order.PricePerUnit:N0} gold each (Total: {order.TotalValue:N0})";

        public static string BuyOrderUnitPrice(AuctionBuyOrderRecord order)
            => $"{order.PricePerUnit:N0} gold each";

        public static string Quantity(AuctionBuyOrderRecord order)
            => $"{order.QuantityRemaining}/{order.QuantityTotal}";

        public static string ListingStatus(AuctionListingStatus status)
            => status switch
            {
                AuctionListingStatus.Active => "Active",
                AuctionListingStatus.Sold => "Sold",
                AuctionListingStatus.Cancelled => "Cancelled",
                AuctionListingStatus.Expired => "Expired",
                _ => status.ToString(),
            };

        public static Color ListingStatusColor(AuctionListingStatus status)
            => status switch
            {
                AuctionListingStatus.Active => Color.green,
                AuctionListingStatus.Sold => Color.blue,
                AuctionListingStatus.Cancelled => Color.gray,
                AuctionListingStatus.Expired => Color.red,
                _ => Color.white,
            };

        public static string BuyOrderStatus(AuctionBuyOrderStatus status)
            => status switch
            {
                AuctionBuyOrderStatus.Active => "Active",
                AuctionBuyOrderStatus.PartiallyFilled => "Partially Filled",
                AuctionBuyOrderStatus.Filled => "Filled",
                AuctionBuyOrderStatus.Cancelled => "Cancelled",
                AuctionBuyOrderStatus.Expired => "Expired",
                _ => status.ToString(),
            };

        public static Color BuyOrderStatusColor(AuctionBuyOrderStatus status)
            => status switch
            {
                AuctionBuyOrderStatus.Active => Color.green,
                AuctionBuyOrderStatus.PartiallyFilled => Color.yellow,
                AuctionBuyOrderStatus.Filled => Color.blue,
                AuctionBuyOrderStatus.Cancelled => Color.gray,
                AuctionBuyOrderStatus.Expired => Color.red,
                _ => Color.white,
            };

        public static string Date(long ticks)
        {
            DateTime date = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            return date.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
