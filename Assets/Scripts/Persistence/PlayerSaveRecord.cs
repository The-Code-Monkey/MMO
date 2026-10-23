using System;
using System.Collections.Generic;
using MMO.Content;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// One item as persisted. Matches the runtime <c>ItemInstance</c> wire shape (name + grid
    /// cell + footprint) and adds the <see cref="EquipmentSlot"/> a worn item occupied, or -1 for
    /// a bag item. The name is the <see cref="Inventory.ItemDefinitionRegistry"/> key both ends
    /// resolve locally; the definition itself never travels.
    /// </summary>
    [Serializable]
    public struct ItemSaveData
    {
        public string ItemName;

        /// <summary>
        /// The server-assigned identity of the item instance. Older saves predate it and carry an
        /// empty string; the mapper mints a fresh id on load so the dupe guard can track the item
        /// from the moment it enters the world.
        /// </summary>
        public string InstanceId;

        public int GridX;
        public int GridY;
        public int Width;
        public int Height;

        /// <summary>Remaining durability at save time; zero for an indestructible item.</summary>
        public int Durability;

        /// <summary>-1 = bag item, otherwise the <see cref="Inventory.EquipmentSlot"/> it was worn in.</summary>
        public int EquipmentSlot;
    }

    /// <summary>
    /// The single "source of truth" a connected player's state is round-tripped through. Replaces
    /// the old write-only <c>PlayerSaveData</c>/<c>InventorySaveData</c> pair with one record that
    /// can actually be loaded back: vitals, level, experience, world position (reserved — no
    /// movement system saves position yet), both inventory containers, and quest progress in one
    /// flat shape.
    ///
    /// A record is owned by the <em>server's</em> storage session: the database key is the player's
    /// stable <c>AccountId</c>, but the storage object itself belongs to the game-server account, so
    /// any number of server instances sharing that account can read and write every player. That is
    /// what makes the optimistic <see cref="StorageVersion"/> check meaningful — two servers racing
    /// a write collide on the version and one of them retries instead of silently clobbering.
    ///
    /// <see cref="StorageVersion"/> is the backend's revision hash rather than player data; it is
    /// carried on the record so the IO layer can append it to the value without a second lookup.
    /// </summary>
    [Serializable]
    public class PlayerSaveRecord
    {
        /// <summary>Bump when the shape below stops round-tripping old saves.</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>The player's stable account key; the storage key this record lives under.</summary>
        public string UserId;

        public int SchemaVersion = CurrentSchemaVersion;

        public int Level = PlayerStats.DefaultLevel;

        /// <summary>Clamped to the caps in force when applied; never trusted past them.</summary>
        public int Health = PlayerStats.MaxHealth;

        public int Mana = PlayerStats.MaxMana;

        /// <summary>Quest/event experience. Not a level curve; the level travels separately.</summary>
        public int Experience = 0;

        /// <summary>Reserved. No movement save exists yet, so it round-trips but is not applied.</summary>
        public Vector3 Position;

/// <summary>The backend's revision hash for optimistic concurrency; not player data.</summary>
        public string StorageVersion;

        /// <summary>Player's gold currency.</summary>
        public long Gold = 0;

        public List<ItemSaveData> Items = new List<ItemSaveData>();

        /// <summary>Quest ids that have already completed and paid their reward.</summary>
        public List<string> CompletedQuestIds = new List<string>();

        /// <summary>Active objective counters. Completed quests may still have rows here.</summary>
        public List<QuestProgressEntry> QuestProgress = new List<QuestProgressEntry>();
}
}