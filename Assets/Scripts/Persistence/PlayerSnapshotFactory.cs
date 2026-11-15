using MMO.Auction;
using MMO.Banking;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Progression;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>Builds one save snapshot from the live player components.</summary>
    internal static class PlayerSnapshotFactory
    {
        public static PlayerSaveRecord Capture(
            string accountId,
            PlayerStats stats,
            PlayerInventory inventory,
            PlayerQuestLog quests,
            PlayerWallet wallet,
            PlayerBank bank,
            PlayerAchievements achievements,
            Vector3 position)
        {
            PlayerSaveRecord record = PlayerSaveMapper.Capture(
                accountId,
                stats.Level,
                stats.Health,
                stats.Mana,
                wallet != null ? wallet.Gold : 0,
                position,
                inventory.Items,
                inventory.Equipment,
                stats.Experience,
                quests != null ? quests.CompletedQuestIds : null,
                quests != null ? quests.Progress : null);
            wallet?.CaptureToSave(record);
            bank?.CaptureToSave(record);
            record.Achievements = achievements != null
                ? achievements.CaptureSave()
                : new System.Collections.Generic.List<AchievementSaveData>();
            return record;
        }
    }
}
