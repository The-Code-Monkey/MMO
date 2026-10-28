using MMO.Auction;
using MMO.Persistence;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class AuctionWalletReservationRulesTests
    {
        [Test]
        public void TryGetRefundAmount_ListingPurchase_RefundsUnlessSoldToBuyer()
        {
            var reservation = new WalletReservationRecord
            {
                Kind = WalletReservationKind.AuctionPurchase,
                TargetId = "listing-1",
                Amount = 125,
            };

            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", new AuctionListingRecord
                {
                    ListingId = "listing-1",
                    Status = AuctionListingStatus.Active,
                }, null, out long activeRefund));
            Assert.AreEqual(125L, activeRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", new AuctionListingRecord
                {
                    ListingId = "listing-1",
                    Status = AuctionListingStatus.Sold,
                    BuyerAccountId = "other",
                }, null, out long otherBuyerRefund));
            Assert.AreEqual(125L, otherBuyerRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", new AuctionListingRecord
                {
                    ListingId = "listing-1",
                    Status = AuctionListingStatus.Sold,
                    BuyerAccountId = "buyer",
                }, null, out long committedRefund));
            Assert.AreEqual(0L, committedRefund);
        }

        [Test]
        public void TryGetRefundAmount_BuyOrder_RefundsMissingOrdersAndCanceledRemainder()
        {
            var reservation = new WalletReservationRecord
            {
                Kind = WalletReservationKind.BuyOrder,
                TargetId = "order-1",
                Amount = 500,
            };

            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", null, null, out long missingRefund));
            Assert.AreEqual(500L, missingRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", null, new AuctionBuyOrderRecord
                {
                    BuyOrderId = "order-1",
                    BuyerAccountId = "other",
                }, out long wrongBuyerRefund));
            Assert.AreEqual(500L, wrongBuyerRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", null, new AuctionBuyOrderRecord
                {
                    BuyOrderId = "order-1",
                    BuyerAccountId = "buyer",
                    Status = AuctionBuyOrderStatus.Active,
                }, out long activeRefund));
            Assert.AreEqual(0L, activeRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", null, new AuctionBuyOrderRecord
                {
                    BuyOrderId = "order-1",
                    BuyerAccountId = "buyer",
                    Status = AuctionBuyOrderStatus.Cancelled,
                    PricePerUnit = 100,
                    QuantityRemaining = 2,
                }, out long canceledRefund));
            Assert.AreEqual(200L, canceledRefund);
            Assert.IsTrue(AuctionWalletReservationRules.TryGetRefundAmount(
                reservation, "buyer", null, new AuctionBuyOrderRecord
                {
                    BuyOrderId = "order-1",
                    BuyerAccountId = "buyer",
                    Status = AuctionBuyOrderStatus.Expired,
                    PricePerUnit = 100,
                    QuantityRemaining = 1,
                }, out long expiredRefund));
            Assert.AreEqual(100L, expiredRefund);
        }
    }
}
