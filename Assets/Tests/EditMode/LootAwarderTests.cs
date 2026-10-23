using System.Collections.Generic;
using MMO.Content;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The server award path: fill the recipient first, and report every unit that did not fit so
    /// the caller can put it in a bag instead of losing it.
    /// </summary>
    public sealed class LootAwarderTests
    {
        private GameObject _player;
        private PlayerInventory _inventory;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("LootAwarderPlayer");
            _inventory = _player.AddComponent<PlayerInventory>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
                Object.DestroyImmediate(_player);

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Award_PutsDropsInTheInventory()
        {
            ItemDefinition ore = TestItemDefinitions.Create("AwardOre");
            ItemDefinition leather = TestItemDefinitions.Create("AwardLeather");

            LootAwardResult result = LootAwarder.Award(
                new[] { new LootDrop(ore, 2), new LootDrop(leather, 1) },
                _inventory, manager: null, position: Vector3.zero, preferBag: false);

            Assert.AreEqual(3, result.AddedToInventory);
            Assert.IsEmpty(result.Leftovers);
            Assert.IsNull(result.Bag);
            Assert.AreEqual(2, _inventory.CountItem("AwardOre"));
            Assert.AreEqual(1, _inventory.CountItem("AwardLeather"));
        }

        [Test]
        public void Award_FullBag_ReportsEveryItemAsLeftover()
        {
            ItemDefinition filler = TestItemDefinitions.Create("AwardFiller");
            ItemDefinition drop = TestItemDefinitions.Create("AwardDrop");
            FillBag(filler);

            LootAwardResult result = LootAwarder.Award(
                new[] { new LootDrop(drop, 2) },
                _inventory, manager: null, position: Vector3.zero, preferBag: false);

            Assert.AreEqual(0, result.AddedToInventory);
            Assert.AreEqual(2, result.Leftovers.Count);
            Assert.IsNull(result.Bag, "No manager was supplied, so no bag could be spawned.");
            Assert.AreEqual("AwardDrop", result.Leftovers[0].ItemName);
        }

        [Test]
        public void Award_PartialFit_OnlyTheLeftoverIsReported()
        {
            ItemDefinition filler = TestItemDefinitions.Create("AwardFiller");
            ItemDefinition drop = TestItemDefinitions.Create("AwardDrop");
            FillBag(filler);
            Assert.IsTrue(_inventory.RemoveItemAt(0, 0), "Free the first cell for one drop.");

            LootAwardResult result = LootAwarder.Award(
                new[] { new LootDrop(drop, 2) },
                _inventory, manager: null, position: Vector3.zero, preferBag: false);

            Assert.AreEqual(1, result.AddedToInventory);
            Assert.AreEqual(1, result.Leftovers.Count);
            Assert.AreEqual(1, _inventory.CountItem("AwardDrop"));
        }

        [Test]
        public void Award_PreferBag_LeavesTheInventoryAlone()
        {
            ItemDefinition drop = TestItemDefinitions.Create("AwardDrop");

            LootAwardResult result = LootAwarder.Award(
                new[] { new LootDrop(drop, 3) },
                _inventory, manager: null, position: Vector3.zero, preferBag: true);

            Assert.AreEqual(0, result.AddedToInventory);
            Assert.AreEqual(3, result.Leftovers.Count);
            Assert.AreEqual(0, _inventory.CountItem("AwardDrop"));
        }

        [Test]
        public void Award_IgnoresEmptyDrops()
        {
            ItemDefinition drop = TestItemDefinitions.Create("AwardDrop");

            LootAwardResult result = LootAwarder.Award(
                new[] { new LootDrop(drop, 0), new LootDrop(null, 2) },
                _inventory, manager: null, position: Vector3.zero, preferBag: false);

            Assert.AreEqual(0, result.AddedToInventory);
            Assert.IsEmpty(result.Leftovers);
            Assert.AreEqual(0, _inventory.ItemCount);
        }

        private void FillBag(ItemDefinition filler)
        {
            while (_inventory.TryAddItem(filler))
            {
            }
        }
    }
}
