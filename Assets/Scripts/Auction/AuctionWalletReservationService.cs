using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Durable wallet reservations around auction RPCs: persist before the remote call, complete or
    /// refund after it, and reconcile an uncommitted reservation after a restart.
    /// </summary>
    internal sealed class AuctionWalletReservationService
    {
        private readonly IAuctionRepository _repository;
        private readonly Func<bool> _isConnected;

        public AuctionWalletReservationService(IAuctionRepository repository, Func<bool> isConnected)
        {
            _repository = repository;
            _isConnected = isConnected;
        }

        /// <summary>Refunds an uncommitted reservation after restart, or clears a committed one.</summary>
        public async Task<bool> ReconcileAsync(
            string accountId, PlayerWallet wallet, WalletReservationRecord reservation)
        {
            if (string.IsNullOrEmpty(accountId) || wallet == null || reservation == null ||
                reservation.Amount <= 0 || !_isConnected())
                return false;

            AuctionListingRecord listing = null;
            AuctionBuyOrderRecord buyOrder = null;
            try
            {
                switch (reservation.Kind)
                {
                    case WalletReservationKind.AuctionPurchase:
                        listing = await _repository.LoadListingAsync(reservation.TargetId, CancellationToken.None);
                        break;
                    case WalletReservationKind.BuyOrder:
                        buyOrder = await _repository.LoadBuyOrderAsync(reservation.TargetId, CancellationToken.None);
                        break;
                    default:
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Could not reconcile wallet reservation {reservation.ReservationId}: {e.Message}");
                return false;
            }

            if (!AuctionWalletReservationRules.TryGetRefundAmount(
                    reservation, accountId, listing, buyOrder, out long refundAmount))
            {
                return false;
            }

            if (refundAmount > 0)
                wallet.AddGold(refundAmount);

            return await CompleteAsync(accountId, reservation.ReservationId);
        }

        public static async Task<bool> PersistAsync(
            string accountId, WalletReservationRecord reservation)
        {
            PlayerPersistenceService persistence = PlayerPersistenceService.Instance;
            return persistence != null && await persistence.AddWalletReservationAsync(accountId, reservation);
        }

        public static async Task<bool> CompleteAsync(string accountId, string reservationId)
        {
            PlayerPersistenceService persistence = PlayerPersistenceService.Instance;
            bool saved = persistence != null &&
                await persistence.CompleteWalletReservationAsync(accountId, reservationId);
            if (!saved)
                Debug.LogError($"[Auction] Wallet reservation {reservationId} for {accountId} remains queued for recovery.");

            return saved;
        }

        public static async Task RefundAsync(
            string accountId, PlayerWallet wallet, long amount, string reservationId)
        {
            wallet.AddGold(amount);

            try
            {
                if (await CompleteAsync(accountId, reservationId))
                    return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Auction] Could not persist refund of {amount} gold for {accountId}: {e.Message}");
                return;
            }

            Debug.LogError($"[Auction] Could not persist refund of {amount} gold for {accountId}.");
        }
    }
}
