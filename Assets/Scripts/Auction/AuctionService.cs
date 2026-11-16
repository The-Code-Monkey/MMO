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
        private readonly SocialHubService _socialHub;
        private readonly AuctionDeliveryService _delivery;
        private readonly AuctionQueryService _queries;
        private readonly MarketIndexService _marketIndex;
        private readonly AuctionWalletReservationService _reservations;
        private readonly AuctionListingService _listings;
        private readonly AuctionBuyOrderService _buyOrders;
        private readonly AuctionPurchaseService _purchases;

        public AuctionService(IAuctionRepository repository, NakamaRestClient rest, SocialHubService socialHub, int casRetries = 5)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
            _socialHub = socialHub ?? throw new ArgumentNullException(nameof(socialHub));
            _delivery = new AuctionDeliveryService(_socialHub, GetPlayerBag);
            _marketIndex = new MarketIndexService(_repository, () => _rest.IsConnected, casRetries);
            _queries = new AuctionQueryService(_repository, _marketIndex, HasAuctionHouse);
            _reservations = new AuctionWalletReservationService(_repository, () => _rest.IsConnected);
            _listings = new AuctionListingService(
                _repository, _socialHub, _marketIndex, GetPlayerBag, () => _rest.IsConnected, HasAuctionHouse, casRetries);
            _buyOrders = new AuctionBuyOrderService(
                _repository, _rest, _socialHub, _delivery, GetPlayerWallet, GetPlayerBag, HasAuctionHouse, casRetries);
            _purchases = new AuctionPurchaseService(
                _repository, _rest, _socialHub, _delivery, _marketIndex, GetPlayerWallet, HasAuctionHouse);
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
        public Task<AuctionResult> BuyListingAsync(
            string buyerAccountId,
            string listingId,
            CancellationToken ct = default)
            => _purchases.BuyAsync(buyerAccountId, listingId, ct);

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

        /// <summary>Re-reads one listing after a MarketChanged hint from a peer zone.</summary>
        public Task RefreshSearchRowAsync(string listingId)
            => HasAuctionHouse()
                ? _marketIndex.RefreshAsync(listingId, CancellationToken.None)
                : Task.CompletedTask;

        /// <summary>Recomputes the whole search cache from the authoritative listings.</summary>
        public Task<MarketSearchIndex> ReconcileSearchIndexAsync(CancellationToken ct = default)
            => _marketIndex.ReconcileAsync(ct);

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

    }
}
