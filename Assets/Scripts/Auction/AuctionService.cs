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
        public const int MaxActiveBuyOrdersPerPlayer = 20;
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

                return AuctionResult.Ok(listingId: listingId, listing: listing);
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
                    return AuctionResult.Fail(MapErrorToRule(buyResponse?.errorCode));
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

                return AuctionResult.Ok(listingId: listingId, listing: updatedListing);
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

                return AuctionResult.Ok(listingId: listingId, listing: listing);
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
                                bag.TryAddExistingItem(item, x, y);
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

        /// <summary>Creates a "want to buy" order. Reserves gold from buyer's wallet.</summary>
        public async Task<AuctionResult> CreateBuyOrderAsync(
            string buyerAccountId,
            string itemName,
            int quantity,
            long pricePerUnit,
            int durationHours = DefaultListingDurationHours,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (pricePerUnit < MinPricePerUnit)
                return AuctionResult.Fail(AuctionRule.InvalidPrice);

            if (quantity < 1)
                return AuctionResult.Fail(AuctionRule.InvalidStackCount);

            ItemDefinition def = ItemDefinitionRegistry.Resolve(itemName);
            if (def == null)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            long totalCost = pricePerUnit * quantity;

            // Check max active buy orders
            AuctionIndexRecord index = await _repository.LoadIndexAsync(buyerAccountId, ct)
                ?? new AuctionIndexRecord { AccountId = buyerAccountId };

            if (index.ActiveBuyOrderIds != null && index.ActiveBuyOrderIds.Count >= MaxActiveBuyOrdersPerPlayer)
                return AuctionResult.Fail(AuctionRule.MaxBuyOrdersReached);

            // Reserve gold from buyer's wallet
            PlayerWallet wallet = GetPlayerWallet(buyerAccountId);
            if (wallet == null || !wallet.TrySpend(totalCost))
                return AuctionResult.Fail(AuctionRule.InsufficientFunds);

            AuctionResult result = await RetryAsync(async () =>
            {
                // Re-read index inside CAS loop
                index = await _repository.LoadIndexAsync(buyerAccountId, ct)
                    ?? new AuctionIndexRecord { AccountId = buyerAccountId };

                if (index.ActiveBuyOrderIds != null && index.ActiveBuyOrderIds.Count >= MaxActiveBuyOrdersPerPlayer)
                    return AuctionResult.Fail(AuctionRule.MaxBuyOrdersReached);

                // Create the buy order
                string buyOrderId = Guid.NewGuid().ToString("N");
                long nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                long expiresTicks = nowTicks + TimeSpan.FromHours(durationHours).Ticks;

                var buyOrder = new AuctionBuyOrderRecord
                {
                    BuyOrderId = buyOrderId,
                    BuyerAccountId = buyerAccountId,
                    BuyerPlayerName = _socialHub.GetPlayerName(buyerAccountId),
                    ItemName = itemName,
                    ItemWidth = def.GridWidth,
                    ItemHeight = def.GridHeight,
                    QuantityRemaining = quantity,
                    QuantityTotal = quantity,
                    PricePerUnit = pricePerUnit,
                    ListedAtTicks = nowTicks,
                    ExpiresAtTicks = expiresTicks,
                    Status = AuctionBuyOrderStatus.Active,
                };

                await _repository.SaveBuyOrderAsync(buyOrder, ct);

                // Update buyer's index
                if (index.ActiveBuyOrderIds == null)
                    index.ActiveBuyOrderIds = new List<string>();
                index.ActiveBuyOrderIds.Add(buyOrderId);
                await _repository.SaveIndexAsync(index, ct);

                return AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: buyOrder);
            }, ct);

            if (result.Rule == AuctionRule.Ok)
            {
                // Notify buyer's client
                _socialHub.NotifyAuctionBuyOrderCreated(buyerAccountId, result.BuyOrder);
            }
            else
            {
                // Refund gold if buy order creation failed
                wallet?.AddGold(totalCost);
            }

            return result;
        }

        /// <summary>Fulfills a buy order - seller sells items to the buy order.</summary>
        public async Task<AuctionResult> FulfillBuyOrderAsync(
            string sellerAccountId,
            string buyOrderId,
            int bagX,
            int bagY,
            int stackCount = -1,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            // Load buy order to validate
            AuctionBuyOrderRecord buyOrder = await _repository.LoadBuyOrderAsync(buyOrderId, ct);
            if (buyOrder == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (buyOrder.Status != AuctionBuyOrderStatus.Active && buyOrder.Status != AuctionBuyOrderStatus.PartiallyFilled)
                return AuctionResult.Fail(AuctionRule.BuyOrderNotActive);

            if (buyOrder.ExpiresAtTicks > 0 && buyOrder.ExpiresAtTicks < DateTimeOffset.UtcNow.UtcDateTime.Ticks)
                return AuctionResult.Fail(AuctionRule.BuyOrderNotActive); // expired

            if (buyOrder.BuyerAccountId == sellerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner); // can't fulfill own buy order

            // Verify seller has the item
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

            if (item.ItemName != buyOrder.ItemName)
                return AuctionResult.Fail(AuctionRule.ItemNotFound); // item doesn't match buy order

            ItemDefinition def = ItemDefinitionRegistry.Resolve(item.ItemName);
            if (def == null)
                return AuctionResult.Fail(AuctionRule.ItemNotFound);

            // Determine quantity to sell
            int availableCount = item.StackCount < 1 ? 1 : item.StackCount;
            int sellCount = stackCount > 0 ? Math.Min(stackCount, availableCount) : availableCount;
            sellCount = Math.Min(sellCount, buyOrder.QuantityRemaining);
            if (sellCount < 1)
                return AuctionResult.Fail(AuctionRule.InvalidStackCount);

            // Call atomic Lua RPC to transfer item and gold
            string payload = JsonUtility.ToJson(new FulfillBuyOrderPayload
            {
                sellerAccountId = sellerAccountId,
                buyOrderId = buyOrderId,
                quantity = sellCount,
            });

            try
            {
                string response = await _rest.CallRpcAsync<string>("auction_fulfill_buy_order", payload, ct);
                if (string.IsNullOrEmpty(response))
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);

                FulfillBuyOrderResponse fulfillResponse = JsonUtility.FromJson<FulfillBuyOrderResponse>(response);
                if (fulfillResponse == null || !fulfillResponse.success)
                {
                    return AuctionResult.Fail(MapErrorToRule(fulfillResponse?.errorCode));
                }

                // Update seller's bag
                if (sellCount >= availableCount)
                {
                    bag.RemoveItemAt(bagX, bagY);
                }
                else
                {
                    ItemInstance remaining = item;
                    remaining.StackCount = availableCount - sellCount;
                    bag.RemoveItemAt(bagX, bagY);
                    bag.TryAddExistingItem(remaining, bagX, bagY);
                }

                // Update indexes
                AuctionIndexRecord buyerIndex = await _repository.LoadIndexAsync(buyOrder.BuyerAccountId, ct);
                if (buyerIndex != null && buyerIndex.ActiveBuyOrderIds != null)
                {
                    if (fulfillResponse.buyOrder.Status == AuctionBuyOrderStatus.Filled)
                    {
                        buyerIndex.ActiveBuyOrderIds.Remove(buyOrderId);
                    }
                    await _repository.SaveIndexAsync(buyerIndex, ct);
                }

                AuctionIndexRecord sellerIndex = await _repository.LoadIndexAsync(sellerAccountId, ct);
                if (sellerIndex != null && sellerIndex.PurchaseHistoryIds == null)
                    sellerIndex.PurchaseHistoryIds = new List<string>();

                // Add to purchase history for both parties
                if (buyerIndex != null)
                {
                    buyerIndex.PurchaseHistoryIds.Add(fulfillResponse.transaction.TransactionId);
                    await _repository.SaveIndexAsync(buyerIndex, ct);
                }
                if (sellerIndex != null)
                {
                    sellerIndex.PurchaseHistoryIds.Add(fulfillResponse.transaction.TransactionId);
                    await _repository.SaveIndexAsync(sellerIndex, ct);
                }

                // Notify clients
                _socialHub.NotifyAuctionBuyOrderFulfilled(sellerAccountId, buyOrder.BuyerAccountId, fulfillResponse.buyOrder, fulfillResponse.transaction);
                _socialHub.NotifyAuctionPurchaseCompleted(sellerAccountId, fulfillResponse.transaction);
                _socialHub.NotifyAuctionPurchaseCompleted(buyOrder.BuyerAccountId, fulfillResponse.transaction);

                return AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: fulfillResponse.buyOrder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Fulfill buy order RPC failed: {e.Message}");
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }
        }

        /// <summary>Cancels a buy order and returns reserved gold to buyer.</summary>
        public async Task<AuctionResult> CancelBuyOrderAsync(
            string buyerAccountId,
            string buyOrderId,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!HasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            AuctionBuyOrderRecord buyOrder = await _repository.LoadBuyOrderAsync(buyOrderId, ct);
            if (buyOrder == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (buyOrder.BuyerAccountId != buyerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner);

            if (buyOrder.Status != AuctionBuyOrderStatus.Active && buyOrder.Status != AuctionBuyOrderStatus.PartiallyFilled)
                return AuctionResult.Fail(AuctionRule.BuyOrderNotActive);

            long refundAmount = buyOrder.PricePerUnit * buyOrder.QuantityRemaining;

            AuctionResult result = await RetryAsync(async () =>
            {
                buyOrder = await _repository.LoadBuyOrderAsync(buyOrderId, ct);
                if (buyOrder == null || (buyOrder.Status != AuctionBuyOrderStatus.Active && buyOrder.Status != AuctionBuyOrderStatus.PartiallyFilled))
                    return AuctionResult.Fail(AuctionRule.BuyOrderNotActive);

                if (buyOrder.BuyerAccountId != buyerAccountId)
                    return AuctionResult.Fail(AuctionRule.NotOwner);

                // Mark as cancelled
                buyOrder.Status = AuctionBuyOrderStatus.Cancelled;
                await _repository.SaveBuyOrderAsync(buyOrder, ct);

                // Update buyer's index
                AuctionIndexRecord index = await _repository.LoadIndexAsync(buyerAccountId, ct);
                if (index != null && index.ActiveBuyOrderIds != null)
                {
                    index.ActiveBuyOrderIds.Remove(buyOrderId);
                    await _repository.SaveIndexAsync(index, ct);
                }

                return AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: buyOrder);
            }, ct);

            if (result.Rule == AuctionRule.Ok)
            {
                // Refund reserved gold
                PlayerWallet wallet = GetPlayerWallet(buyerAccountId);
                wallet?.AddGold(refundAmount);

                _socialHub.NotifyAuctionBuyOrderCancelled(buyerAccountId, buyOrder);
            }

            return result;
        }

        /// <summary>Gets active buy orders for a player.</summary>
        public async Task<List<AuctionBuyOrderRecord>> GetMyBuyOrdersAsync(string accountId, CancellationToken ct = default)
        {
            if (!HasAuctionHouse())
                return new List<AuctionBuyOrderRecord>();

            AuctionIndexRecord index = await _repository.LoadIndexAsync(accountId, ct);
            if (index == null || index.ActiveBuyOrderIds == null || index.ActiveBuyOrderIds.Count == 0)
                return new List<AuctionBuyOrderRecord>();

            return await _repository.LoadBuyOrdersAsync(index.ActiveBuyOrderIds, ct);
        }

        /// <summary>Searches buy orders.</summary>
        public async Task<List<AuctionBuyOrderRecord>> SearchBuyOrdersAsync(
            string itemNameFilter = null,
            long minPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            if (!HasAuctionHouse())
                return new List<AuctionBuyOrderRecord>();

            return await _repository.SearchBuyOrdersAsync(itemNameFilter, minPrice, page, pageSize, ct);
        }

        private PlayerWallet GetPlayerWallet(string accountId)
        {
            if (!_socialHub.TryGetWallet(accountId, out PlayerWallet wallet))
                return null;
            return wallet;
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
                case "BUY_ORDER_NOT_ACTIVE": return AuctionRule.BuyOrderNotActive;
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
                    await Task.Delay(50 * (1 << i), ct);
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

        [Serializable]
        private class FulfillBuyOrderPayload
        {
            public string sellerAccountId;
            public string buyOrderId;
            public int quantity;
        }

        [Serializable]
        private class FulfillBuyOrderResponse
        {
            public bool success;
            public string errorCode;
            public AuctionBuyOrderRecord buyOrder;
            public AuctionTransactionRecord transaction;
        }
    }
}