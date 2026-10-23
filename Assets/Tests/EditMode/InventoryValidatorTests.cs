using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The level gate of the server's equip verification. This is the issue's own worked example
    /// made executable: a level 5 player may not equip a level 10 staff, and the answer comes
    /// from a pure function an EditMode test can reach with no server, no socket, and no spawn.
    /// </summary>
    public sealed class InventoryValidatorTests
    {
        private ItemDefinition _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);

            // Destroying the asset unregisters it via OnDisable; clearing removes any entry a
            // failed teardown left behind so no test inherits another test's items.
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Inventory_EquipItem_FailsWhenLevelRequirementNotMet()
        {
            // The exact scenario agents.md specifies: level 5 player, level 10 staff.
            ItemDefinition highLevelStaff = CreateDefinition("Validation Staff", requiredLevel: 10);
            int playerLevel = 5;

            bool result = InventoryValidator.CanEquip(playerLevel, highLevelStaff);

            Assert.IsFalse(result, "A level 5 player must not be able to equip a level 10 item.");
        }

        [Test]
        public void Inventory_EquipItem_SucceedsAtExactlyTheRequiredLevel()
        {
            // The boundary: requirements are inclusive, "requires 10" means 10 is enough.
            ItemDefinition staff = CreateDefinition("Validation Staff", requiredLevel: 10);

            Assert.IsTrue(InventoryValidator.CanEquip(10, staff));
            Assert.IsTrue(InventoryValidator.CanEquip(11, staff),
                "A player above the requirement may always equip the item.");
        }

        [Test]
        public void Inventory_EquipItem_AcceptsAnyLevelWhenNoRequirementIsSet()
        {
            ItemDefinition bare = CreateDefinition("Validation Rust", requiredLevel: 0);

            Assert.IsTrue(InventoryValidator.CanEquip(1, bare));
        }

        [Test]
        public void Inventory_EquipItem_FailsForMissingDefinition()
        {
            // An item the server cannot resolve is data the server must refuse, not interpret.
            Assert.IsFalse(InventoryValidator.CanEquip(99, null));
        }

        [Test]
        public void Inventory_EquipItem_FailsForABagItemWithNoWearableSlot()
        {
            var donkey = ScriptableObject.CreateInstance<ItemDefinition>();
            _created = donkey;
            TestItemDefinitions.Write(donkey, "Donkey", equipSlot: EquipmentSlot.None);

            Assert.IsFalse(InventoryValidator.CanEquip(playerLevel: 99, definition: donkey),
                "A mount is carried, not worn; equipping it must never land in MainHand.");
        }

        [Test]
        public void Inventory_EquipItem_AcceptsAMountSlotItem()
        {
            var donkey = ScriptableObject.CreateInstance<ItemDefinition>();
            _created = donkey;
            TestItemDefinitions.Write(donkey, "Donkey", equipSlot: EquipmentSlot.Mount);

            Assert.IsTrue(InventoryValidator.CanEquip(playerLevel: 1, definition: donkey),
                "A mount item must be equippable into the mount slot.");
        }

        [Test]
        public void InventoryValidator_IsAStaticPureHelper()
        {
            // Pure statics are the project's testing contract: reachable without a scene.
            Assert.IsTrue(typeof(InventoryValidator).IsAbstract && typeof(InventoryValidator).IsSealed,
                "InventoryValidator must be a static class.");
        }

        private ItemDefinition CreateDefinition(string name, int requiredLevel)
        {
            _created = ScriptableObject.CreateInstance<ItemDefinition>();
            TestItemDefinitions.Write(_created, name, requiredLevel: requiredLevel);
            return _created;
        }
    }
}
