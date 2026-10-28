using MMO.Auction;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class AuctionPricingTests
    {
        [Test]
        public void TotalPrice_MultipliesUnitPriceByStack()
        {
            Assert.AreEqual(750, AuctionPricing.TotalPrice(150, 5));
        }

        [Test]
        public void TotalPrice_ZeroStackIsZero()
        {
            Assert.AreEqual(0, AuctionPricing.TotalPrice(150, 0));
        }

        [Test]
        public void TotalValue_MultipliesUnitPriceByQuantity()
        {
            Assert.AreEqual(600, AuctionPricing.TotalValue(200, 3));
        }
    }
}
