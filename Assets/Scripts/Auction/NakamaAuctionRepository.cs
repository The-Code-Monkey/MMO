using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Nakama-backed auction repository: listings, index, and transactions all stored under
    /// the shared service account. Every write is version-guarded so multiple zone servers
    /// race on the same revision rather than silently overwriting.
    /// </summary>
    public sealed class NakamaAuctionRepository : IAuctionRepository
    {
        private readonly NakamaRestClient _rest;

        public NakamaAuctionRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        public async Task<AuctionListingRecord> LoadListingAsync(string listingId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(AuctionListingRecord.Collection, listingId, ct);
            return Deserialize<AuctionListingRecord>(obj);
        }

        public async Task SaveListingAsync(AuctionListingRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                AuctionListingRecord.Collection, record.ListingId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task DeleteListingAsync(string listingId, CancellationToken ct = default)
            => await _rest.DeleteObjectAsync(AuctionListingRecord.Collection, listingId, ct);

        public async Task<List<AuctionListingRecord>> LoadListingsAsync(IReadOnlyList<string> listingIds, CancellationToken ct = default)
        {
            if (listingIds == null || listingIds.Count == 0)
                return new List<AuctionListingRecord>();

            List<StorageObjectData> objects = await _rest.ReadObjectsAsync(AuctionListingRecord.Collection, listingIds, ct);

            var result = new List<AuctionListingRecord>(objects.Count);
            foreach (StorageObjectData obj in objects)
            {
                AuctionListingRecord record = Deserialize<AuctionListingRecord>(obj);
                if (record != null)
                    result.Add(record);
            }
            return result;
        }

        public async Task<List<AuctionListingRecord>> SearchListingsAsync(string itemNameFilter = null, long maxPrice = 0, int page = 0, int pageSize = 50, CancellationToken ct = default)
        {
            // For now, list all active listings by scanning the collection.
            // In production, this would use Nakama's storage list with a cursor.
            // Since we don't have a dedicated search index, we list all and filter client-side.
            // This is acceptable for moderate listing counts (< 10k).

            List<StorageObjectData> objects = await _rest.ListObjectsAsync(AuctionListingRecord.Collection, ct);

            var result = new List<AuctionListingRecord>();
            foreach (StorageObjectData obj in objects)
            {
                AuctionListingRecord record = Deserialize<AuctionListingRecord>(obj);
                if (record == null)
                    continue;

                // Filter: only active listings
                if (record.Status != AuctionListingStatus.Active)
                    continue;

                // Filter: not expired
                if (record.ExpiresAtTicks > 0 && record.ExpiresAtTicks < DateTimeOffset.UtcNow.UtcDateTime.Ticks)
                    continue;

                // Filter: item name (case-insensitive contains)
                if (!string.IsNullOrEmpty(itemNameFilter))
                {
                    ItemDefinition def = ItemDefinitionRegistry.Resolve(record.ItemName);
                    string displayName = def != null ? def.ItemName : record.ItemName;
                    if (!displayName.Contains(itemNameFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Filter: max price
                if (maxPrice > 0 && record.TotalPrice > maxPrice)
                    continue;

                result.Add(record);
            }

            // Sort by listed time descending (newest first)
            result.Sort((a, b) => b.ListedAtTicks.CompareTo(a.ListedAtTicks));

            // Paginate
            int start = page * pageSize;
            if (start >= result.Count)
                return new List<AuctionListingRecord>();

            int count = Math.Min(pageSize, result.Count - start);
            return result.GetRange(start, count);
        }

        public async Task<AuctionIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(AuctionIndexRecord.Collection, accountId, ct);
            AuctionIndexRecord record = Deserialize<AuctionIndexRecord>(obj);
            if (record != null && string.IsNullOrEmpty(record.AccountId))
                record.AccountId = accountId;
            return record;
        }

        public async Task SaveIndexAsync(AuctionIndexRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                AuctionIndexRecord.Collection, record.AccountId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<AuctionTransactionRecord> LoadTransactionAsync(string transactionId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(AuctionTransactionRecord.Collection, transactionId, ct);
            return Deserialize<AuctionTransactionRecord>(obj);
        }

        public async Task SaveTransactionAsync(AuctionTransactionRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                AuctionTransactionRecord.Collection, record.TransactionId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<List<AuctionTransactionRecord>> LoadTransactionHistoryAsync(string accountId, int limit = 20, CancellationToken ct = default)
        {
            // For history, we need to scan transactions. In production, this would use a secondary index.
            // For now, we load the player's index and then batch-load their transactions.
            AuctionIndexRecord index = await LoadIndexAsync(accountId, ct);
            if (index == null || index.PurchaseHistoryIds == null || index.PurchaseHistoryIds.Count == 0)
                return new List<AuctionTransactionRecord>();

            // Take the most recent N
            var recentIds = index.PurchaseHistoryIds
                .OrderByDescending(id => id) // assuming IDs have time component
                .Take(limit)
                .ToList();

            var result = new List<AuctionTransactionRecord>();
            foreach (string id in recentIds)
            {
                AuctionTransactionRecord tx = await LoadTransactionAsync(id, ct);
                if (tx != null)
                    result.Add(tx);
            }
            return result;
        }

        private static T Deserialize<T>(StorageObjectData obj) where T : class
        {
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return null;

            T record = JsonUtility.FromJson<T>(obj.Value);
            if (record is AuctionListingRecord listing)
                listing.StorageVersion = obj.Version;
            else if (record is AuctionIndexRecord index)
                index.StorageVersion = obj.Version;
            else if (record is AuctionTransactionRecord tx)
                tx.StorageVersion = obj.Version;

            return record;
        }
    }
}