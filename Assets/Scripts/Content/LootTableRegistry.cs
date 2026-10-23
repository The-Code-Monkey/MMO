using System.Collections.Generic;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// Resolves a loot table id back to its asset. Mirrors <see cref="QuestRegistry"/> and the
    /// project's other registries: self-registration plus a <c>Resources</c> fallback.
    /// </summary>
    public static class LootTableRegistry
    {
        public const string LootTablesFolder = "Content/LootTables";

        private static readonly Dictionary<string, WeightedLootTable> Registered = new();

        public static int Count => Registered.Count;

        public static void Register(WeightedLootTable table)
        {
            if (table == null || string.IsNullOrEmpty(table.TableId))
                return;

            if (Registered.TryGetValue(table.TableId, out WeightedLootTable existing) && existing != table)
            {
                Debug.LogWarning(
                    $"{nameof(LootTableRegistry)}: '{table.TableId}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{table.name}'.");
            }

            Registered[table.TableId] = table;
        }

        public static void Unregister(WeightedLootTable table)
        {
            if (table == null || string.IsNullOrEmpty(table.TableId))
                return;

            if (Registered.TryGetValue(table.TableId, out WeightedLootTable existing) && existing == table)
                Registered.Remove(table.TableId);
        }

        public static WeightedLootTable Resolve(string tableId)
        {
            if (string.IsNullOrEmpty(tableId))
                return null;

            if (Registered.TryGetValue(tableId, out WeightedLootTable table) && table != null)
                return table;

            WeightedLootTable loaded = Resources.Load<WeightedLootTable>($"{LootTablesFolder}/{tableId}");
            if (loaded != null)
                Registered[tableId] = loaded;

            return loaded;
        }

        public static void Clear() => Registered.Clear();

        /// <summary>A point-in-time snapshot of every registered table, nulls skipped.</summary>
        public static IReadOnlyList<WeightedLootTable> All
        {
            get
            {
                var snapshot = new List<WeightedLootTable>(Registered.Count);
                foreach (KeyValuePair<string, WeightedLootTable> entry in Registered)
                {
                    if (entry.Value != null)
                        snapshot.Add(entry.Value);
                }

                return snapshot;
            }
        }

        public static void LoadAllFromResources()
        {
            foreach (WeightedLootTable table in Resources.LoadAll<WeightedLootTable>(LootTablesFolder))
                Register(table);
        }
    }
}
