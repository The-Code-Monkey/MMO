using MMO.Auction;
using MMO.Social;
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
                AuctionDeliveryRules.Choose(buyerOnThisZone: true, hasBagSpace: true, inboxCount: MailRules.MaxInbox));
        }

        [Test]
        public void Choose_LocalBuyerWithoutBagSpace_UsesMailWhenInboxHasRoom()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Mail,
                AuctionDeliveryRules.Choose(buyerOnThisZone: true, hasBagSpace: false, inboxCount: MailRules.MaxInbox - 1));
        }

        [Test]
        public void Choose_CrossZoneBuyer_UsesMailEvenWithBagSpace()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Mail,
                AuctionDeliveryRules.Choose(buyerOnThisZone: false, hasBagSpace: true, inboxCount: 0));
        }

        [Test]
        public void Choose_NoBagSpaceAndFullInbox_Rejects()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Reject,
                AuctionDeliveryRules.Choose(buyerOnThisZone: true, hasBagSpace: false, inboxCount: MailRules.MaxInbox));
        }

        [Test]
        public void Choose_CrossZoneBuyerWithFullInbox_Rejects()
        {
            Assert.AreEqual(
                AuctionDeliveryTarget.Reject,
                AuctionDeliveryRules.Choose(buyerOnThisZone: false, hasBagSpace: false, inboxCount: MailRules.MaxInbox));
        }
    }
}
