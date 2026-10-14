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

        private async System.Threading.Tasks.Task SearchAndRespondAsync(string accountId, string itemNameFilter, long maxPrice, int page, int pageSize, NetworkConnection sender)
        {
            List<AuctionListingRecord> results = await SocialHubService.Instance.Auction.SearchAsync(itemNameFilter, maxPrice, page, pageSize);
            string json = JsonUtility.ToJson(new AuctionSearchResults { listings = results });
            TargetRpcSearchResults(json, sender);
        }

        private async System.Threading.Tasks.Task MyListingsAndRespondAsync(string accountId, NetworkConnection sender)
        {
            List<AuctionListingRecord> results = await SocialHubService.Instance.Auction.GetMyListingsAsync(accountId);
            string json = JsonUtility.ToJson(new AuctionSearchResults { listings = results });
            TargetRpcMyListings(json, sender);
        }

        private async System.Threading.Tasks.Task PurchaseHistoryAndRespondAsync(string accountId, int limit, NetworkConnection sender)
        {
            List<AuctionTransactionRecord> results = await SocialHubService.Instance.Auction.GetPurchaseHistoryAsync(accountId, limit);
            string json = JsonUtility.ToJson(new AuctionTransactionHistory { transactions = results });
            TargetRpcPurchaseHistory(json, sender);
        }

        [TargetRpc]
        private void TargetRpcSearchResults(string json, NetworkConnection target)
        {
            if (!IsOwner)
                return;

            AuctionSearchResults results = JsonUtility.FromJson<AuctionSearchResults>(json);
            OnSearchResultsReceived?.Invoke(results?.listings ?? new List<AuctionListingRecord>());
        }

        [TargetRpc]
        private void TargetRpcMyListings(string json, NetworkConnection target)
        {
            if (!IsOwner)
                return;

            AuctionSearchResults results = JsonUtility.FromJson<AuctionSearchResults>(json);
            OnMyListingsReceived?.Invoke(results?.listings ?? new List<AuctionListingRecord>());
        }

        [TargetRpc]
        private void TargetRpcPurchaseHistory(string json, NetworkConnection target)
        {
            if (!IsOwner)
                return;

            AuctionTransactionHistory history = JsonUtility.FromJson<AuctionTransactionHistory>(json);
            OnPurchaseHistoryReceived?.Invoke(history?.transactions ?? new List<AuctionTransactionRecord>());
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

        // Events from server (via SocialHubService)
        public event Action<AuctionListingRecord> OnListingCreated;
        public event Action<AuctionListingRecord, AuctionTransactionRecord> OnListingBought;
        public event Action<AuctionListingRecord> OnListingCancelled;
        public event Action<AuctionTransactionRecord> OnPurchaseCompleted;

        private void OnEnable()
        {
            if (SocialHubService.Instance != null)
            {
                SocialHubService.Instance.AuctionListingCreated += OnAuctionListingCreated;
                SocialHubService.Instance.AuctionListingSold += OnAuctionListingSold;
                SocialHubService.Instance.AuctionListingCancelled += OnAuctionListingCancelled;
                SocialHubService.Instance.AuctionPurchaseCompleted += OnAuctionPurchaseCompleted;
            }
        }

        private void OnDisable()
        {
            if (SocialHubService.Instance != null)
            {
                SocialHubService.Instance.AuctionListingCreated -= OnAuctionListingCreated;
                SocialHubService.Instance.AuctionListingSold -= OnAuctionListingSold;
                SocialHubService.Instance.AuctionListingCancelled -= OnAuctionListingCancelled;
                SocialHubService.Instance.AuctionPurchaseCompleted -= OnAuctionPurchaseCompleted;
            }
        }

        private void OnAuctionListingCreated(string accountId, AuctionListingRecord listing)
        {
            if (IsOwner && GetAccountId(Owner) == accountId)
                OnListingCreated?.Invoke(listing);
        }

        private void OnAuctionListingSold(string buyerAccountId, string sellerAccountId, AuctionListingRecord listing, AuctionTransactionRecord transaction)
        {
            string myAccountId = GetAccountId(Owner);
            if (IsOwner && (myAccountId == buyerAccountId || myAccountId == sellerAccountId))
                OnListingBought?.Invoke(listing, transaction);
        }

        private void OnAuctionListingCancelled(string sellerAccountId, AuctionListingRecord listing)
        {
            if (IsOwner && GetAccountId(Owner) == sellerAccountId)
                OnListingCancelled?.Invoke(listing);
        }

        private void OnAuctionPurchaseCompleted(string buyerAccountId, AuctionTransactionRecord transaction)
        {
            if (IsOwner && GetAccountId(Owner) == buyerAccountId)
                OnPurchaseCompleted?.Invoke(transaction);
        }

        [Serializable]
        private class AuctionSearchResults
        {
            public List<AuctionListingRecord> listings;
        }

        [Serializable]
        private class AuctionTransactionHistory
        {
            public List<AuctionTransactionRecord> transactions;
        }
    }
}