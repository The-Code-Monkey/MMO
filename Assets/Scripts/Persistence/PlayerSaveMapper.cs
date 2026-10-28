using System.Collections.Generic;
using MMO.Content;
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
            IReadOnlyList<ItemInstance> equipment,
            int experience = 0,
            IReadOnlyList<string> completedQuestIds = null,
            IReadOnlyList<QuestProgressEntry> questProgress = null)
        {
            var record = new PlayerSaveRecord
            {
                UserId = userId,
                Level = level,
                Health = health,
                Mana = mana,
                Experience = Mathf.Max(0, experience),
                Gold = gold,
                Position = position,
            };

            if (completedQuestIds != null)
            {
                for (int i = 0; i < completedQuestIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(completedQuestIds[i]))
                        record.CompletedQuestIds.Add(completedQuestIds[i]);
                }
            }

            if (questProgress != null)
            {
                for (int i = 0; i < questProgress.Count; i++)
                {
                    QuestProgressEntry entry = questProgress[i];
                    if (!string.IsNullOrEmpty(entry.QuestId))
                    {
                        entry.Count = Mathf.Max(0, entry.Count);
                        record.QuestProgress.Add(entry);
                    }
                }
            }

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
            Apply(record, out bag, out equipment, out skippedItems, out gold,
                out _, out _, out _);
        }

        /// <summary>
        /// Rebuilds the containers and returns the XP and quest content alongside them. The
        /// original overload above stays in place for callers that only care about items.
        /// </summary>
        public static void Apply(
            PlayerSaveRecord record,
            out List<ItemInstance> bag,
            out List<ItemInstance> equipment,
            out int skippedItems,
            out long gold,
            out int experience,
            out List<string> completedQuestIds,
            out List<QuestProgressEntry> questProgress)
        {
            bag = new List<ItemInstance>();
            equipment = new List<ItemInstance>();
            skippedItems = 0;
            gold = 0;
            experience = 0;
            completedQuestIds = new List<string>();
            questProgress = new List<QuestProgressEntry>();

            if (record == null)
                return;

            gold = record.Gold;
            experience = Mathf.Max(0, record.Experience);

            if (record.CompletedQuestIds != null)
            {
                for (int i = 0; i < record.CompletedQuestIds.Count; i++)
                {
                    if (!string.IsNullOrEmpty(record.CompletedQuestIds[i]))
                        completedQuestIds.Add(record.CompletedQuestIds[i]);
                }
            }

            if (record.QuestProgress != null)
            {
                for (int i = 0; i < record.QuestProgress.Count; i++)
                {
                    QuestProgressEntry entry = record.QuestProgress[i];
                    if (!string.IsNullOrEmpty(entry.QuestId))
                    {
                        entry.Count = Mathf.Max(0, entry.Count);
                        questProgress.Add(entry);
                    }
                }
            }

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

        /// <summary>
        /// Captures a personal bank's items. Bank items are stored exactly like bag items except
        /// that they are never worn, so every entry carries slot -1.
        /// </summary>
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

        /// <summary>
        /// Rebuilds a personal bank's items. Entries with no name, or whose definition no longer
        /// resolves, are skipped and counted rather than resurrected as ghost items.
        /// </summary>
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
                ItemSaveData entry = saved[i];
                if (string.IsNullOrEmpty(entry.ItemName))
                {
                    skippedItems++;
                    continue;
                }

                ItemDefinition definition = ItemDefinitionRegistry.Resolve(entry.ItemName);
                if (definition == null)
                {
                    skippedItems++;
                    continue;
                }

                int maxDurability = definition.MaxDurability;
                int durability = entry.Durability <= 0
                    ? maxDurability
                    : Mathf.Min(entry.Durability, maxDurability);

                items.Add(new ItemInstance
                {
                    ItemName = entry.ItemName,
                    InstanceId = string.IsNullOrEmpty(entry.InstanceId) ? ItemInstance.NewInstanceId() : entry.InstanceId,
                    GridX = entry.GridX,
                    GridY = entry.GridY,
                    Width = entry.Width,
                    Height = entry.Height,
                    Durability = durability,
                });
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