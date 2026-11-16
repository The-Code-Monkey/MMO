using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;

namespace MMO.Auction
{
    public interface IAuctionRepository
    {
        Task<AuctionListingRecord> LoadListingAsync(string listingId, CancellationToken ct = default);
        Task SaveListingAsync(AuctionListingRecord record, CancellationToken ct = default);
        Task DeleteListingAsync(string listingId, CancellationToken ct = default);
        Task<List<AuctionListingRecord>> LoadListingsAsync(IReadOnlyList<string> listingIds, CancellationToken ct = default);
        Task<List<AuctionListingRecord>> SearchListingsAsync(string itemNameFilter = null, long maxPrice = 0, int page = 0, int pageSize = 50, CancellationToken ct = default);

        /// <summary>Every active, unexpired listing across all zones; the search kernel filters it.</summary>
        Task<List<AuctionListingRecord>> ListActiveListingsAsync(CancellationToken ct = default);

        /// <summary>The realm-wide search cache, or null when it has never been built.</summary>
        Task<MarketSearchIndex> LoadSearchIndexAsync(CancellationToken ct = default);

        Task SaveSearchIndexAsync(MarketSearchIndex index, CancellationToken ct = default);

        Task<AuctionBuyOrderRecord> LoadBuyOrderAsync(string buyOrderId, CancellationToken ct = default);
        Task SaveBuyOrderAsync(AuctionBuyOrderRecord record, CancellationToken ct = default);
        Task DeleteBuyOrderAsync(string buyOrderId, CancellationToken ct = default);
        Task<List<AuctionBuyOrderRecord>> LoadBuyOrdersAsync(IReadOnlyList<string> buyOrderIds, CancellationToken ct = default);
        Task<List<AuctionBuyOrderRecord>> SearchBuyOrdersAsync(string itemNameFilter = null, long minPrice = 0, int page = 0, int pageSize = 50, CancellationToken ct = default);

        Task<AuctionIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default);
        Task SaveIndexAsync(AuctionIndexRecord record, CancellationToken ct = default);

        Task<AuctionTransactionRecord> LoadTransactionAsync(string transactionId, CancellationToken ct = default);
        Task SaveTransactionAsync(AuctionTransactionRecord record, CancellationToken ct = default);
        Task<List<AuctionTransactionRecord>> LoadTransactionHistoryAsync(string accountId, int limit = 20, CancellationToken ct = default);
    }
}