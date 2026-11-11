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
    /// <summary>Listing purchase through the atomic Nakama RPC, including lost-response recovery.</summary>
    internal sealed class AuctionPurchaseService
    {
        private readonly IAuctionRepository _repository;
        private readonly NakamaRestClient _rest;
        private readonly SocialHubService _socialHub;
        private readonly AuctionDeliveryService _delivery;
        private readonly MarketIndexService _marketIndex;
        private readonly Func<string, PlayerWallet> _getWallet;
        private readonly Func<bool> _hasAuctionHouse;

        public AuctionPurchaseService(
            IAuctionRepository repository,
            NakamaRestClient rest,
            SocialHubService socialHub,
            AuctionDeliveryService delivery,
            MarketIndexService marketIndex,
            Func<string, PlayerWallet> getWallet,
            Func<bool> hasAuctionHouse)
        {
            _repository = repository;
            _rest = rest;
            _socialHub = socialHub;
            _delivery = delivery;
            _marketIndex = marketIndex;
            _getWallet = getWallet;
            _hasAuctionHouse = hasAuctionHouse;
        }

        public async Task<AuctionResult> BuyAsync(
            string buyerAccountId,
            string listingId,
            CancellationToken ct)
        {
            if (!_rest.IsConnected)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!_hasAuctionHouse())
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            AuctionListingRecord listing = await _repository.LoadListingAsync(listingId, ct);
            if (listing == null)
                return AuctionResult.Fail(AuctionRule.NotFound);

            if (listing.Status != AuctionListingStatus.Active)
                return AuctionResult.Fail(AuctionRule.ListingNotActive);

            if (listing.ExpiresAtTicks > 0 && listing.ExpiresAtTicks < DateTimeOffset.UtcNow.UtcDateTime.Ticks)
                return AuctionResult.Fail(AuctionRule.ListingNotActive);

            if (listing.SellerAccountId == buyerAccountId)
                return AuctionResult.Fail(AuctionRule.NotOwner);

            // Reserve the buyer's gold up front so the live wallet and its save stay consistent.
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

            PlayerWallet buyerWallet = _getWallet(buyerAccountId);
            if (buyerWallet == null)
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);

            if (!buyerWallet.TrySpend(totalPrice))
                return AuctionResult.Fail(AuctionRule.InsufficientFunds);

            if (!await AuctionWalletReservationService.PersistAsync(buyerAccountId, reservation))
            {
                await AuctionWalletReservationService.RefundAsync(
                    buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }

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

                    await AuctionWalletReservationService.RefundAsync(
                        buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
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

                    await AuctionWalletReservationService.RefundAsync(
                        buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }

                if (!buyResponse.success)
                {
                    await AuctionWalletReservationService.RefundAsync(
                        buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                    return AuctionResult.Fail(AuctionErrorRules.Map(buyResponse.errorCode));
                }

                await AuctionWalletReservationService.CompleteAsync(buyerAccountId, reservation.ReservationId);

                AuctionListingRecord updatedListing = buyResponse.listing;
                AuctionTransactionRecord transaction = buyResponse.transaction;

                AuctionIndexRecord buyerIndex = await _repository.LoadIndexAsync(buyerAccountId, ct)
                    ?? new AuctionIndexRecord { AccountId = buyerAccountId };
                if (buyerIndex.PurchaseHistoryIds == null)
                    buyerIndex.PurchaseHistoryIds = new List<string>();
                buyerIndex.PurchaseHistoryIds.Add(transaction.TransactionId);
                await _repository.SaveIndexAsync(buyerIndex, ct);

                AuctionIndexRecord sellerIndex = await _repository.LoadIndexAsync(listing.SellerAccountId, ct);
                if (sellerIndex != null && sellerIndex.ActiveListingIds != null)
                {
                    sellerIndex.ActiveListingIds.Remove(listingId);
                    await _repository.SaveIndexAsync(sellerIndex, ct);
                }

                _socialHub.NotifyAuctionListingSold(buyerAccountId, listing.SellerAccountId, updatedListing, transaction);
                _socialHub.NotifyAuctionPurchaseCompleted(buyerAccountId, transaction);

                await _marketIndex.RemoveAsync(listingId, ct);
                _socialHub.NotifyMarketChanged(listingId);

                await _delivery.CreditSellerAsync(listing.SellerAccountId, totalPrice, ct);
                sellerCredited = true;

                if (!string.IsNullOrEmpty(buyResponse.deliveryMailId))
                {
                    await _socialHub.ClaimMailAsync(
                        buyerAccountId,
                        buyResponse.deliveryMailId,
                        CancellationToken.None);
                }
                else
                {
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

                await AuctionWalletReservationService.RefundAsync(
                    buyerAccountId, buyerWallet, totalPrice, reservation.ReservationId);
                return AuctionResult.Fail(AuctionRule.BackendUnavailable);
            }
        }

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

                await _marketIndex.RemoveAsync(listingId, CancellationToken.None);
                _socialHub.NotifyMarketChanged(listingId);

                return AuctionResult.Ok(listingId: listingId, listing: sold);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Lost-purchase recovery failed for {listingId}: {e.Message}");
                return null;
            }
        }
    }
}
