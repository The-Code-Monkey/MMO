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
        private readonly AuctionListingService _listings;
        private readonly AuctionBuyOrderService _buyOrders;

        public AuctionService(IAuctionRepository repository, NakamaRestClient rest, SocialHubService socialHub, int casRetries = 5)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socialHub = socialHub ?? throw new ArgumentNullException(nameof(socialHub));
            _delivery = new AuctionDeliveryService(_socialHub, GetPlayerBag);
            _queries = new AuctionQueryService(_repository, HasAuctionHouse);
            _reservations = new AuctionWalletReservationService(_repository, () => _rest.IsConnected);
            _listings = new AuctionListingService(
                _repository, _socialHub, GetPlayerBag, () => _rest.IsConnected, HasAuctionHouse, casRetries);
            _buyOrders = new AuctionBuyOrderService(
                _repository, _rest, _socialHub, _delivery, GetPlayerWallet, GetPlayerBag, HasAuctionHouse, casRetries);
            _casRetries = casRetries;
        }

        /// <summary>Checks if the current zone has an auction house.</summary>
        private bool HasAuctionHouse()
        {
            return _socialHub != null && _socialHub.HasAuctionHouse;
        }

        /// <summary>Lists an item from the seller's bag on the auction house.</summary>
        public Task<AuctionResult> CreateListingAsync(
            string sellerAccountId,
            int bagX,
            int bagY,
            long pricePerUnit,
            int stackCount = -1,
            int durationHours = DefaultListingDurationHours,
            CancellationToken ct = default)
            => _listings.CreateAsync(
                sellerAccountId, bagX, bagY, pricePerUnit, stackCount, durationHours, ct);

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
        public Task<AuctionResult> CancelListingAsync(
            string sellerAccountId,
            string listingId,
            CancellationToken ct = default)
            => _listings.CancelAsync(sellerAccountId, listingId, ct);

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
        public Task<AuctionResult> CreateBuyOrderAsync(
            string buyerAccountId,
            string itemName,
            int quantity,
            long pricePerUnit,
            int durationHours = DefaultListingDurationHours,
            CancellationToken ct = default)
            => _buyOrders.CreateAsync(buyerAccountId, itemName, quantity, pricePerUnit, durationHours, ct);

        /// <summary>Fulfills a buy order - seller sells items to the buy order.</summary>
        public Task<AuctionResult> FulfillBuyOrderAsync(
            string sellerAccountId,
            string buyOrderId,
            int bagX,
            int bagY,
            int stackCount = -1,
            CancellationToken ct = default)
            => _buyOrders.FulfillAsync(sellerAccountId, buyOrderId, bagX, bagY, stackCount, ct);

        /// <summary>Cancels a buy order and returns reserved gold to the buyer.</summary>
        public Task<AuctionResult> CancelBuyOrderAsync(
            string buyerAccountId,
            string buyOrderId,
            CancellationToken ct = default)
            => _buyOrders.CancelAsync(buyerAccountId, buyOrderId, ct);

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

    }
}
