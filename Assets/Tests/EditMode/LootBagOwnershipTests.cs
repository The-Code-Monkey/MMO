using System.Collections.Generic;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests.EditMode
{
    /// <summary>
    /// Tests that LootBag fill/loot operations route through the ItemOwnershipRegistry.
    /// </summary>
    public sealed class LootBagOwnershipTests
    {
        private ItemOwnershipRegistry _registry;
        private LootBag _lootBag;
        private PlayerInventory _inventory;
        private string _playerHolderId = "player-account-456";

        [SetUp]
        public void SetUp()
        {
            _registry = new ItemOwnershipRegistry();

            _lootBag = new GameObject("LootBag").AddComponent<LootBag>();
            _lootBag.SetInteractRange(10f);
            _lootBag._registry = _registry;

            _inventory = new GameObject("Player").AddComponent<PlayerInventory>();
            _inventory.SetHolderId(_playerHolderId);
            _inventory._registry = _registry;
        }

        [TearDown]
        public void TearDown()
        {
            if (_lootBag != null)
                Object.DestroyImmediate(_lootBag.gameObject);
            if (_inventory != null)
                Object.DestroyImmediate(_inventory.gameObject);
        }

        [Test]
        public void Fill_GrantsOwnershipToLootBag()
        {
            var def = TestItemDefinitions.Create("LootedSword", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 100);

            var item = ItemInstance.Create(def, 0, 0);
            var instanceId = item.InstanceId;

            _lootBag.Fill(new List<ItemInstance> { item });

            Assert.AreEqual(1, _lootBag.Contents.Count);
            Assert.IsTrue(_registry.IsHeldBy(instanceId, _lootBag.HolderId), "LootBag should hold the item after Fill");
        }

        [Test]
        public void Loot_TransfersOwnershipFromLootBagToPlayer()
        {
            var def = TestItemDefinitions.Create("LootedShield", 1, 1, EquipmentSlot.OffHand, 0, 0, 0, 50);

            var item = ItemInstance.Create(def, 0, 0);
            var instanceId = item.InstanceId;

            _lootBag.Fill(new List<ItemInstance> { item });
            Assert.IsTrue(_registry.IsHeldBy(instanceId, _lootBag.HolderId));

            Assert.IsTrue(_lootBag.Loot(_inventory));

            Assert.AreEqual(0, _lootBag.Contents.Count);
            Assert.AreEqual(1, _inventory.Items.Count);
            Assert.IsTrue(_registry.IsHeldBy(instanceId, _playerHolderId), "Player should hold the item after Loot");
            Assert.IsFalse(_registry.IsHeldBy(instanceId, _lootBag.HolderId), "LootBag should no longer hold the item");
        }

        [Test]
        public void Loot_MultipleItems_AllTransferred()
        {
            var def1 = TestItemDefinitions.Create("ItemA", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 10);
            var def2 = TestItemDefinitions.Create("ItemB", 1, 1, EquipmentSlot.OffHand, 0, 0, 0, 20);

            var item1 = ItemInstance.Create(def1, 0, 0);
            var item2 = ItemInstance.Create(def2, 1, 0);
            var id1 = item1.InstanceId;
            var id2 = item2.InstanceId;

            _lootBag.Fill(new List<ItemInstance> { item1, item2 });
            Assert.IsTrue(_registry.IsHeldBy(id1, _lootBag.HolderId));
            Assert.IsTrue(_registry.IsHeldBy(id2, _lootBag.HolderId));

            Assert.IsTrue(_lootBag.Loot(_inventory));

            Assert.AreEqual(2, _inventory.Items.Count);
            Assert.IsTrue(_registry.IsHeldBy(id1, _playerHolderId));
            Assert.IsTrue(_registry.IsHeldBy(id2, _playerHolderId));
            Assert.IsFalse(_registry.IsHeldBy(id1, _lootBag.HolderId));
            Assert.IsFalse(_registry.IsHeldBy(id2, _lootBag.HolderId));
        }

        [Test]
        public void Loot_PartialFit_OnlyFittingItemsTransferred()
        {
            var smallDef = TestItemDefinitions.Create("SmallItem", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 10);
            var largeDef = TestItemDefinitions.Create("LargeItem", 3, 3, EquipmentSlot.OffHand, 0, 0, 0, 20);

            var small = ItemInstance.Create(smallDef, 0, 0);
            var large = ItemInstance.Create(largeDef, 0, 0);
            var smallId = small.InstanceId;
            var largeId = large.InstanceId;

            _lootBag.Fill(new List<ItemInstance> { small, large });

            // Fill player bag to only have room for small item
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (x == 0 && y == 0) continue; // leave one slot
                    var filler = ItemInstance.Create(smallDef, x, y);
                    _inventory.TryAddExistingItem(filler);
                }
            }

            Assert.IsTrue(_lootBag.Loot(_inventory));

            // Only small item should fit
            Assert.IsTrue(_registry.IsHeldBy(smallId, _playerHolderId));
            Assert.IsTrue(_registry.IsHeldBy(largeId, _lootBag.HolderId), "Large item should remain in loot bag");
            Assert.AreEqual(1, _lootBag.Contents.Count);
        }
    }
}