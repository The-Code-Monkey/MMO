using System;
using MMO.Auction;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Binds auction records into the instantiated row widgets.</summary>
    internal static class AuctionListRowBinder
    {
        private static Action<string> _onBuy;
        private static Action<string> _onCancelListing;
        private static Action<AuctionBuyOrderRecord> _onFulfill;
        private static Action<string> _onCancelOrder;
        public static void BrowseListing(GameObject item, AuctionListingRecord listing, Action<string> onBuy)
        {
            string displayName = AuctionUiFormat.DisplayName(listing.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text sellerText = item.transform.Find("SellerText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Button buyButton = item.transform.Find("BuyButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.ListingPrice(listing);
            if (sellerText != null) sellerText.text = $"Seller: {listing.SellerPlayerName}";
            if (stackText != null) stackText.text = AuctionUiFormat.StackLabel(listing.StackCount);

            if (buyButton != null)
            {
                buyButton.onClick.RemoveAllListeners();
                buyButton.onClick.AddListener(() => onBuy?.Invoke(listing.ListingId));
            }
        }

        public static void MyListing(GameObject item, AuctionListingRecord listing, Action<string> onCancel)
        {
            string displayName = AuctionUiFormat.DisplayName(listing.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text stackText = item.transform.Find("StackText")?.GetComponent<Text>();
            Text statusText = item.transform.Find("StatusText")?.GetComponent<Text>();
            Button cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.ListingPrice(listing);
            if (stackText != null) stackText.text = AuctionUiFormat.StackLabel(listing.StackCount);

            if (statusText != null)
            {
                statusText.text = AuctionUiFormat.ListingStatus(listing.Status);
                statusText.color = AuctionUiFormat.ListingStatusColor(listing.Status);
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(listing.Status == AuctionListingStatus.Active);
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(() => onCancel?.Invoke(listing.ListingId));
            }
        }

        public static void History(GameObject item, AuctionTransactionRecord tx)
        {
            string displayName = AuctionUiFormat.DisplayName(tx.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text countText = item.transform.Find("CountText")?.GetComponent<Text>();
            Text dateText = item.transform.Find("DateText")?.GetComponent<Text>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = $"{tx.TotalPrice:N0} gold";
            if (countText != null) countText.text = AuctionUiFormat.StackLabel(tx.StackCount);
            if (dateText != null)
                dateText.text = AuctionUiFormat.Date(tx.CompletedAtTicks);
        }

        public static void BrowseBuyOrder(GameObject item, AuctionBuyOrderRecord buyOrder, Action<AuctionBuyOrderRecord> onFulfill)
        {
            string displayName = AuctionUiFormat.DisplayName(buyOrder.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text buyerText = item.transform.Find("BuyerText")?.GetComponent<Text>();
            Text quantityText = item.transform.Find("QuantityText")?.GetComponent<Text>();
            Button fulfillButton = item.transform.Find("FulfillButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.BuyOrderPrice(buyOrder);
            if (buyerText != null) buyerText.text = $"Buyer: {buyOrder.BuyerPlayerName}";
            if (quantityText != null) quantityText.text = AuctionUiFormat.Quantity(buyOrder);

            if (fulfillButton != null)
            {
                fulfillButton.onClick.RemoveAllListeners();
                fulfillButton.onClick.AddListener(() => onFulfill?.Invoke(buyOrder));
            }
        }

        public static void MyBuyOrder(GameObject item, AuctionBuyOrderRecord buyOrder, Action<string> onCancel)
        {
            string displayName = AuctionUiFormat.DisplayName(buyOrder.ItemName);

            Text nameText = item.transform.Find("NameText")?.GetComponent<Text>();
            Text priceText = item.transform.Find("PriceText")?.GetComponent<Text>();
            Text quantityText = item.transform.Find("QuantityText")?.GetComponent<Text>();
            Text statusText = item.transform.Find("StatusText")?.GetComponent<Text>();
            Button cancelButton = item.transform.Find("CancelButton")?.GetComponent<Button>();

            if (nameText != null) nameText.text = displayName;
            if (priceText != null) priceText.text = AuctionUiFormat.BuyOrderUnitPrice(buyOrder);
            if (quantityText != null) quantityText.text = AuctionUiFormat.Quantity(buyOrder);

            if (statusText != null)
            {
                statusText.text = AuctionUiFormat.BuyOrderStatus(buyOrder.Status);
                statusText.color = AuctionUiFormat.BuyOrderStatusColor(buyOrder.Status);
            }

            if (cancelButton != null)
            {
                cancelButton.gameObject.SetActive(buyOrder.Status == AuctionBuyOrderStatus.Active || buyOrder.Status == AuctionBuyOrderStatus.PartiallyFilled);
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(() => onCancel?.Invoke(buyOrder.BuyOrderId));
            }
        }
    }
}
