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
    /// <summary>Creating and cancelling auction listings, with CAS retries and bag escrow.</summary>
    internal sealed class AuctionListingService
    {
        private readonly IAuctionRepository _repository;
        private readonly SocialHubService _socialHub;
        private readonly MarketIndexService _marketIndex;
        private readonly Func<string, PlayerInventory> _getBag;
        private readonly Func<bool> _isConnected;
        private readonly Func<bool> _hasAuctionHouse;
        private readonly int _casRetries;

        public AuctionListingService(
            IAuctionRepository repository,
            SocialHubService socialHub,
            MarketIndexService marketIndex,
            Func<string, PlayerInventory> getBag,
            Func<bool> isConnected,
            Func<bool> hasAuctionHouse,
            int casRetries)
        {
            _repository = repository;
            _socialHub = socialHub;
            _marketIndex = marketIndex;
            _getBag = getBag;
            _isConnected = isConnected;
            _hasAuctionHouse = hasAuctionHouse;
            _casRetries = casRetries;
        }

        public async Task<AuctionResult> CreateAsync(
            string sellerAccountId,
            int bagX,
            int bagY,
            long pricePerUnit,
            int stackCount,
            int durationHours,
            CancellationToken ct)
        {
            if (!_isConnected() || !_hasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (pricePerUnit < AuctionService.MinPricePerUnit)
                return AuctionResult.Fail(AuctionRule.InvalidPrice);

            PlayerInventory bag = _getBag(sellerAccountId);
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

            int availableCount = item.StackCount < 1 ? 1 : item.StackCount;
            int sellCount = stackCount > 0 ? Math.Min(stackCount, availableCount) : availableCount;
            if (sellCount < 1)
                return AuctionResult.Fail(AuctionRule.InvalidStackCount);

            AuctionIndexRecord index = await _repository.LoadIndexAsync(sellerAccountId, ct)
                ?? new AuctionIndexRecord { AccountId = sellerAccountId };

            if (index.ActiveListingIds != null && index.ActiveListingIds.Count >= AuctionService.MaxActiveListingsPerPlayer)
                return AuctionResult.Fail(AuctionRule.MaxListingsReached);

            AuctionResult result = await RetryAsync(async () =>
            {
                // Re-read index inside the CAS loop.
                index = await _repository.LoadIndexAsync(sellerAccountId, ct)
                    ?? new AuctionIndexRecord { AccountId = sellerAccountId };

                if (index.ActiveListingIds != null && index.ActiveListingIds.Count >= AuctionService.MaxActiveListingsPerPlayer)
                    return AuctionResult.Fail(AuctionRule.MaxListingsReached);

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

                if (index.ActiveListingIds == null)
                    index.ActiveListingIds = new List<string>();
                index.ActiveListingIds.Add(listingId);
                await _repository.SaveIndexAsync(index, ct);

                return AuctionResult.Ok(listingId: listingId, listing: listing);
            }, ct);

            if (result.Rule == AuctionRule.Ok)
            {
                // Remove the item only after the listing is durable.
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

                await _marketIndex.UpsertAsync(result.Listing, ct);
                _socialHub.NotifyAuctionListingCreated(sellerAccountId, result.Listing);
                _socialHub.NotifyMarketChanged(result.ListingId);
            }

            return result;
        }

        public async Task<AuctionResult> CancelAsync(
            string sellerAccountId,
            string listingId,
            CancellationToken ct)
        {
            if (!_isConnected() || !_hasAuctionHouse())
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

                listing.Status = AuctionListingStatus.Cancelled;
                await _repository.SaveListingAsync(listing, ct);

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
                ReturnToSeller(sellerAccountId, listing);
                await _marketIndex.RemoveAsync(listingId, ct);
                _socialHub.NotifyMarketChanged(listingId);
            }

            return result;
        }

        private void ReturnToSeller(string sellerAccountId, AuctionListingRecord listing)
        {
            PlayerInventory bag = _getBag(sellerAccountId);
            if (bag == null)
                return;

            ItemDefinition def = ItemDefinitionRegistry.Resolve(listing.ItemName);
            if (def == null)
                return;

            ItemInstance item = ItemInstance.Create(def, 0, 0, listing.StackCount);
            if (bag.TryAddExistingItem(item, 0, 0))
                return;

            if (new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                .TryFindFreeSpace(bag.Items, item.Width, item.Height, out int x, out int y))
            {
                item.GridX = x;
                item.GridY = y;
                bag.TryAddExistingItem(item, x, y);
                return;
            }

            Debug.LogError($"[Auction] Seller bag full, could not return cancelled item for {sellerAccountId}");
        }

        private async Task<AuctionResult> RetryAsync(
            Func<Task<AuctionResult>> attempt, CancellationToken ct)
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
                    Debug.LogError($"[Auction] Listing mutation failed: {e.Message}");
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }
            }

            return AuctionResult.Fail(AuctionRule.Conflict);
        }
    }
}
