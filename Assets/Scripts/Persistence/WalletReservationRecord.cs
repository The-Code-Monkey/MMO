using System;

namespace MMO.Persistence
{
    /// <summary>The backend operation a durable wallet reservation protects.</summary>
    public enum WalletReservationKind
    {
        AuctionPurchase,
        BuyOrder,
    }

    /// <summary>A debit saved until its auction operation can be confirmed or rolled back.</summary>
    [Serializable]
    public sealed class WalletReservationRecord
    {
        public string ReservationId;
        public string TargetId;
        public WalletReservationKind Kind;
        public long Amount;
    }
}
