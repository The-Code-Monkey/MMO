using MMO.Persistence;

namespace MMO.Auction
{
    /// <summary>Pure checks for whether a pending wallet debit reached the auction backend.</summary>
    public static class AuctionWalletReservationRules
    {
        /// <summary>Resolves the refund still owed for a pending purchase or buy-order debit.</summary>
        public static bool TryGetRefundAmount(
            WalletReservationRecord reservation,
            string accountId,
            AuctionListingRecord listing,
            AuctionBuyOrderRecord buyOrder,
            out long refundAmount)
        {
            refundAmount = 0;
            if (reservation == null || string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(reservation.TargetId) ||
                reservation.Amount <= 0)
                return false;

            switch (reservation.Kind)
            {
                case WalletReservationKind.AuctionPurchase:
                    bool purchaseCommitted = listing != null &&
                        listing.ListingId == reservation.TargetId &&
                        listing.Status == AuctionListingStatus.Sold &&
                        listing.BuyerAccountId == accountId;
                    if (!purchaseCommitted)
                        refundAmount = reservation.Amount;
                    return true;
                case WalletReservationKind.BuyOrder:
                    bool orderMatches = buyOrder != null &&
                        buyOrder.BuyOrderId == reservation.TargetId &&
                        buyOrder.BuyerAccountId == accountId;
                    if (!orderMatches)
                    {
                        refundAmount = reservation.Amount;
                    }
                    else if (buyOrder.Status == AuctionBuyOrderStatus.Cancelled ||
                             buyOrder.Status == AuctionBuyOrderStatus.Expired)
                    {
                        refundAmount = buyOrder.PricePerUnit * buyOrder.QuantityRemaining;
                    }
                    return true;
                default:
                    return false;
            }
        }
    }
}
