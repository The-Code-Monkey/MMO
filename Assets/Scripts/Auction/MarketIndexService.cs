using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Keeps the realm-wide <see cref="MarketSearchIndex"/> cache converging on the authoritative
    /// listings. Writes are best-effort and version-guarded: a lost row is repaired by the next
    /// reconcile pass, never by trusting the cache for settlement.
    /// </summary>
    internal sealed class MarketIndexService
    {
        /// <summary>How long a reconcile snapshot is trusted before the next read rebuilds it.</summary>
        public const long ReconcileIntervalTicks = 5 * TimeSpan.TicksPerMinute;

        private readonly IAuctionRepository _repository;
        private readonly Func<bool> _isConnected;
        private readonly int _casRetries;

        public MarketIndexService(IAuctionRepository repository, Func<bool> isConnected, int casRetries)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _isConnected = isConnected;
            _casRetries = casRetries;
        }

        /// <summary>
        /// Returns a searchable snapshot, rebuilding from truth when the cache is missing or older
        /// than <see cref="ReconcileIntervalTicks"/>. On a backend failure an empty snapshot is
        /// returned rather than throwing: search is a convenience, never a settlement path.
        /// </summary>
        public async Task<MarketSearchIndex> EnsureAsync(CancellationToken ct = default)
        {
            if (!_isConnected())
                return new MarketSearchIndex();

            MarketSearchIndex index = await TryLoadAsync(ct);
            long nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
            if (index != null && !MarketIndexRules.IsStale(index.ReconciledAtTicks, nowTicks, ReconcileIntervalTicks))
                return index;

            return await ReconcileAsync(ct);
        }

        /// <summary>Recomputes every row from the authoritative listings under the index CAS loop.</summary>
        public async Task<MarketSearchIndex> ReconcileAsync(CancellationToken ct = default)
        {
            if (!_isConnected())
                return new MarketSearchIndex();

            var fallback = new MarketSearchIndex();
            for (int attempt = 0; attempt < _casRetries; attempt++)
            {
                try
                {
                    MarketSearchIndex index = await _repository.LoadSearchIndexAsync(ct) ?? new MarketSearchIndex();
                    fallback = index;

                    long nowTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                    List<AuctionListingRecord> listings = await _repository.ListActiveListingsAsync(ct);

                    var truth = new List<SearchRow>(listings.Count);
                    for (int i = 0; i < listings.Count; i++)
                    {
                        if (MarketIndexMapper.IsActive(listings[i], nowTicks))
                            truth.Add(MarketIndexMapper.ToRow(listings[i]));
                    }

                    index.Rows = MarketIndexRules.Reconcile(index.Rows, truth, nowTicks);
                    index.ReconciledAtTicks = nowTicks;
                    await _repository.SaveSearchIndexAsync(index, ct);
                    return index;
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
                    Debug.LogWarning($"[Auction] Search index reconcile failed: {e.Message}");
                    return fallback;
                }
            }

            return fallback;
        }

        /// <summary>Upserts one listing; an inactive listing is removed instead.</summary>
        public async Task UpsertAsync(AuctionListingRecord listing, CancellationToken ct = default)
        {
            if (listing == null || string.IsNullOrEmpty(listing.ListingId))
                return;

            if (!MarketIndexMapper.IsActive(listing, DateTimeOffset.UtcNow.UtcDateTime.Ticks))
            {
                await RemoveAsync(listing.ListingId, ct);
                return;
            }

            await MutateAsync((index, nowTicks) =>
                MarketIndexRules.Upsert(index.Rows, MarketIndexMapper.ToRow(listing), nowTicks), ct);
        }

        public Task RemoveAsync(string listingId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(listingId))
                return Task.CompletedTask;

            return MutateAsync((index, _) => MarketIndexRules.Remove(index.Rows, listingId), ct);
        }

        /// <summary>
        /// Re-reads one listing from truth and updates only its row. This is the
        /// <c>MarketChanged</c> hint handler: cheap, idempotent, and it repairs a writer that
        /// missed its own cache update.
        /// </summary>
        public async Task RefreshAsync(string listingId, CancellationToken ct = default)
        {
            if (!_isConnected() || string.IsNullOrEmpty(listingId))
                return;

            try
            {
                AuctionListingRecord listing = await _repository.LoadListingAsync(listingId, ct);
                if (listing == null)
                    await RemoveAsync(listingId, ct);
                else
                    await UpsertAsync(listing, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Auction] Search row refresh failed for {listingId}: {e.Message}");
            }
        }

        private async Task MutateAsync(Action<MarketSearchIndex, long> mutate, CancellationToken ct)
        {
            for (int attempt = 0; attempt < _casRetries; attempt++)
            {
                try
                {
                    MarketSearchIndex index = await _repository.LoadSearchIndexAsync(ct) ?? new MarketSearchIndex();
                    mutate(index, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
                    await _repository.SaveSearchIndexAsync(index, ct);
                    return;
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
                    Debug.LogWarning($"[Auction] Search index update failed: {e.Message}");
                    return;
                }
            }
        }

        private async Task<MarketSearchIndex> TryLoadAsync(CancellationToken ct)
        {
            try
            {
                return await _repository.LoadSearchIndexAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Auction] Search index load failed: {e.Message}");
                return null;
            }
        }
    }
}
