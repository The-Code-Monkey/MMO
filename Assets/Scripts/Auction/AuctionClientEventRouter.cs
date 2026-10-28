using System;
using MMO.Social;

namespace MMO.Auction
{
    /// <summary>
    /// Filters server auction pushes down to the events this client's UI cares about.
    /// </summary>
    internal sealed class AuctionClientEventRouter
    {
        private SocialHubService _hub;
        private Func<string> _accountId;

        public event Action<AuctionListingRecord> ListingCreated;
        public event Action<AuctionListingRecord, AuctionTransactionRecord> ListingBought;
        public event Action<AuctionListingRecord> ListingCancelled;
        public event Action<AuctionTransactionRecord> PurchaseCompleted;
        public event Action<AuctionBuyOrderRecord> BuyOrderCreated;
        public event Action<AuctionBuyOrderRecord, AuctionTransactionRecord> BuyOrderFulfilled;
        public event Action<AuctionBuyOrderRecord> BuyOrderCancelled;

        public void Bind(SocialHubService hub, Func<string> accountId)
        {
            Unbind();
            _hub = hub;
            _accountId = accountId;

            if (_hub == null)
                return;

            _hub.AuctionListingCreated += OnListingCreated;
            _hub.AuctionListingSold += OnListingSold;
            _hub.AuctionListingCancelled += OnListingCancelled;
            _hub.AuctionPurchaseCompleted += OnPurchaseCompleted;
            _hub.AuctionBuyOrderCreated += OnBuyOrderCreated;
            _hub.AuctionBuyOrderFulfilled += OnBuyOrderFulfilled;
            _hub.AuctionBuyOrderCancelled += OnBuyOrderCancelled;
        }

        public void Unbind()
        {
            if (_hub == null)
                return;

            _hub.AuctionListingCreated -= OnListingCreated;
            _hub.AuctionListingSold -= OnListingSold;
            _hub.AuctionListingCancelled -= OnListingCancelled;
            _hub.AuctionPurchaseCompleted -= OnPurchaseCompleted;
            _hub.AuctionBuyOrderCreated -= OnBuyOrderCreated;
            _hub.AuctionBuyOrderFulfilled -= OnBuyOrderFulfilled;
            _hub.AuctionBuyOrderCancelled -= OnBuyOrderCancelled;
            _hub = null;
        }

        private void OnListingCreated(string accountId, AuctionListingRecord listing)
        {
            if (_accountId() == accountId)
                ListingCreated?.Invoke(listing);
        }

        private void OnListingSold(
            string buyerAccountId, string sellerAccountId,
            AuctionListingRecord listing, AuctionTransactionRecord transaction)
        {
            string mine = _accountId();
            if (mine == buyerAccountId || mine == sellerAccountId)
                ListingBought?.Invoke(listing, transaction);
        }

        private void OnListingCancelled(string sellerAccountId, AuctionListingRecord listing)
        {
            if (_accountId() == sellerAccountId)
                ListingCancelled?.Invoke(listing);
        }

        private void OnPurchaseCompleted(string buyerAccountId, AuctionTransactionRecord transaction)
        {
            if (_accountId() == buyerAccountId)
                PurchaseCompleted?.Invoke(transaction);
        }

        private void OnBuyOrderCreated(string accountId, AuctionBuyOrderRecord buyOrder)
        {
            if (_accountId() == accountId)
                BuyOrderCreated?.Invoke(buyOrder);
        }

        private void OnBuyOrderFulfilled(
            string sellerAccountId, string buyerAccountId,
            AuctionBuyOrderRecord buyOrder, AuctionTransactionRecord transaction)
        {
            string mine = _accountId();
            if (mine == sellerAccountId || mine == buyerAccountId)
                BuyOrderFulfilled?.Invoke(buyOrder, transaction);
        }

        private void OnBuyOrderCancelled(string buyerAccountId, AuctionBuyOrderRecord buyOrder)
        {
            if (_accountId() == buyerAccountId)
                BuyOrderCancelled?.Invoke(buyOrder);
        }
    }
}
