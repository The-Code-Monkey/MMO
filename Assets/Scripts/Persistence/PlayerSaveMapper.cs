using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The only code that knows how a live <c>PlayerStats</c> + <c>PlayerInventory</c> maps onto a
    /// <see cref="PlayerSaveRecord"/> and back. Pure enough to unit test in EditMode: capture takes
    /// plain values and <see cref="IReadOnlyList{T}"/>s, and apply returns plain lists — the
    /// networked components are left to the callers (<see cref="PlayerSaveTrigger"/> wires the two
    /// halves together), so none of the mapping logic needs a running server.
    ///
    /// Load-time pruning is deliberate: an item whose definition no longer resolves is counted in
    /// <paramref name="skippedItems"/> and dropped rather than resurrected as a ghost entry, so a
    /// balance-data rollback can never corrupt a save. Surplus equipment slots beyond the current
    /// slot count are pruned the same way rather than grown past the container.
    /// </summary>
    public static class PlayerSaveMapper
    {
        public static PlayerSaveRecord Capture(
            string userId,
            int level,
            int health,
            int mana,
            long gold,
            Vector3 position,
            IReadOnlyList<ItemInstance> bag,
            IReadOnlyList<ItemInstance> equipment)
        {
            var record = new PlayerSaveRecord
            {
                UserId = userId,
                Level = level,
                Health = health,
                Mana = mana,
                Gold = gold,
                Position = position,
            };

            if (bag != null)
            {
                for (int i = 0; i < bag.Count; i++)
                {
                    ItemInstance item = bag[i];
                    if (!item.IsEmpty)
                        record.Items.Add(ToSaveData(item, -1));
                }
            }

            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    ItemInstance item = equipment[i];
                    if (!item.IsEmpty)
                        record.Items.Add(ToSaveData(item, i));
                }
            }

            return record;
        }

        /// <summary>
        /// Rebuilds the two containers from a record. A null or empty record yields two empty
        /// lists (the caller seeds defaults); items with unresolvable definitions or slot indices
        /// past the current slot count are skipped and counted.
        /// </summary>
        public static void Apply(
            PlayerSaveRecord record,
            out List<ItemInstance> bag,
            out List<ItemInstance> equipment,
            out int skippedItems,
            out long gold)
        {
            bag = new List<ItemInstance>();
            equipment = new List<ItemInstance>();
            skippedItems = 0;
            gold = 0;

            if (record == null)
                return;

            gold = record.Gold;

            if (record.Items == null)
                return;

            for (int i = 0; i < record.Items.Count; i++)
            {
                ItemSaveData saved = record.Items[i];
                if (string.IsNullOrEmpty(saved.ItemName))
                {
                    skippedItems++;
                    continue;
                }

                // Resolve through the registry so the same no-definition-anywhere miss a client
                // equip would hit is what drops the item here.
                ItemDefinition definition = ItemDefinitionRegistry.Resolve(saved.ItemName);
                if (definition == null)
                {
                    skippedItems++;
                    continue;
                }

                // Durability is clamped to the definition's maximum so a balance-data rollback
                // (a lower cap) can never leave an item over its own ceiling. A save written
                // before durability existed carries zero for a destructible item; that reads as
                // "fully repaired" rather than "broken".
                int maxDurability = definition.MaxDurability;
                int durability = saved.Durability <= 0 ? maxDurability : Mathf.Min(saved.Durability, maxDurability);

                var instance = new ItemInstance
                {
                    ItemName = saved.ItemName,
                    InstanceId = string.IsNullOrEmpty(saved.InstanceId) ? ItemInstance.NewInstanceId() : saved.InstanceId,
                    GridX = saved.GridX,
                    GridY = saved.GridY,
                    Width = saved.Width,
                    Height = saved.Height,
                    Durability = durability,
                };

                if (saved.EquipmentSlot < 0)
                {
                    bag.Add(instance);
                }
                else
                {
                    if (saved.EquipmentSlot >= EquipmentSlots.Count)
                    {
                        skippedItems++;
                        continue;
                    }

                    while (equipment.Count <= saved.EquipmentSlot)
                        equipment.Add(ItemInstance.Empty);

                    equipment[saved.EquipmentSlot] = instance;
                }
            }
        }

        private static ItemSaveData ToSaveData(ItemInstance item, int equipmentSlot)
        {
            return new ItemSaveData
            {
                ItemName = item.ItemName,
                InstanceId = item.InstanceId,
                GridX = item.GridX,
                GridY = item.GridY,
                Width = item.Width,
                Height = item.Height,
                Durability = item.Durability,
                EquipmentSlot = equipmentSlot,
            };
        }
    }
}