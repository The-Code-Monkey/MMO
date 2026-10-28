using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using MMO.Auction;
using MMO.Inventory;
using MMO.Social;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Client-side auction house controller. Sends intents to the server via FishNet RPCs.
    /// The server (SocialHubService.AuctionService) handles the actual Nakama operations.
    /// </summary>
    public sealed class PlayerAuctionHouse : NetworkBehaviour
    {
        private readonly AuctionClientEventRouter _events = new AuctionClientEventRouter();

        private PlayerInventory _inventory;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _inventory = GetComponent<PlayerInventory>();
        }

        /// <summary>Checks if the current zone has an auction house.</summary>
        private bool CanAccessAuctionHouse()
        {
            return SocialHubService.Instance != null && SocialHubService.Instance.HasAuctionHouse;
        }

        /// <summary>Owner-side intent: list an item from the bag on the auction house.</summary>
        public void RequestCreateListing(int bagX, int bagY, long pricePerUnit, int stackCount = -1, int durationHours = AuctionService.DefaultListingDurationHours)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestCreateListing(bagX, bagY, pricePerUnit, stackCount, durationHours);
        }

        [ServerRpc]
        private void RpcRequestCreateListing(int bagX, int bagY, long pricePerUnit, int stackCount, int durationHours, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.CreateListingAsync(accountId, bagX, bagY, pricePerUnit, stackCount, durationHours);
        }

        /// <summary>Owner-side intent: buy an item from the auction house.</summary>
        public void RequestBuyListing(string listingId)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestBuyListing(listingId);
        }

        [ServerRpc]
        private void RpcRequestBuyListing(string listingId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.BuyListingAsync(accountId, listingId);
        }

        /// <summary>Owner-side intent: cancel an active listing.</summary>
        public void RequestCancelListing(string listingId)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestCancelListing(listingId);
        }

        [ServerRpc]
        private void RpcRequestCancelListing(string listingId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.CancelListingAsync(accountId, listingId);
        }

        /// <summary>Owner-side intent: search the auction house.</summary>
        public void RequestSearch(string itemNameFilter = null, long maxPrice = 0, int page = 0, int pageSize = 50)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestSearch(itemNameFilter, maxPrice, page, pageSize);
        }

        [ServerRpc]
        private void RpcRequestSearch(string itemNameFilter, long maxPrice, int page, int pageSize, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            // Search doesn't need account validation, but we pass it for potential future use
            _ = SearchAndRespondAsync(accountId, itemNameFilter, maxPrice, page, pageSize, sender);
        }

        /// <summary>Owner-side intent: get my active listings.</summary>
        public void RequestMyListings()
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestMyListings();
        }

        [ServerRpc]
        private void RpcRequestMyListings(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = MyListingsAndRespondAsync(accountId, sender);
        }

        /// <summary>Owner-side intent: get purchase history.</summary>
        public void RequestPurchaseHistory(int limit = 20)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestPurchaseHistory(limit);
        }

        [ServerRpc]
        private void RpcRequestPurchaseHistory(int limit, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = PurchaseHistoryAndRespondAsync(accountId, limit, sender);
        }

        /// <summary>Owner-side intent: create a buy order.</summary>
        public void RequestCreateBuyOrder(string itemName, int quantity, long pricePerUnit, int durationHours = AuctionService.DefaultListingDurationHours)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestCreateBuyOrder(itemName, quantity, pricePerUnit, durationHours);
        }

        [ServerRpc]
        private void RpcRequestCreateBuyOrder(string itemName, int quantity, long pricePerUnit, int durationHours, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.CreateBuyOrderAsync(accountId, itemName, quantity, pricePerUnit, durationHours);
        }

        /// <summary>Owner-side intent: fulfill a buy order.</summary>
        public void RequestFulfillBuyOrder(string buyOrderId, int bagX, int bagY, int stackCount = -1)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestFulfillBuyOrder(buyOrderId, bagX, bagY, stackCount);
        }

        [ServerRpc]
        private void RpcRequestFulfillBuyOrder(string buyOrderId, int bagX, int bagY, int stackCount, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.FulfillBuyOrderAsync(accountId, buyOrderId, bagX, bagY, stackCount);
        }

        /// <summary>Owner-side intent: cancel a buy order.</summary>
        public void RequestCancelBuyOrder(string buyOrderId)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestCancelBuyOrder(buyOrderId);
        }

        [ServerRpc]
        private void RpcRequestCancelBuyOrder(string buyOrderId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SocialHubService.Instance.Auction.CancelBuyOrderAsync(accountId, buyOrderId);
        }

        /// <summary>Owner-side intent: get my active buy orders.</summary>
        public void RequestMyBuyOrders()
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestMyBuyOrders();
        }

        [ServerRpc]
        private void RpcRequestMyBuyOrders(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = MyBuyOrdersAndRespondAsync(accountId, sender);
        }

        /// <summary>Owner-side intent: search buy orders.</summary>
        public void RequestSearchBuyOrders(string itemNameFilter = null, long minPrice = 0, int page = 0, int pageSize = 50)
        {
            if (!IsOwner)
                return;

            if (!CanAccessAuctionHouse())
            {
                Debug.LogWarning("[AuctionHouse] This zone does not have an auction house.");
                return;
            }

            RpcRequestSearchBuyOrders(itemNameFilter, minPrice, page, pageSize);
        }

        [ServerRpc]
        private void RpcRequestSearchBuyOrders(string itemNameFilter, long minPrice, int page, int pageSize, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (SocialHubService.Instance == null || SocialHubService.Instance.Auction == null)
                return;

            if (!SocialHubService.Instance.HasAuctionHouse)
                return;

            string accountId = GetAccountId(sender);
            if (string.IsNullOrEmpty(accountId))
                return;

            _ = SearchBuyOrdersAndRespondAsync(accountId, itemNameFilter, minPrice, page, pageSize, sender);
        }

        private async System.Threading.Tasks.Task SearchAndRespondAsync(string accountId, string itemNameFilter, long maxPrice, int page, int pageSize, NetworkConnection sender)
        {
            List<AuctionListingRecord> results = await SocialHubService.Instance.Auction.SearchAsync(itemNameFilter, maxPrice, page, pageSize);
            string json = AuctionClientProtocol.EncodeListings(results);
            TargetRpcSearchResults(sender, json);
        }

        private async System.Threading.Tasks.Task MyListingsAndRespondAsync(string accountId, NetworkConnection sender)
        {
            List<AuctionListingRecord> results = await SocialHubService.Instance.Auction.GetMyListingsAsync(accountId);
            string json = AuctionClientProtocol.EncodeListings(results);
            TargetRpcMyListings(sender, json);
        }

        private async System.Threading.Tasks.Task PurchaseHistoryAndRespondAsync(string accountId, int limit, NetworkConnection sender)
        {
            List<AuctionTransactionRecord> results = await SocialHubService.Instance.Auction.GetPurchaseHistoryAsync(accountId, limit);
            string json = AuctionClientProtocol.EncodeHistory(results);
            TargetRpcPurchaseHistory(sender, json);
        }

        private async System.Threading.Tasks.Task SearchBuyOrdersAndRespondAsync(string accountId, string itemNameFilter, long minPrice, int page, int pageSize, NetworkConnection sender)
        {
            List<AuctionBuyOrderRecord> results = await SocialHubService.Instance.Auction.SearchBuyOrdersAsync(itemNameFilter, minPrice, page, pageSize);
            string json = AuctionClientProtocol.EncodeBuyOrders(results);
            TargetRpcSearchBuyOrders(sender, json);
        }

        private async System.Threading.Tasks.Task MyBuyOrdersAndRespondAsync(string accountId, NetworkConnection sender)
        {
            List<AuctionBuyOrderRecord> results = await SocialHubService.Instance.Auction.GetMyBuyOrdersAsync(accountId);
            string json = AuctionClientProtocol.EncodeBuyOrders(results);
            TargetRpcMyBuyOrders(sender, json);
        }

        [TargetRpc]
        private void TargetRpcSearchResults(NetworkConnection target, string json)
        {
            if (!IsOwner)
                return;

            List<AuctionListingRecord> results = AuctionClientProtocol.DecodeListings(json);
            OnSearchResultsReceived?.Invoke(results);
        }

        [TargetRpc]
        private void TargetRpcMyListings(NetworkConnection target, string json)
        {
            if (!IsOwner)
                return;

            List<AuctionListingRecord> results = AuctionClientProtocol.DecodeListings(json);
            OnMyListingsReceived?.Invoke(results);
        }

        [TargetRpc]
        private void TargetRpcPurchaseHistory(NetworkConnection target, string json)
        {
            if (!IsOwner)
                return;

            List<AuctionTransactionRecord> history = AuctionClientProtocol.DecodeHistory(json);
            OnPurchaseHistoryReceived?.Invoke(history);
        }

        [TargetRpc]
        private void TargetRpcSearchBuyOrders(NetworkConnection target, string json)
        {
            if (!IsOwner)
                return;

            List<AuctionBuyOrderRecord> results = AuctionClientProtocol.DecodeBuyOrders(json);
            OnSearchBuyOrdersReceived?.Invoke(results);
        }

        [TargetRpc]
        private void TargetRpcMyBuyOrders(NetworkConnection target, string json)
        {
            if (!IsOwner)
                return;

            List<AuctionBuyOrderRecord> results = AuctionClientProtocol.DecodeBuyOrders(json);
            OnMyBuyOrdersReceived?.Invoke(results);
        }

        private string GetAccountId(NetworkConnection sender)
        {
            // The account ID is stored in the PlayerPersistenceService or can be retrieved from the connection
            // For now, we use the PlayerGuild's account ID which is registered with SocialHubService
            PlayerGuild guild = GetComponent<PlayerGuild>();
            return guild != null ? guild.AccountId : sender.ClientId.ToString();
        }

        // Events for UI to subscribe to
        public event Action<List<AuctionListingRecord>> OnSearchResultsReceived;
        public event Action<List<AuctionListingRecord>> OnMyListingsReceived;
        public event Action<List<AuctionTransactionRecord>> OnPurchaseHistoryReceived;
        public event Action<List<AuctionBuyOrderRecord>> OnSearchBuyOrdersReceived;
        public event Action<List<AuctionBuyOrderRecord>> OnMyBuyOrdersReceived;

        // Events from server (via SocialHubService)
        public event Action<AuctionListingRecord> OnListingCreated
        {
            add => _events.ListingCreated += value;
            remove => _events.ListingCreated -= value;
        }

        public event Action<AuctionListingRecord, AuctionTransactionRecord> OnListingBought
        {
            add => _events.ListingBought += value;
            remove => _events.ListingBought -= value;
        }

        public event Action<AuctionListingRecord> OnListingCancelled
        {
            add => _events.ListingCancelled += value;
            remove => _events.ListingCancelled -= value;
        }

        public event Action<AuctionTransactionRecord> OnPurchaseCompleted
        {
            add => _events.PurchaseCompleted += value;
            remove => _events.PurchaseCompleted -= value;
        }

        public event Action<AuctionBuyOrderRecord> OnBuyOrderCreated
        {
            add => _events.BuyOrderCreated += value;
            remove => _events.BuyOrderCreated -= value;
        }

        public event Action<AuctionBuyOrderRecord, AuctionTransactionRecord> OnBuyOrderFulfilled
        {
            add => _events.BuyOrderFulfilled += value;
            remove => _events.BuyOrderFulfilled -= value;
        }

        public event Action<AuctionBuyOrderRecord> OnBuyOrderCancelled
        {
            add => _events.BuyOrderCancelled += value;
            remove => _events.BuyOrderCancelled -= value;
        }

        private void OnEnable()
            => _events.Bind(SocialHubService.Instance, () => GetAccountId(Owner));

        private void OnDisable()
            => _events.Unbind();

    }
}