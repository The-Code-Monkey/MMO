using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Auction
{
    public interface IAuctionRepository
    {
        Task<AuctionListingRecord> LoadListingAsync(string listingId, CancellationToken ct = default);
        Task SaveListingAsync(AuctionListingRecord record, CancellationToken ct = default);
        Task DeleteListingAsync(string listingId, CancellationToken ct = default);
        Task<List<AuctionListingRecord>> LoadListingsAsync(IReadOnlyList<string> listingIds, CancellationToken ct = default);
        Task<List<AuctionListingRecord>> SearchListingsAsync(string itemNameFilter = null, long maxPrice = 0, int page = 0, int pageSize = 50, CancellationToken ct = default);

        Task<AuctionIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default);
        Task SaveIndexAsync(AuctionIndexRecord record, CancellationToken ct = default);

        Task<AuctionTransactionRecord> LoadTransactionAsync(string transactionId, CancellationToken ct = default);
        Task SaveTransactionAsync(AuctionTransactionRecord record, CancellationToken ct = default);
        Task<List<AuctionTransactionRecord>> LoadTransactionHistoryAsync(string accountId, int limit = 20, CancellationToken ct = default);
    }
}