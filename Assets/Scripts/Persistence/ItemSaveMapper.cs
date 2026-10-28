using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Maps live <see cref="ItemInstance"/> values to and from their save DTOs. Pruning and
    /// durability clamping live here so every item container loads the same way.
    /// </summary>
    internal static class ItemSaveMapper
    {
        public static void CaptureInto(
            List<ItemSaveData> target,
            IReadOnlyList<ItemInstance> bag,
            IReadOnlyList<ItemInstance> equipment)
        {
            if (bag != null)
            {
                for (int i = 0; i < bag.Count; i++)
                {
                    ItemInstance item = bag[i];
                    if (!item.IsEmpty)
                        target.Add(ToSaveData(item, -1));
                }
            }

            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    ItemInstance item = equipment[i];
                    if (!item.IsEmpty)
                        target.Add(ToSaveData(item, i));
                }
            }
        }

        public static void ApplyItems(
            IReadOnlyList<ItemSaveData> saved,
            out List<ItemInstance> bag,
            out List<ItemInstance> equipment,
            out int skippedItems)
        {
            bag = new List<ItemInstance>();
            equipment = new List<ItemInstance>();
            skippedItems = 0;

            if (saved == null)
                return;

            for (int i = 0; i < saved.Count; i++)
            {
                ItemSaveData entry = saved[i];
                if (!TryCreate(entry, out ItemInstance instance))
                {
                    skippedItems++;
                    continue;
                }

                if (entry.EquipmentSlot < 0)
                {
                    bag.Add(instance);
                    continue;
                }

                if (entry.EquipmentSlot >= EquipmentSlots.Count)
                {
                    skippedItems++;
                    continue;
                }

                while (equipment.Count <= entry.EquipmentSlot)
                    equipment.Add(ItemInstance.Empty);

                equipment[entry.EquipmentSlot] = instance;
            }
        }

        public static List<ItemSaveData> CaptureBank(IReadOnlyList<ItemInstance> bank)
        {
            var saved = new List<ItemSaveData>();
            if (bank == null)
                return saved;

            for (int i = 0; i < bank.Count; i++)
            {
                ItemInstance item = bank[i];
                if (!item.IsEmpty)
                    saved.Add(ToSaveData(item, -1));
            }

            return saved;
        }

        public static void ApplyBank(
            IReadOnlyList<ItemSaveData> saved,
            out List<ItemInstance> items,
            out int skippedItems)
        {
            items = new List<ItemInstance>();
            skippedItems = 0;

            if (saved == null)
                return;

            for (int i = 0; i < saved.Count; i++)
            {
                if (!TryCreate(saved[i], out ItemInstance instance))
                {
                    skippedItems++;
                    continue;
                }

                items.Add(instance);
            }
        }

        private static bool TryCreate(ItemSaveData saved, out ItemInstance instance)
        {
            instance = default;

            if (string.IsNullOrEmpty(saved.ItemName))
                return false;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(saved.ItemName);
            if (definition == null)
                return false;

            int maxDurability = definition.MaxDurability;
            int durability = saved.Durability <= 0 ? maxDurability : Mathf.Min(saved.Durability, maxDurability);

            instance = new ItemInstance
            {
                ItemName = saved.ItemName,
                InstanceId = string.IsNullOrEmpty(saved.InstanceId) ? ItemInstance.NewInstanceId() : saved.InstanceId,
                GridX = saved.GridX,
                GridY = saved.GridY,
                Width = saved.Width,
                Height = saved.Height,
                Durability = durability,
            };

            return true;
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
