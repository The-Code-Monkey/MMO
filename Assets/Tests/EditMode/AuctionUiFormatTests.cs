using MMO.Auction;
using MMO.UI;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class AuctionUiFormatTests
    {
        [Test]
        public void ListingPrice_ShowsTotalAndUnitPrice()
        {
            var listing = new AuctionListingRecord { PricePerUnit = 25, StackCount = 4 };
            Assert.AreEqual("100 gold (25 each)", AuctionUiFormat.ListingPrice(listing));
        }

        [Test]
        public void StackLabel_HidesSingleItems()
        {
            Assert.AreEqual(string.Empty, AuctionUiFormat.StackLabel(1));
            Assert.AreEqual("x2", AuctionUiFormat.StackLabel(2));
        }

        [Test]
        public void BuyOrderPrice_ShowsUnitAndTotal()
        {
            var order = new AuctionBuyOrderRecord { PricePerUnit = 50, QuantityTotal = 3 };
            Assert.AreEqual("50 gold each (Total: 150)", AuctionUiFormat.BuyOrderPrice(order));
        }

        [Test]
        public void StatusLabels_CoverEveryValue()
        {
            Assert.AreEqual("Active", AuctionUiFormat.ListingStatus(AuctionListingStatus.Active));
            Assert.AreEqual("Sold", AuctionUiFormat.ListingStatus(AuctionListingStatus.Sold));
            Assert.AreEqual("Cancelled", AuctionUiFormat.ListingStatus(AuctionListingStatus.Cancelled));
            Assert.AreEqual("Expired", AuctionUiFormat.ListingStatus(AuctionListingStatus.Expired));

            Assert.AreEqual("Partially Filled", AuctionUiFormat.BuyOrderStatus(AuctionBuyOrderStatus.PartiallyFilled));
            Assert.AreEqual("Filled", AuctionUiFormat.BuyOrderStatus(AuctionBuyOrderStatus.Filled));
        }
    }
}
