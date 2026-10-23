using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace MMO.Content
{
    /// <summary>
    /// The pure loot roll. Each entry is rolled independently so a table can express the ticket's
    /// "50% Leather, 1% Epic Mount" without the two chances competing for a normalized total.
    /// </summary>
    public static class LootRules
    {
        /// <summary>
        /// Rolls every entry once. A null table or an entry with no item contributes nothing; a
        /// null random is a caller bug, not an empty drop.
        /// </summary>
        public static List<LootDrop> Roll(WeightedLootTable table, Random rng)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            var drops = new List<LootDrop>();
            if (table == null)
                return drops;

            IReadOnlyList<LootEntry> entries = table.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                LootEntry entry = entries[i];
                if (entry.Item == null)
                    continue;

                float chance = Mathf.Clamp01(entry.Chance);
                if (chance <= 0f || rng.NextDouble() >= chance)
                    continue;

                int count = RollCount(entry.MinCount, entry.MaxCount, rng);
                if (count > 0)
                    drops.Add(new LootDrop(entry.Item, count));
            }

            return drops;
        }

        /// <summary>
        /// An inclusive count in the authored range. A reversed range collapses to its minimum, so
        /// hand-edited YAML cannot produce a negative or randomly enormous drop.
        /// </summary>
        public static int RollCount(int minCount, int maxCount, Random rng)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));

            int min = Math.Max(0, minCount);
            int max = Math.Max(min, maxCount);
            if (max == min)
                return min;

            long span = (long)max - min + 1;
            return min + (int)(rng.NextDouble() * span);
        }
    }
}
