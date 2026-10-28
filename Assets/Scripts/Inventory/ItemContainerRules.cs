using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>Pure helpers over item containers (bag or equipment lists).</summary>
    public static class ItemContainerRules
    {
        public static List<ItemInstance> CollectNonEmpty(
            IReadOnlyList<ItemInstance> first,
            IReadOnlyList<ItemInstance> second)
        {
            var loot = new List<ItemInstance>();

            if (first != null)
            {
                for (int i = 0; i < first.Count; i++)
                {
                    if (!first[i].IsEmpty)
                        loot.Add(first[i]);
                }
            }

            if (second != null)
            {
                for (int i = 0; i < second.Count; i++)
                {
                    if (!second[i].IsEmpty)
                        loot.Add(second[i]);
                }
            }

            return loot;
        }
    }
}
