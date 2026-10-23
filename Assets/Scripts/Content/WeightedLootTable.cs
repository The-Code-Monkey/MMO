using System;
using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// One independent drop chance. <see cref="Chance"/> is a probability, not a normalized
    /// weight: 0.5 means the entry rolls at 50% regardless of how many other entries the table has.
    /// </summary>
    [Serializable]
    public struct LootEntry
    {
        public ItemDefinition Item;
        [Range(0f, 1f)] public float Chance;
        [Min(1)] public int MinCount;
        [Min(1)] public int MaxCount;
    }

    /// <summary>One rolled drop, ready to be awarded to a bag.</summary>
    public readonly struct LootDrop
    {
        public LootDrop(ItemDefinition item, int count)
        {
            Item = item;
            Count = count;
        }

        public ItemDefinition Item { get; }
        public int Count { get; }
    }

    /// <summary>
    /// A data-authored loot table. The server rolls it on a mob death or chest open; the client
    /// never sees the table, only the items that end up in a container.
    /// </summary>
    [CreateAssetMenu(fileName = "New Weighted Loot Table", menuName = "MMO/Weighted Loot Table")]
    public sealed class WeightedLootTable : ScriptableObject
    {
        [SerializeField] private string _tableId = string.Empty;
        [SerializeField] private LootEntry[] _entries = Array.Empty<LootEntry>();

        public string TableId => _tableId;
        public IReadOnlyList<LootEntry> Entries => _entries ?? Array.Empty<LootEntry>();

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_tableId))
                LootTableRegistry.Register(this);
        }

        private void OnDisable() => LootTableRegistry.Unregister(this);
    }
}
