using System.Collections.Generic;
using FishNet.Object;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pure surface of the inventory container: its grid dimensions, its read-only views of
    /// the two containers, and the bonus-summing helpers that turn "what is worn" into the caps
    /// <c>PlayerStats</c> clamps against. The <c>[Server]</c> mutators are deliberately not
    /// called here — FishNet's codegen refuses them without a live server, and the behavioural
    /// half belongs to the PlayMode suite, where a real connection exists.
    /// </summary>
    public sealed class PlayerInventoryTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [Test]
        public void PlayerInventory_IsANetworkBehaviour()
        {
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(PlayerInventory).BaseType,
                "Only a NetworkBehaviour can own SyncTypes and ServerRpcs.");
        }

        [Test]
        public void PlayerInventory_GridIsEightBySix()
        {
            // The issue's container: a fixed grid, sized as a constant both the server and any
            // bag UI can agree on without configuration.
            Assert.AreEqual(8, PlayerInventory.GridWidth);
            Assert.AreEqual(6, PlayerInventory.GridHeight);
            Assert.AreEqual(48, PlayerInventory.GridWidth * PlayerInventory.GridHeight);
        }

        [Test]
        public void UnspawnedInventory_ReadsEmptyContainersRatherThanThrowing()
        {
            PlayerInventory inventory = AttachInventory();

            Assert.AreEqual(0, inventory.Items.Count, "No SyncType writes happen before the network starts.");
            Assert.AreEqual(0, inventory.Equipment.Count);
            Assert.IsTrue(inventory.GetInventoryItem(0).IsEmpty);
            Assert.IsTrue(inventory.GetInventoryItem(-1).IsEmpty, "Out-of-range reads are empty, not exceptions.");
            Assert.IsTrue(inventory.GetItemAt(0, 0).IsEmpty);
            Assert.IsTrue(inventory.GetEquipped(EquipmentSlot.MainHand).IsEmpty);
            Assert.IsTrue(inventory.GetEquipped((EquipmentSlot)99).IsEmpty,
                "A garbage slot cast reads as empty instead of indexing out of range.");
        }

        [Test]
        public void ComputeMaxHealthBonus_SumsOnlyTheBonusesPresent()
        {
            List<ItemDefinition> worn = new()
            {
                TestItemDefinitions.Create("Helm A", maxHealthBonus: 10),
                TestItemDefinitionWithoutBonus(),
                TestItemDefinitions.Create("Helm B", maxHealthBonus: 5),
            };

            try
            {
                Assert.AreEqual(15, PlayerInventory.ComputeMaxHealthBonus(worn));
            }
            finally
            {
                foreach (ItemDefinition definition in worn)
                {
                    if (definition != null)
                        Object.DestroyImmediate(definition);
                }

                ItemDefinitionRegistry.Clear();
            }
        }

        [Test]
        public void ComputeMaxManaBonus_SumsOnlyTheBonusesPresent()
        {
            List<ItemDefinition> worn = new()
            {
                TestItemDefinitions.Create("Ring", maxManaBonus: 20),
                TestItemDefinitions.Create("Amulet", maxManaBonus: 30),
            };

            try
            {
                Assert.AreEqual(50, PlayerInventory.ComputeMaxManaBonus(worn));
            }
            finally
            {
                foreach (ItemDefinition definition in worn)
                    Object.DestroyImmediate(definition);

                ItemDefinitionRegistry.Clear();
            }
        }

        [Test]
        public void BonusHelpers_TolerateNullListsAndNullEntries()
        {
            // Recalculation runs against a freshly built list; a helper that throws on the empty
            // case would take the server's equip path down with it.
            Assert.AreEqual(0, PlayerInventory.ComputeMaxHealthBonus(null));
            Assert.AreEqual(0, PlayerInventory.ComputeMaxManaBonus(null));

            List<ItemDefinition> withNull = new() { null, TestItemDefinitions.Create("Only One", maxHealthBonus: 7) };

            try
            {
                Assert.AreEqual(7, PlayerInventory.ComputeMaxHealthBonus(withNull));
            }
            finally
            {
                if (withNull[1] != null)
                    Object.DestroyImmediate(withNull[1]);

                ItemDefinitionRegistry.Clear();
            }
        }

        private static ItemDefinition TestItemDefinitionWithoutBonus()
        {
            return TestItemDefinitions.Create("Plain Filler");
        }

        private PlayerInventory AttachInventory()
        {
            _go = new GameObject("PlayerInventoryUnderTest");
            return _go.AddComponent<PlayerInventory>();
        }
    }
}
