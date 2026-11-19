using System.Collections.Generic;
using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Progression;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>Applies a loaded save record to a spawned player's live components in order.</summary>
    internal static class PlayerSaveApplier
    {
        public static void Apply(
            PlayerSaveRecord record,
            string accountId,
            PlayerStats stats,
            PlayerInventory inventory,
            PlayerQuestLog quests,
            PlayerWallet wallet,
            PlayerBank bank,
            PlayerAchievements achievements,
            PlayerCrafting crafting)
        {
            PlayerSaveMapper.Apply(
                record,
                out List<ItemInstance> bag,
                out List<ItemInstance> equipment,
                out int skippedItems,
                out long gold,
                out int experience,
                out List<string> completedQuestIds,
                out List<QuestProgressEntry> questProgress);

            if (skippedItems > 0)
            {
                Debug.LogWarning(
                    $"{nameof(PlayerSaveTrigger)}: dropped {skippedItems} item(s) from '{accountId}' that no longer have a definition.");
            }

            if (record != null)
                stats.ApplySave(record.Health, record.Mana, record.Level);

            stats.ApplyExperience(experience);
            inventory.Restore(bag, equipment);
            quests?.Restore(completedQuestIds, questProgress);
            wallet?.InitializeFromSave(record);
            bank?.InitializeFromSave(record);
            achievements?.Restore(record?.Achievements);
            crafting?.RestoreFromSave(record != null ? record.CraftingXp : 0, record?.CraftOrders);
        }
    }
}
