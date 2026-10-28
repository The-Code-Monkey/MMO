using MMO.Auction;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class AuctionErrorRulesTests
    {
        [TestCase("INSUFFICIENT_FUNDS", AuctionRule.InsufficientFunds)]
        [TestCase("ALREADY_SOLD", AuctionRule.AlreadySold)]
        [TestCase("NOT_ACTIVE", AuctionRule.ListingNotActive)]
        [TestCase("BUY_ORDER_NOT_ACTIVE", AuctionRule.BuyOrderNotActive)]
        [TestCase("DELIVERY_FAILED", AuctionRule.Conflict)]
        [TestCase("NOT_FOUND", AuctionRule.NotFound)]
        [TestCase("SOMETHING_NEW", AuctionRule.BackendUnavailable)]
        [TestCase(null, AuctionRule.BackendUnavailable)]
        public void Map_ReturnsTheExpectedRule(string code, AuctionRule expected)
        {
            Assert.AreEqual(expected, AuctionErrorRules.Map(code));
        }
    }
}
