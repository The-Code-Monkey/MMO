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
    /// <summary>Buy-order creation, fulfillment and cancellation, including lost-RPC recovery.</summary>
    internal sealed class AuctionBuyOrderService
    {
        private readonly IAuctionRepository _repository;
        private readonly NakamaRestClient _rest;
        private readonly SocialHubService _socialHub;
        private readonly AuctionDeliveryService _delivery;
        private readonly Func<string, PlayerWallet> _getWallet;
        private readonly Func<string, PlayerInventory> _getBag;
        private readonly Func<bool> _hasAuctionHouse;
        private readonly int _casRetries;

        public AuctionBuyOrderService(
            IAuctionRepository repository,
            NakamaRestClient rest,
            SocialHubService socialHub,
            AuctionDeliveryService delivery,
            Func<string, PlayerWallet> getWallet,
            Func<string, PlayerInventory> getBag,
            Func<bool> hasAuctionHouse,
            int casRetries)
        {
            _repository = repository;
            _rest = rest;
            _socialHub = socialHub;
            _delivery = delivery;
            _getWallet = getWallet;
            _getBag = getBag;
            _hasAuctionHouse = hasAuctionHouse;
            _casRetries = casRetries;
        }

        /// <summary>Creates a "want to buy" order. Reserves gold from buyer's wallet.</summary>
        public async Task<AuctionResult> CreateAsync(
            string buyerAccountId,
            string itemName,
            int quantity,
            long pricePerUnit,
            int durationHours = AuctionService.DefaultListingDurationHours,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!_hasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (pricePerUnit < AuctionService.MinPricePerUnit)
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

            if (index.ActiveBuyOrderIds != null && index.ActiveBuyOrderIds.Count >= AuctionService.MaxActiveBuyOrdersPerPlayer)
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
            PlayerWallet wallet = _getWallet(buyerAccountId);
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
                result = await AuctionCasRetry.RetryAsync(_casRetries, async () =>
                {
                    // Re-read index inside CAS loop
                    index = await _repository.LoadIndexAsync(buyerAccountId, ct)
                        ?? new AuctionIndexRecord { AccountId = buyerAccountId };

                    if (index.ActiveBuyOrderIds != null && index.ActiveBuyOrderIds.Count >= AuctionService.MaxActiveBuyOrdersPerPlayer)
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
        public async Task<AuctionResult> FulfillAsync(
            string sellerAccountId,
            string buyOrderId,
            int bagX,
            int bagY,
            int stackCount = -1,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!_hasAuctionHouse())
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
        public async Task<AuctionResult> CancelAsync(
            string buyerAccountId,
            string buyOrderId,
            CancellationToken ct = default)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!_hasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            AuctionBuyOrderRecord buyOrder = await _repository.LoadBuyOrderAsync(buyOrderId, ct);
            if (buyOrder == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (buyOrder.BuyerAccountId != buyerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner);

            if (buyOrder.Status != AuctionBuyOrderStatus.Active && buyOrder.Status != AuctionBuyOrderStatus.PartiallyFilled)
                return AuctionResult.Fail(AuctionRule.BuyOrderNotActive);

            long refundAmount = buyOrder.PricePerUnit * buyOrder.QuantityRemaining;

            AuctionResult result = await AuctionCasRetry.RetryAsync(_casRetries, async () =>
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
                PlayerWallet wallet = _getWallet(buyerAccountId);
                wallet?.AddGold(refundAmount);

                _socialHub.NotifyAuctionBuyOrderCancelled(buyerAccountId, buyOrder);
            }

            return result;
        }
    }
}
