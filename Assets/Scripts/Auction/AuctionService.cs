using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Server-side auction service: all mutations go through a bounded CAS retry loop so
    /// N zone servers can mutate shared auction state without conflicts. The actual
    /// gold/item transfer on purchase happens via a Nakama Lua RPC (atomic_transaction)
    /// which runs on the database server, ensuring atomicity.
    /// </summary>
    public sealed class AuctionService
    {
        public const int DefaultListingDurationHours = 48;
        public const int MaxActiveListingsPerPlayer = 20;
        public const long MinPricePerUnit = 1;

        private readonly IAuctionRepository _repository;
        private readonly NakamaRestClient _rest;
        private readonly int _casRetries;
        private readonly SocialHubService _socialHub;

        public AuctionService(IAuctionRepository repository, NakamaRestClient rest, SocialHubService socialHub, int casRetries = 5)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socialHub = socialHub ?? throw new ArgumentNullException(nameof(socialHub));
            _casRetries = casRetries;
        }

        /// <summary>Checks if the current zone has an auction house.</summary>
        private bool HasAuctionHouse()
        {
            return _socialHub != null && _socialHub.HasAuctionHouse;
        }

        /// <summary>Lists an item from the seller's bag on the auction house.</summary>
        public async Task<AuctionResult> CreateListingAsync(
            string sellerAccountId,
            int bagX,
            int bagY,
            long pricePerUnit,
            int stackCount = -1,
            int durationHours = DefaultListingDurationHours,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (pricePerUnit < MinPricePerUnit)
                return AuctionResult.Fail(AuctionRule.InvalidPrice);

            // Verify seller has the item and get the item instance
            PlayerInventory bag = GetPlayerBag(sellerAccountId);
            if (bag == null)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            int itemIndex = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                .FindAt(bag.Items, bagX, bagY);
            if (itemIndex < 0)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            ItemInstance item = bag.Items[itemIndex];
            if (item.IsEmpty)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            ItemDefinition def = ItemDefinitionRegistry.Resolve(item.ItemName);
            if (def == null)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            // Determine stack count to sell
            int availableCount = item.StackCount < 1 ? 1 : item.StackCount;
            int sellCount = stackCount > 0 ? Math.Min(stackCount, availableCount) : availableCount;
            if (sellCount < 1)
                return AuctionResult.Fail(AuctionRule.InvalidStackCount);

            // Check max active listings
            AuctionIndexRecord index = await _repository.LoadIndexAsync(sellerAccountId, ct)
                ?? new AuctionIndexRecord { AccountId = sellerAccountId };

            if (index.ActiveListingIds != null && index.ActiveListingIds.Count >= MaxActiveListingsPerPlayer)
                return AuctionResult.Fail(AuctionRule.MaxListingsReached);

            AuctionResult result = await RetryAsync(async () =>
            {
                // Re-read index inside CAS loop
                index = await _repository.LoadIndexAsync(sellerAccountId, ct)
                    ?? new AuctionIndexRecord { AccountId = sellerAccountId };

                if (index.ActiveListingIds != null && index.ActiveListingIds.Count >= MaxActiveListingsPerPlayer)
                    return AuctionResult.Fail(AuctionRule.MaxListingsReached);

                // Create the listing
                string listingId = Guid.NewGuid().ToString("N");
                long nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                long expiresTicks = nowTicks + TimeSpan.FromHours(durationHours).Ticks;

                var listing = new AuctionListingRecord
                {
                    ListingId = listingId,
                    SellerAccountId = sellerAccountId,
                    SellerPlayerName = _socialHub.GetPlayerName(sellerAccountId),
                    ItemName = item.ItemName,
                    ItemWidth = item.Width,
                    ItemHeight = item.Height,
                    StackCount = sellCount,
                    PricePerUnit = pricePerUnit,
                    ListedAtTicks = nowTicks,
                    ExpiresAtTicks = expiresTicks,
                    Status = AuctionListingStatus.Active,
                };

                await _repository.SaveListingAsync(listing, ct);

                // Update seller's index
                if (index.ActiveListingIds == null)
                    index.ActiveListingIds = new List<string>();
                index.ActiveListingIds.Add(listingId);
                await _repository.SaveIndexAsync(index, ct);

                return AuctionResult.Ok(listingId, listing);
            }, ct);

            if (result.Rule == AuctionRule.Ok)
            {
                // Remove item from seller's bag (only after listing is durable)
                if (sellCount >= availableCount)
                {
                    bag.RemoveItemAt(bagX, bagY);
                }
                else
                {
                    // Partial stack: reduce stack count
                    ItemInstance remaining = item;
                    remaining.StackCount = availableCount - sellCount;
                    bag.RemoveItemAt(bagX, bagY);
                    bag.TryAddExistingItem(remaining, bagX, bagY);
                }

                // Notify seller's client
                _socialHub.NotifyAuctionListingCreated(sellerAccountId, result.Listing);
            }

            return result;
        }

        /// <summary>Buys an item from the auction house. Uses atomic Lua RPC for gold/item transfer.</summary>
        public async Task<AuctionResult> BuyListingAsync(
            string buyerAccountId,
            string listingId,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            // Load listing to validate
            AuctionListingRecord listing = await _repository.LoadListingAsync(listingId, ct);
            if (listing == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (listing.Status != AuctionListingStatus.Active)
                return AuctionResult.Fail(AuctionRule.ListingNotActive);

            if (listing.ExpiresAtTicks > 0 && listing.ExpiresAtTicks < DateTimeOffset.UtcNow.UtcDateTime.Ticks)
                return AuctionResult.Fail(AuctionRule.ListingNotActive); // expired

            if (listing.SellerAccountId == buyerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner); // can't buy own listing

            // Call atomic Lua RPC to transfer gold and mark listing sold
            // The RPC returns the transaction record on success
            string payload = JsonUtility.ToJson(new BuyListingPayload
            {
                buyerAccountId = buyerAccountId,
                listingId = listingId,
            });

            try
            {
                string response = await _rest.CallRpcAsync<string>("auction_buy", payload, ct);
                if (string.IsNullOrEmpty(response))
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);

                BuyListingResponse buyResponse = JsonUtility.FromJson<BuyListingResponse>(response);
                if (buyResponse == null || !buyResponse.success)
                {
                    // Map error code to rule
                    return MapErrorToRule(buyResponse?.errorCode);
                }

                // Purchase successful - notify both parties
                AuctionListingRecord updatedListing = buyResponse.listing;
                AuctionTransactionRecord transaction = buyResponse.transaction;

                // Update buyer's index (add to purchase history)
                AuctionIndexRecord buyerIndex = await _repository.LoadIndexAsync(buyerAccountId, ct)
                    ?? new AuctionIndexRecord { AccountId = buyerAccountId };
                if (buyerIndex.PurchaseHistoryIds == null)
                    buyerIndex.PurchaseHistoryIds = new List<string>();
                buyerIndex.PurchaseHistoryIds.Add(transaction.TransactionId);
                await _repository.SaveIndexAsync(buyerIndex, ct);

                // Update seller's index (remove from active)
                AuctionIndexRecord sellerIndex = await _repository.LoadIndexAsync(listing.SellerAccountId, ct);
                if (sellerIndex != null && sellerIndex.ActiveListingIds != null)
                {
                    sellerIndex.ActiveListingIds.Remove(listingId);
                    await _repository.SaveIndexAsync(sellerIndex, ct);
                }

                // Notify clients
                _socialHub.NotifyAuctionListingSold(buyerAccountId, listing.SellerAccountId, updatedListing, transaction);
                _socialHub.NotifyAuctionPurchaseCompleted(buyerAccountId, transaction);

                return AuctionResult.Ok(listingId, updatedListing);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Buy RPC failed: {e.Message}");
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }
        }

        /// <summary>Cancels an active listing and returns the item to the seller's bag.</summary>
        public async Task<AuctionResult> CancelListingAsync(
            string sellerAccountId,
            string listingId,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            AuctionListingRecord listing = await _repository.LoadListingAsync(listingId, ct);
            if (listing == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (listing.SellerAccountId != sellerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner);

            if (listing.Status != AuctionListingStatus.Active)
                return AuctionResult.Fail(AuctionRule.ListingNotActive);

            AuctionResult result = await RetryAsync(async () =>
            {
                listing = await _repository.LoadListingAsync(listingId, ct);
                if (listing == null || listing.Status != AuctionListingStatus.Active)
                    return AuctionResult.Fail(AuctionRule.ListingNotActive);

                if (listing.SellerAccountId != sellerAccountId)
                    return AuctionResult.Fail(AuctionRule.NotOwner);

                // Mark as cancelled
                listing.Status = AuctionListingStatus.Cancelled;
                await _repository.SaveListingAsync(listing, ct);

                // Update seller's index
                AuctionIndexRecord index = await _repository.LoadIndexAsync(sellerAccountId, ct);
                if (index != null && index.ActiveListingIds != null)
                {
                    index.ActiveListingIds.Remove(listingId);
                    await _repository.SaveIndexAsync(index, ct);
                }

                return AuctionResult.Ok(listingId, listing);
            }, ct);

            if (result.Rule == AuctionRule.Ok)
            {
                // Return item to seller's bag
                PlayerInventory bag = GetPlayerBag(sellerAccountId);
                if (bag != null)
                {
                    ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
                    if (def != null)
                    {
                        ItemInstance item = ItemInstance.Create(def, 0, 0, listing.StackCount);
                        // Try to place at original position or find free space
                        if (!bag.TryAddExistingItem(item, 0, 0))
                        {
                            // Find free space
                            if (new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                                .TryFindFreeSpace(bag.Items, item.Width, item.Height, out int x, out int y))
                            {
                                item.GridX = x;
                                item.GridY = y;
                                bag.Add(item);
                            }
                            else
                            {
                                Debug.LogError($"[Auction] Seller bag full, could not return cancelled item for {sellerAccountId}");
                                // In this case, item is lost - but listing is cancelled
                                // Could send to a "mail" system in future
                            }
                        }
                    }
                }

                _socialHub.NotifyAuctionListingCancelled(sellerAccountId, listing);
            }

            return result;
        }

        /// <summary>Gets active listings for a player.</summary>
        public async Task<List<AuctionListingRecord>> GetMyListingsAsync(string accountId, CancellationToken ct = default)
        {
            if (!HasAuctionHouse())
                return new List<AuctionListingRecord>();

            AuctionIndexRecord index = await _repository.LoadIndexAsync(accountId, ct);
            if (index == null || index.ActiveListingIds == null || index.ActiveListingIds.Count == 0)
                return new List<AuctionListingRecord>();

            return await _repository.LoadListingsAsync(index.ActiveListingIds, ct);
        }

        /// <summary>Searches the auction house.</summary>
        public async Task<List<AuctionListingRecord>> SearchAsync(
            string itemNameFilter = null,
            long maxPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            if (!HasAuctionHouse())
                return new List<AuctionListingRecord>();

            return await _repository.SearchListingsAsync(itemNameFilter, maxPrice, page, pageSize, ct);
        }

        /// <summary>Gets purchase history for a player.</summary>
        public async Task<List<AuctionTransactionRecord>> GetPurchaseHistoryAsync(string accountId, int limit = 20, CancellationToken ct = default)
        {
            if (!HasAuctionHouse())
                return new List<AuctionTransactionRecord>();

            return await _repository.LoadTransactionHistoryAsync(accountId, limit, ct);
        }

        private PlayerInventory GetPlayerBag(string accountId)
        {
            if (!_socialHub.TryGetLocalPlayer(accountId, out PlayerGuild player) || player == null)
                return null;
            return player.Inventory;
        }

        private AuctionRule MapErrorToRule(string errorCode)
        {
            switch (errorCode)
            {
                case "INSUFFICIENT_FUNDS": return AuctionRule.InsufficientFunds;
                case "ALREADY_SOLD": return AuctionRule.AlreadySold;
                case "NOT_ACTIVE": return AuctionRule.ListingNotActive;
                case "NOT_FOUND": return AuctionRule.NotFound;
                default: return AuctionRule.BackendUnavailable;
            }
        }

        private async Task<AuctionResult> RetryAsync(Func<Task<AuctionResult>> attempt, CancellationToken ct)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await Task.Delay(50 * (1 << attempt), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Auction] Mutation failed: {e.Message}");
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }
            }
            return AuctionResult.Fail(AuctionRule.Conflict);
        }

        [Serializable]
        private class BuyListingPayload
        {
            public string buyerAccountId;
            public string listingId;
        }

        [Serializable]
        private class BuyListingResponse
        {
            public bool success;
            public string errorCode;
            public AuctionListingRecord listing;
            public AuctionTransactionRecord transaction;
        }
    }
}