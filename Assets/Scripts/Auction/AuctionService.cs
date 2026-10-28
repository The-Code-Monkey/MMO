using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly AuctionDeliveryService _delivery;
        private readonly AuctionQueryService _queries;
        private readonly AuctionWalletReservationService _reservations;

        public AuctionService(IAuctionRepository repository, NakamaRestClient rest, SocialHubService socialHub, int casRetries = 5)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socialHub = socialHub ?? throw new ArgumentNullException(nameof(socialHub));
            _delivery = new AuctionDeliveryService(_socialHub, GetPlayerBag);
            _queries = new AuctionQueryService(_repository, HasAuctionHouse);
            _reservations = new AuctionWalletReservationService(_repository, () => _rest.IsConnected);
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
                    SellerZoneId = _socialHub.ZoneId,
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

            // Reserve the buyer's gold up front so the live wallet and its save stay consistent
            // (the buy-order path reserves the same way). Refunded on any failure below.
            long totalPrice = listing.PricePerUnit * listing.StackCount;
            string attemptId = Guid.NewGuid().ToString("N");
            string deliveryBody = $"You won listing {listingId}. ({attemptId})";
            string payload = JsonUtility.ToJson(new BuyListingPayload
            {
                buyerAccountId = buyerAccountId,
                listingId = listingId,
                itemName = listing.ItemName,
                itemInstanceId = ItemInstance.NewInstanceId(),
                itemWidth = listing.ItemWidth,
                itemHeight = listing.ItemHeight,
                itemDurability = 0,
                attemptId = attemptId,
            });
            var reservation = new WalletReservationRecord
            {
                ReservationId = attemptId,
                TargetId = listingId,
                Kind = WalletReservationKind.AuctionPurchase,
                Amount = totalPrice,
            };

            PlayerWallet buyerWallet = GetPlayerWallet(buyerAccountId);
            if (buyerWallet == null)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!buyerWallet.TrySpend(totalPrice))
                return AuctionResult.Fail(AuctionRule.InsufficientFunds);

            if (!await AuctionWalletReservationService.PersistAsync(buyerAccountId, reservation))
            {
                await AuctionWalletReservationService.RefundAsync(buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }

            // Call the Lua RPC. It claims the listing and writes the buyer's delivery mail in the
            // same transaction, so the item is durable before the call returns. The attempt id
            // makes that mail findable if the response itself is lost.
            bool sellerCredited = false;

            try
            {
                string response = await _rest.CallRpcAsync<string>("auction_buy", payload, ct);
                if (string.IsNullOrEmpty(response))
                {
                    AuctionResult? recovered = await RecoverLostPurchaseAsync(
                        buyerAccountId, listingId, totalPrice, deliveryBody, sellerCredited);
                    if (recovered.HasValue)
                    {
                        await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservation.ReservationId);
                        return recovered.Value;
                    }

                    await AuctionWalletReservationService.RefundAsync(buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }

                BuyListingResponse buyResponse = JsonUtility.FromJson<BuyListingResponse>(response);
                if (buyResponse == null)
                {
                    AuctionResult? recovered = await RecoverLostPurchaseAsync(
                        buyerAccountId, listingId, totalPrice, deliveryBody, sellerCredited);
                    if (recovered.HasValue)
                    {
                        await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservation.ReservationId);
                        return recovered.Value;
                    }

                    await AuctionWalletReservationService.RefundAsync(buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }

                if (!buyResponse.success)
                {
                    await AuctionWalletReservationService.RefundAsync(buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                    // Map error code to rule
                    return AuctionResult.Fail(AuctionErrorRules.Map(buyResponse.errorCode));
                }

                await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservation.ReservationId);

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

                // Credit the seller. Same-zone sellers get it straight into their wallet;
                // cross-zone (or offline) sellers get claimable mail their own zone grants.
                await _delivery.CreditSellerAsync(listing.SellerAccountId, totalPrice, ct);
                sellerCredited = true;

                // The item is already durable in the buyer's mailbox, written atomically with the
                // claim. Claim it now so a local buyer with room gets it in the bag; otherwise the
                // mail sweep or the mailbox UI grants it later. This can never lose the item.
                if (!string.IsNullOrEmpty(buyResponse.deliveryMailId))
                {
                    await _socialHub.ClaimMailAsync(
                        buyerAccountId,
                        buyResponse.deliveryMailId,
                        CancellationToken.None);
                }
                else
                {
                    // Older backend without atomic delivery: fall back to the in-process path.
                    await _delivery.DeliverPurchaseAsync(buyerAccountId, updatedListing, ct);
                }

                return AuctionResult.Ok(listingId: listingId, listing: updatedListing);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Buy RPC failed: {e.Message}");

                AuctionResult? recovered = await RecoverLostPurchaseAsync(
                    buyerAccountId, listingId, totalPrice, deliveryBody, sellerCredited);
                if (recovered.HasValue)
                {
                    await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservation.ReservationId);
                    return recovered.Value;
                }

                await AuctionWalletReservationService.RefundAsync(buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }
        }

        /// <summary>
        /// Finishes a purchase whose RPC response was lost. The atomic delivery mail proves the
        /// claim committed; without it the caller refunds the buyer as before. Returns null when
        /// this attempt's delivery cannot be found.
        /// </summary>
        private async Task<AuctionResult?> RecoverLostPurchaseAsync(
            string buyerAccountId, string listingId, long totalPrice, string deliveryBody, bool sellerCredited)
        {
            try
            {
                string mailId = await _socialHub.FindMailIdByBodyAsync(buyerAccountId, deliveryBody);
                if (string.IsNullOrEmpty(mailId))
                    return null;

                AuctionListingRecord sold = await _repository.LoadListingAsync(listingId, CancellationToken.None);
                if (sold == null || sold.Status != AuctionListingStatus.Sold || sold.BuyerAccountId != buyerAccountId)
                    return null;

                await _socialHub.ClaimMailAsync(buyerAccountId, mailId, CancellationToken.None);
                if (!sellerCredited)
                    await _delivery.CreditSellerAsync(sold.SellerAccountId, totalPrice, CancellationToken.None);

                return AuctionResult.Ok(listingId: listingId, listing: sold);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Lost-purchase recovery failed for {listingId}: {e.Message}");
                return null;
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
        public Task<List<AuctionListingRecord>> GetMyListingsAsync(string accountId, CancellationToken ct = default)
            => _queries.GetMyListingsAsync(accountId, ct);

        /// <summary>Searches the auction house.</summary>
        public Task<List<AuctionListingRecord>> SearchAsync(
            string itemNameFilter = null,
            long maxPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
            => _queries.SearchAsync(itemNameFilter, maxPrice, page, pageSize, ct);

        /// <summary>
        /// Realm-wide search through the pure <see cref="AuctionSearch"/> kernel: every active
        /// listing across zones is mapped to a <see cref="SearchRow"/>, then filtered, sorted and
        /// paged server-side. Expiry is re-checked against the truth on purchase.
        /// </summary>
        public Task<List<SearchRow>> SearchAsync(AuctionQuery query, CancellationToken ct = default)
            => _queries.SearchAsync(query, ct);

        /// <summary>Gets purchase history for a player.</summary>
        public Task<List<AuctionTransactionRecord>> GetPurchaseHistoryAsync(
            string accountId, int limit = 20, CancellationToken ct = default)
            => _queries.GetPurchaseHistoryAsync(accountId, limit, ct);

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

            string buyOrderId = Guid.NewGuid().ToString("N");
            string reservationId = Guid.NewGuid().ToString("N");
            var reservation = new WalletReservationRecord
            {
                ReservationId = reservationId,
                TargetId = buyOrderId,
                Kind = WalletReservationKind.BuyOrder,
                Amount = totalCost,
            };

            // Reserve gold from buyer's wallet
            PlayerWallet wallet = GetPlayerWallet(buyerAccountId);
            if (wallet == null || !wallet.TrySpend(totalCost))
                return AuctionResult.Fail(AuctionRule.InsufficientFunds);

            if (!await AuctionWalletReservationService.PersistAsync(buyerAccountId, reservation))
            {
                await AuctionWalletReservationService.RefundAsync(buyerAccountId, wallet, totalCost, reservationId);
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }

            AuctionResult result;
            try
            {
                result = await RetryAsync(async () =>
                {
                    // Re-read index inside CAS loop
                    index = await _repository.LoadIndexAsync(buyerAccountId, ct)
                        ?? new AuctionIndexRecord { AccountId = buyerAccountId };

                    if (index.ActiveBuyOrderIds != null && index.ActiveBuyOrderIds.Count >= MaxActiveBuyOrdersPerPlayer)
                        return AuctionResult.Fail(AuctionRule.MaxBuyOrdersReached);

                    // Create the buy order
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
                }
                catch
                {
                    AuctionBuyOrderRecord persistedOrder = null;
                    bool resolved = false;
                    try
                    {
                        persistedOrder = await _repository.LoadBuyOrderAsync(buyOrderId, CancellationToken.None);
                        resolved = true;
                    }
                    catch
                    {
                        // Leave the durable reservation for join-time reconciliation.
                    }

                    if (resolved && persistedOrder != null && persistedOrder.BuyerAccountId == buyerAccountId)
                    {
                        result = AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: persistedOrder);
                    }
                    else if (resolved)
                    {
                        await AuctionWalletReservationService.RefundAsync(buyerAccountId, wallet, totalCost, reservationId);
                        throw;
                    }
                    else
                    {
                        throw;
                    }
                }

            if (result.Rule != AuctionRule.Ok)
            {
                AuctionBuyOrderRecord persistedOrder = null;
                bool resolved = false;
                try
                {
                    persistedOrder = await _repository.LoadBuyOrderAsync(buyOrderId, ct);
                    resolved = true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Auction] Could not verify buy order {buyOrderId} after reserve: {e.Message}");
                }

                if (resolved && persistedOrder != null && persistedOrder.BuyerAccountId == buyerAccountId)
                    result = AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: persistedOrder);
                else if (resolved)
                    await AuctionWalletReservationService.RefundAsync(buyerAccountId, wallet, totalCost, reservationId);
            }

            if (result.Rule == AuctionRule.Ok)
            {
                await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservationId);
                // Notify buyer's client
                _socialHub.NotifyAuctionBuyOrderCreated(buyerAccountId, result.BuyOrder);
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

            // The stack that actually changes hands. A full-stack sale keeps the seller's instance
            // identity; a split gets a fresh one.
            ItemInstance soldItem = item;
            soldItem.StackCount = sellCount;
            if (sellCount < availableCount)
                soldItem.InstanceId = ItemInstance.NewInstanceId();

            // Call the atomic Lua RPC: it claims the order, writes the buyer's delivery mail and
            // returns the mail id. The item is durable before this call returns. The attempt id
            // makes that mail findable if the response itself is lost.
            string attemptId = Guid.NewGuid().ToString("N");
            string deliveryBody = $"Your buy order {buyOrderId} was filled. ({attemptId})";
            long totalPrice = buyOrder.PricePerUnit * sellCount;
            string payload = JsonUtility.ToJson(new FulfillBuyOrderPayload
            {
                sellerAccountId = sellerAccountId,
                buyOrderId = buyOrderId,
                quantity = sellCount,
                itemName = soldItem.ItemName,
                itemInstanceId = string.IsNullOrEmpty(soldItem.InstanceId) ? ItemInstance.NewInstanceId() : soldItem.InstanceId,
                itemWidth = soldItem.Width,
                itemHeight = soldItem.Height,
                itemDurability = soldItem.Durability,
                attemptId = attemptId,
            });

            bool itemRemoved = false;
            bool sellerCredited = false;

            try
            {
                string response = await _rest.CallRpcAsync<string>("auction_fulfill_buy_order", payload, ct);
                if (string.IsNullOrEmpty(response))
                {
                    AuctionResult? recovered = await RecoverLostFulfillmentAsync(
                        sellerAccountId, buyOrderId, deliveryBody, item, bag, bagX, bagY, availableCount, sellCount, totalPrice, itemRemoved, sellerCredited);
                    if (recovered.HasValue)
                        return recovered.Value;

                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }

                FulfillBuyOrderResponse fulfillResponse = JsonUtility.FromJson<FulfillBuyOrderResponse>(response);
                if (fulfillResponse == null)
                {
                    AuctionResult? recovered = await RecoverLostFulfillmentAsync(
                        sellerAccountId, buyOrderId, deliveryBody, item, bag, bagX, bagY, availableCount, sellCount, totalPrice, itemRemoved, sellerCredited);
                    if (recovered.HasValue)
                        return recovered.Value;

                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }

                if (!fulfillResponse.success)
                {
                    return AuctionResult.Fail(AuctionErrorRules.Map(fulfillResponse.errorCode));
                }

                // The item is already durable in the buyer's mailbox, written atomically with the
                // claim. Claim it now so a local buyer with room gets it in the bag; otherwise the
                // mail sweep or the mailbox UI grants it later. This can never lose the item.
                if (!string.IsNullOrEmpty(fulfillResponse.deliveryMailId))
                {
                    await _socialHub.ClaimMailAsync(
                        buyOrder.BuyerAccountId,
                        fulfillResponse.deliveryMailId,
                        CancellationToken.None);
                }
                else
                {
                    // Older backend without atomic delivery: fall back to the in-process path.
                    await _delivery.DeliverItemAsync(
                        buyOrder.BuyerAccountId,
                        soldItem,
                        _delivery.ResolveDeliveryTarget(buyOrder.BuyerAccountId, soldItem),
                        "Auction purchase",
                        $"Your buy order {buyOrderId} was filled.");
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

                itemRemoved = true;

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

                // Credit the seller the buyer's reserved gold (reserved at CreateBuyOrderAsync).
                await _delivery.CreditSellerAsync(sellerAccountId, buyOrder.PricePerUnit * sellCount, ct);
                sellerCredited = true;

                return AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: fulfillResponse.buyOrder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Fulfill buy order RPC failed: {e.Message}");

                AuctionResult? recovered = await RecoverLostFulfillmentAsync(
                    sellerAccountId, buyOrderId, deliveryBody, item, bag, bagX, bagY, availableCount, sellCount, totalPrice, itemRemoved, sellerCredited);
                if (recovered.HasValue)
                    return recovered.Value;

                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }
        }

        /// <summary>
        /// Finishes a fulfillment whose RPC response was lost. The attempt-scoped delivery mail
        /// proves the claim committed; the seller's item and credit are settled from that proof.
        /// Returns null when this attempt's delivery cannot be found.
        /// </summary>
        private async Task<AuctionResult?> RecoverLostFulfillmentAsync(
            string sellerAccountId, string buyOrderId, string deliveryBody, ItemInstance originalItem,
            PlayerInventory bag, int bagX, int bagY, int availableCount, int sellCount, long totalPrice,
            bool itemRemoved, bool sellerCredited)
        {
            try
            {
                AuctionBuyOrderRecord current = await _repository.LoadBuyOrderAsync(buyOrderId, CancellationToken.None);
                if (current == null)
                    return null;

                string mailId = await _socialHub.FindMailIdByBodyAsync(current.BuyerAccountId, deliveryBody);
                if (string.IsNullOrEmpty(mailId))
                    return null;

                await _socialHub.ClaimMailAsync(current.BuyerAccountId, mailId, CancellationToken.None);

                bool stillHeld = false;
                if (bag != null)
                {
                    int index = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                        .FindAt(bag.Items, bagX, bagY);
                    stillHeld = index >= 0 && bag.Items[index].ItemName == originalItem.ItemName;
                }

                if (!itemRemoved && stillHeld)
                {
                    if (sellCount >= availableCount)
                    {
                        bag.RemoveItemAt(bagX, bagY);
                    }
                    else
                    {
                        ItemInstance remaining = originalItem;
                        remaining.StackCount = availableCount - sellCount;
                        bag.RemoveItemAt(bagX, bagY);
                        bag.TryAddExistingItem(remaining, bagX, bagY);
                    }
                }

                if (!sellerCredited)
                    await _delivery.CreditSellerAsync(sellerAccountId, totalPrice, CancellationToken.None);
                return AuctionResult.Ok(buyOrderId: buyOrderId, buyOrder: current);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Lost-fulfillment recovery failed for {buyOrderId}: {e.Message}");
                return null;
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
        public Task<List<AuctionBuyOrderRecord>> GetMyBuyOrdersAsync(string accountId, CancellationToken ct = default)
            => _queries.GetMyBuyOrdersAsync(accountId, ct);

        /// <summary>Searches buy orders.</summary>
        public Task<List<AuctionBuyOrderRecord>> SearchBuyOrdersAsync(
            string itemNameFilter = null,
            long minPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
            => _queries.SearchBuyOrdersAsync(itemNameFilter, minPrice, page, pageSize, ct);

        private PlayerWallet GetPlayerWallet(string accountId)
        {
            if (!_socialHub.TryGetWallet(accountId, out PlayerWallet wallet))
                return null;
            return wallet;
        }

        /// <summary>Refunds an uncommitted reservation after restart, or clears a committed one.</summary>
        public Task<bool> ReconcileWalletReservationAsync(
            string accountId, PlayerWallet wallet, WalletReservationRecord reservation)
            => _reservations.ReconcileAsync(accountId, wallet, reservation);

        private PlayerInventory GetPlayerBag(string accountId)
        {
            if (!_socialHub.TryGetLocalPlayer(accountId, out PlayerGuild player) || player == null)
                return null;
            return player.Inventory;
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
            public string itemName;
            public string itemInstanceId;
            public int itemWidth;
            public int itemHeight;
            public int itemDurability;
            public string attemptId;
        }

        [Serializable]
        private class BuyListingResponse
        {
            public bool success;
            public string errorCode;
            public AuctionListingRecord listing;
            public AuctionTransactionRecord transaction;
            public string deliveryMailId;
        }

        [Serializable]
        private class FulfillBuyOrderPayload
        {
            public string sellerAccountId;
            public string buyOrderId;
            public int quantity;
            public string itemName;
            public string itemInstanceId;
            public int itemWidth;
            public int itemHeight;
            public int itemDurability;
            public string attemptId;
        }

        [Serializable]
        private class FulfillBuyOrderResponse
        {
            public bool success;
            public string errorCode;
            public AuctionBuyOrderRecord buyOrder;
            public AuctionTransactionRecord transaction;
            public string deliveryMailId;
        }
    }
}
