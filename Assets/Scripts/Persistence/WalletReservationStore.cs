using System.Collections.Generic;

namespace MMO.Persistence
{
    /// <summary>Per-account unresolved wallet reservations, copied in and out.</summary>
    internal sealed class WalletReservationStore
    {
        private readonly Dictionary<string, List<WalletReservationRecord>> _pending = new();

        public void SetFromRecord(string accountId, PlayerSaveRecord record)
        {
            List<WalletReservationRecord> reservations =
                CopyAll(record?.PendingWalletReservations);
            if (reservations.Count > 0)
                _pending[accountId] = reservations;
            else
                _pending.Remove(accountId);
        }

        public bool Add(string accountId, WalletReservationRecord reservation)
        {
            if (!_pending.TryGetValue(accountId, out List<WalletReservationRecord> reservations))
            {
                reservations = new List<WalletReservationRecord>();
                _pending.Add(accountId, reservations);
            }

            for (int i = 0; i < reservations.Count; i++)
            {
                if (reservations[i].ReservationId == reservation.ReservationId)
                    return false;
            }

            reservations.Add(Copy(reservation));
            return true;
        }

        public void Remove(string accountId, string reservationId)
        {
            if (!_pending.TryGetValue(accountId, out List<WalletReservationRecord> reservations))
                return;

            reservations.RemoveAll(reservation => reservation.ReservationId == reservationId);
            if (reservations.Count == 0)
                _pending.Remove(accountId);
        }

        public List<WalletReservationRecord> Get(string accountId)
            => _pending.TryGetValue(accountId ?? string.Empty, out List<WalletReservationRecord> reservations)
                ? CopyAll(reservations)
                : new List<WalletReservationRecord>();

        public static List<WalletReservationRecord> CopyAll(
            IReadOnlyList<WalletReservationRecord> reservations)
        {
            var copy = new List<WalletReservationRecord>();
            if (reservations == null)
                return copy;

            for (int i = 0; i < reservations.Count; i++)
            {
                if (reservations[i] != null)
                    copy.Add(Copy(reservations[i]));
            }

            return copy;
        }

        public static WalletReservationRecord Copy(WalletReservationRecord reservation)
        {
            return reservation == null ? null : new WalletReservationRecord
            {
                ReservationId = reservation.ReservationId,
                TargetId = reservation.TargetId,
                Kind = reservation.Kind,
                Amount = reservation.Amount,
            };
        }
    }
}
