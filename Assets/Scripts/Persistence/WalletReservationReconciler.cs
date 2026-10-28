using System.Collections.Generic;
using System.Threading.Tasks;
using MMO.Auction;
using MMO.Social;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Periodically resolves the pending auction wallet reservations attached to a player's save.
    /// </summary>
    internal sealed class WalletReservationReconciler
    {
        private const float ReconcileInterval = 5f;

        private bool _running;
        private float _nextReconcileTime;

        public void Tick(string accountId, PlayerWallet wallet)
        {
            if (_running || Time.unscaledTime < _nextReconcileTime)
                return;

            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            if (service == null || service.GetPendingWalletReservations(accountId).Count == 0)
                return;

            _nextReconcileTime = Time.unscaledTime + ReconcileInterval;
            _ = ReconcileAsync(service, accountId, wallet);
        }

        private async Task ReconcileAsync(
            PlayerPersistenceService service, string accountId, PlayerWallet wallet)
        {
            _running = true;
            try
            {
                AuctionService auction = SocialHubService.Instance != null
                    ? SocialHubService.Instance.Auction
                    : null;
                if (auction == null)
                    return;

                List<WalletReservationRecord> reservations =
                    service.GetPendingWalletReservations(accountId);
                for (int i = 0; i < reservations.Count; i++)
                {
                    if (!await auction.ReconcileWalletReservationAsync(accountId, wallet, reservations[i]))
                        break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[Persistence] failed to reconcile wallet reservations for '{accountId}': {e.Message}");
            }
            finally
            {
                _running = false;
            }
        }
    }
}
