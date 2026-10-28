using MMO.Auction;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    public sealed class AuctionDeliveryRulesTests
    {
        [Test]
        public void Choose_LocalBuyerWithBagSpace_DeliversToBag()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Bag,
                AuctionDeliveryRules.Choose(buyerOnThisZone: true, hasBagSpace: true));
        }

        [Test]
        public void Choose_LocalBuyerWithoutBagSpace_UsesMail()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Mail,
                AuctionDeliveryRules.Choose(buyerOnThisZone: true, hasBagSpace: false));
        }

        [Test]
        public void Choose_CrossZoneBuyer_UsesMailEvenWithBagSpace()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Mail,
                AuctionDeliveryRules.Choose(buyerOnThisZone: false, hasBagSpace: true));
        }
    }
}
