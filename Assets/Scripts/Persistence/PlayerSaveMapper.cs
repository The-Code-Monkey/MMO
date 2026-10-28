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

            QuestSaveMapper.CaptureInto(
                record.CompletedQuestIds, record.QuestProgress, completedQuestIds, questProgress);
            ItemSaveMapper.CaptureInto(record.Items, bag, equipment);
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
            gold = 0;
            experience = 0;

            if (record == null)
            {
                ItemSaveMapper.ApplyItems(null, out bag, out equipment, out skippedItems);
                QuestSaveMapper.Apply(null, out completedQuestIds, out questProgress);
                return;
            }

            gold = record.Gold;
            experience = Mathf.Max(0, record.Experience);
            ItemSaveMapper.ApplyItems(record.Items, out bag, out equipment, out skippedItems);
            QuestSaveMapper.Apply(record, out completedQuestIds, out questProgress);
        }

        /// <summary>
        /// Captures a personal bank's items. Bank items are stored exactly like bag items except
        /// that they are never worn, so every entry carries slot -1.
        /// </summary>
        public static List<ItemSaveData> CaptureBank(IReadOnlyList<ItemInstance> bank)
            => ItemSaveMapper.CaptureBank(bank);

        /// <summary>
        /// Rebuilds a personal bank's items. Entries with no name, or whose definition no longer
        /// resolves, are skipped and counted rather than resurrected as ghost items.
        /// </summary>
        public static void ApplyBank(
            IReadOnlyList<ItemSaveData> saved,
            out List<ItemInstance> items,
            out int skippedItems)
            => ItemSaveMapper.ApplyBank(saved, out items, out skippedItems);
    }
}
