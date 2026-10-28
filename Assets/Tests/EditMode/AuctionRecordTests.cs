using MMO.Auction;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Characterization of the auction record money math before it moves behind
    /// <c>AuctionPricing</c>. Totals are persisted and shown to players, so the multiplication
    /// and result factories are pinned here.
    /// </summary>
    public sealed class AuctionRecordTests
    {
        [Test]
        public void Listing_TotalPrice_IsUnitPriceTimesStack()
        {
            var listing = new AuctionListingRecord { PricePerUnit = 250, StackCount = 4 };

            Assert.AreEqual(1000, listing.TotalPrice);
        }

        [Test]
        public void Listing_TotalPrice_DefaultsToZeroWhenUnset()
        {
            Assert.AreEqual(0, new AuctionListingRecord().TotalPrice);
        }

        [Test]
        public void BuyOrder_TotalValue_IsUnitPriceTimesQuantity()
        {
            var order = new AuctionBuyOrderRecord { PricePerUnit = 75, QuantityTotal = 3 };

            Assert.AreEqual(225, order.TotalValue);
        }

        [Test]
        public void AuctionResult_OkCarriesItsIdsAndRecords()
        {
            var listing = new AuctionListingRecord { ListingId = "listing-1" };
            var order = new AuctionBuyOrderRecord { BuyOrderId = "order-1" };

            AuctionResult result = AuctionResult.Ok("listing-1", "order-1", listing, order);

            Assert.AreEqual(AuctionRule.Ok, result.Rule);
            Assert.AreEqual("listing-1", result.ListingId);
            Assert.AreEqual("order-1", result.BuyOrderId);
            Assert.AreSame(listing, result.Listing);
            Assert.AreSame(order, result.BuyOrder);
        }

        [Test]
        public void AuctionResult_FailCarriesOnlyTheRule()
        {
            AuctionResult result = AuctionResult.Fail(AuctionRule.InvalidPrice);

            Assert.AreEqual(AuctionRule.InvalidPrice, result.Rule);
            Assert.IsNull(result.ListingId);
            Assert.IsNull(result.BuyOrderId);
            Assert.IsNull(result.Listing);
            Assert.IsNull(result.BuyOrder);
        }
    }
}
