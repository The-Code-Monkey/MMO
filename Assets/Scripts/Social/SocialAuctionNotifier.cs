using System;
using MMO.Auction;

namespace MMO.Social
{
    /// <summary>Server → client auction notifications, raised by the auction service.</summary>
    internal sealed class SocialAuctionNotifier
    {
        public event Action<string, AuctionListingRecord> ListingCreated;
        public event Action<string, string, AuctionListingRecord, AuctionTransactionRecord> ListingSold;
        public event Action<string, AuctionListingRecord> ListingCancelled;
        public event Action<string, AuctionTransactionRecord> PurchaseCompleted;
        public event Action<string, AuctionBuyOrderRecord> BuyOrderCreated;
        public event Action<string, string, AuctionBuyOrderRecord, AuctionTransactionRecord> BuyOrderFulfilled;
        public event Action<string, AuctionBuyOrderRecord> BuyOrderCancelled;

        public void RaiseListingCreated(string sellerAccountId, AuctionListingRecord listing)
            => ListingCreated?.Invoke(sellerAccountId, listing);

        public void RaiseListingSold(
            string buyerAccountId, string sellerAccountId,
            AuctionListingRecord listing, AuctionTransactionRecord transaction)
            => ListingSold?.Invoke(buyerAccountId, sellerAccountId, listing, transaction);

        public void RaiseListingCancelled(string sellerAccountId, AuctionListingRecord listing)
            => ListingCancelled?.Invoke(sellerAccountId, listing);

        public void RaisePurchaseCompleted(string buyerAccountId, AuctionTransactionRecord transaction)
            => PurchaseCompleted?.Invoke(buyerAccountId, transaction);

        public void RaiseBuyOrderCreated(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
            => BuyOrderCreated?.Invoke(buyerAccountId, buyOrder);

        public void RaiseBuyOrderFulfilled(
            string sellerAccountId, string buyerAccountId,
            AuctionBuyOrderRecord buyOrder, AuctionTransactionRecord transaction)
            => BuyOrderFulfilled?.Invoke(sellerAccountId, buyerAccountId, buyOrder, transaction);

        public void RaiseBuyOrderCancelled(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
            => BuyOrderCancelled?.Invoke(buyerAccountId, buyOrder);
    }
}
