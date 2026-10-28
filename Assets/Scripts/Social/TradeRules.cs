using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>Pure checks shared by the trade shell before it mutates either bag.</summary>
    public static class TradeRules
    {
        /// <summary>
        /// True when every offered instance is still in <paramref name="bag"/>. The shell checks
        /// this immediately before removal so an item equipped or moved after the lock can never
        /// be duplicated by a stale offer.
        /// </summary>
        public static bool OffersStillHeld(
            IReadOnlyList<ItemInstance> offer, IReadOnlyList<ItemInstance> bag)
        {
            if (offer == null || offer.Count == 0 || bag == null)
                return false;

            for (int i = 0; i < offer.Count; i++)
            {
                if (!ContainsInstance(bag, offer[i].InstanceId))
                    return false;
            }

            return true;
        }

        private static bool ContainsInstance(IReadOnlyList<ItemInstance> bag, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId))
                return false;

            for (int i = 0; i < bag.Count; i++)
            {
                if (bag[i].InstanceId == instanceId)
                    return true;
            }

            return false;
        }
    }
}
