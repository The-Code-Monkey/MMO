using System.Collections.Generic;
using MMO.Inventory;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Durability as it moves through the three shapes that carry it: a fresh instance starts at
    /// its definition's maximum, a save round-trips it (clamped to the definition), and the
    /// full-loot snapshot collects worn and carried gear without the empty holes between them.
    /// </summary>
    public sealed class DurabilityTests
    {
        private readonly List<ItemDefinition> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _created)
            {
                if (definition != null)
                    Object.DestroyImmediate(definition);
            }

            _created.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Create_StartsWithTheDefinitionsMaximumDurability()
        {
            ItemDefinition sword = NewDefinition("Dur.Sword", maxDurability: 40);

            ItemInstance instance = ItemInstance.Create(sword, 0, 0);

            Assert.AreEqual(40, instance.Durability);
        }

        [Test]
        public void Create_IndestructibleItem_StartsWithZero()
        {
            ItemDefinition rock = NewDefinition("Dur.Rock", maxDurability: 0);

            ItemInstance instance = ItemInstance.Create(rock, 0, 0);

            Assert.AreEqual(0, instance.Durability);
        }

        [Test]
        public void CaptureAndApply_RoundTripsWornDurability()
        {
            ItemDefinition sword = NewDefinition("Dur.Worn", maxDurability: 40);
            ItemInstance instance = ItemInstance.Create(sword, 1, 2);
            instance.Durability = 17;

            PlayerSaveRecord record = PlayerSaveMapper.Capture(
                "u-1", 1, PlayerStatsHealth(), 0, 0, Vector3.zero,
                bag: new[] { instance }, equipment: null);

            Assert.AreEqual(17, record.Items[0].Durability);

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out int skipped, out _);

            Assert.AreEqual(0, skipped);
            Assert.AreEqual(17, bag[0].Durability);
        }

        [Test]
        public void Apply_ClampsDurabilityToTheCurrentMaximum()
        {
            ItemDefinition sword = NewDefinition("Dur.Clamp", maxDurability: 20);
            var record = new PlayerSaveRecord
            {
                Items =
                {
                    new ItemSaveData
                    {
                        ItemName = "Dur.Clamp",
                        GridX = 0,
                        GridY = 0,
                        Width = 1,
                        Height = 1,
                        Durability = 999,
                        EquipmentSlot = -1,
                    },
                },
            };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out _, out _);

            Assert.AreEqual(20, bag[0].Durability, "A balance rollback can never leave an item over its own ceiling.");
        }

        [Test]
        public void Apply_ZeroDurabilityFromAnOldSave_ReadsAsFull()
        {
            ItemDefinition sword = NewDefinition("Dur.Legacy", maxDurability: 35);
            var record = new PlayerSaveRecord
            {
                Items =
                {
                    new ItemSaveData
                    {
                        ItemName = "Dur.Legacy",
                        Width = 1,
                        Height = 1,
                        Durability = 0,
                        EquipmentSlot = -1,
                    },
                },
            };

            PlayerSaveMapper.Apply(record, out List<ItemInstance> bag, out _, out _, out _);

            Assert.AreEqual(35, bag[0].Durability,
                "A save written before durability existed is restored fully repaired, not broken.");
        }

        [Test]
        public void CollectLootable_TakesBagThenGearAndSkipsEmptySlots()
        {
            ItemDefinition sword = NewDefinition("Dur.LootSword");
            ItemInstance carried = ItemInstance.Create(sword, 0, 0);
            ItemInstance worn = ItemInstance.Create(sword, 0, 0);

            var bag = new List<ItemInstance> { ItemInstance.Empty, carried };
            var equipment = new List<ItemInstance> { worn, ItemInstance.Empty };

            List<ItemInstance> loot = PlayerInventory.CollectLootable(bag, equipment);

            Assert.AreEqual(2, loot.Count, "Only the occupied cells drop.");
            Assert.AreSame(carried.ItemName, loot[0].ItemName);
            Assert.AreSame(worn.ItemName, loot[1].ItemName);
        }

        [Test]
        public void CollectLootable_EmptyContainers_YieldNothing()
        {
            Assert.IsEmpty(PlayerInventory.CollectLootable(new List<ItemInstance>(), null));
        }

        private static int PlayerStatsHealth() => MMO.Gameplay.PlayerStats.MaxHealth;

        private ItemDefinition NewDefinition(string name, int maxDurability = 0)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            _created.Add(definition);
            TestItemDefinitions.Write(definition, name, maxDurability: maxDurability);
            return definition;
        }
    }
}
