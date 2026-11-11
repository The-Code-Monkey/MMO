using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>Read-only auction queries: player indexes, legacy search and realm-wide search.</summary>
    internal sealed class AuctionQueryService
    {
        private readonly IAuctionRepository _repository;
        private readonly MarketIndexService _marketIndex;
        private readonly Func<bool> _hasAuctionHouse;

        public AuctionQueryService(
            IAuctionRepository repository, MarketIndexService marketIndex, Func<bool> hasAuctionHouse)
        {
            _repository = repository;
            _marketIndex = marketIndex;
            _hasAuctionHouse = hasAuctionHouse;
        }

        public async Task<List<AuctionListingRecord>> GetMyListingsAsync(string accountId, CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<AuctionListingRecord>();

            AuctionIndexRecord index = await _repository.LoadIndexAsync(accountId, ct);
            if (index == null || index.ActiveListingIds == null || index.ActiveListingIds.Count == 0)
                return new List<AuctionListingRecord>();

            return await _repository.LoadListingsAsync(index.ActiveListingIds, ct);
        }

        public async Task<List<AuctionListingRecord>> SearchAsync(
            string itemNameFilter = null,
            long maxPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<AuctionListingRecord>();

            return await _repository.SearchListingsAsync(itemNameFilter, maxPrice, page, pageSize, ct);
        }

        /// <summary>
        /// Realm-wide search through the pure <see cref="AuctionSearch"/> kernel, served from the
        /// <see cref="MarketSearchIndex"/> cache. The kernel filters, sorts and pages server-side;
        /// expiry is re-checked against the truth on purchase.
        /// </summary>
        public async Task<List<SearchRow>> SearchAsync(AuctionQuery query, CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<SearchRow>();

            MarketSearchIndex index = await _marketIndex.EnsureAsync(ct);
            long now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
            return AuctionSearch.Apply(index.Rows, query ?? new AuctionQuery(), now).ToList();
        }

        public async Task<List<AuctionTransactionRecord>> GetPurchaseHistoryAsync(
            string accountId, int limit = 20, CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<AuctionTransactionRecord>();

            return await _repository.LoadTransactionHistoryAsync(accountId, limit, ct);
        }

        public async Task<List<AuctionBuyOrderRecord>> GetMyBuyOrdersAsync(string accountId, CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<AuctionBuyOrderRecord>();

            AuctionIndexRecord index = await _repository.LoadIndexAsync(accountId, ct);
            if (index == null || index.ActiveBuyOrderIds == null || index.ActiveBuyOrderIds.Count == 0)
                return new List<AuctionBuyOrderRecord>();

            return await _repository.LoadBuyOrdersAsync(index.ActiveBuyOrderIds, ct);
        }

        public async Task<List<AuctionBuyOrderRecord>> SearchBuyOrdersAsync(
            string itemNameFilter = null,
            long minPrice = 0,
            int page = 0,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            if (!_hasAuctionHouse())
                return new List<AuctionBuyOrderRecord>();

            return await _repository.SearchBuyOrdersAsync(itemNameFilter, minPrice, page, pageSize, ct);
        }
    }
}
