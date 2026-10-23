using System;
using System.Collections.Generic;
using FishNet.Managing;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>Where a rolled drop set ended up.</summary>
    public readonly struct LootAwardResult
    {
        public LootAwardResult(int addedToInventory, IReadOnlyList<ItemInstance> leftovers, LootBag bag)
        {
            AddedToInventory = addedToInventory;
            Leftovers = leftovers ?? Array.Empty<ItemInstance>();
            Bag = bag;
        }

        /// <summary>How many item units landed in the killer's bag.</summary>
        public int AddedToInventory { get; }

        /// <summary>Items that could not be placed anywhere; empty when they reached a loot bag.</summary>
        public IReadOnlyList<ItemInstance> Leftovers { get; }

        /// <summary>The bag spawned for unplaced items, or null.</summary>
        public LootBag Bag { get; }
    }

    /// <summary>
    /// Server-side award path for a rolled drop set. It prefers the recipient's inventory, falls
    /// back to a loot bag for anything that does not fit, and reports what could not be placed so
    /// a caller never silently destroys an item.
    /// </summary>
    public static class LootAwarder
    {
        public static LootAwardResult Award(
            IReadOnlyList<LootDrop> drops,
            PlayerInventory inventory,
            NetworkManager manager,
            Vector3 position,
            bool preferBag)
        {
            var unplaced = new List<ItemInstance>();
            int added = 0;

            if (drops != null)
            {
                for (int i = 0; i < drops.Count; i++)
                {
                    LootDrop drop = drops[i];
                    if (drop.Item == null || drop.Count <= 0)
                        continue;

                    if (preferBag || inventory == null)
                    {
                        for (int n = 0; n < drop.Count; n++)
                            unplaced.Add(ItemInstance.Create(drop.Item, 0, 0));
                        continue;
                    }

                    for (int n = 0; n < drop.Count; n++)
                    {
                        if (inventory.TryAddItem(drop.Item))
                            added++;
                        else
                            unplaced.Add(ItemInstance.Create(drop.Item, 0, 0));
                    }
                }
            }

            if (unplaced.Count == 0)
                return new LootAwardResult(added, Array.Empty<ItemInstance>(), null);

            LootBag bag = LootBagSpawner.Spawn(manager, null, position, unplaced);
            if (bag != null)
                return new LootAwardResult(added, Array.Empty<ItemInstance>(), bag);

            return new LootAwardResult(added, unplaced, null);
        }
    }
}
