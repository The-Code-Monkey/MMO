using System.Collections.Generic;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests.EditMode
{
    /// <summary>
    /// Tests that PlayerInventory mutations route through the ItemOwnershipRegistry.
    /// </summary>
    public sealed class PlayerInventoryOwnershipTests
    {
        private ItemOwnershipRegistry _registry;
        private PlayerInventory _inventory;
        private string _holderId = "test-account-123";

        [SetUp]
        public void SetUp()
        {
            _registry = new ItemOwnershipRegistry();
            _inventory = new GameObject().AddComponent<PlayerInventory>();
            _inventory.SetHolderId(_holderId);

            // Inject the registry directly via reflection (bypassing SocialHubService)
            var registryField = typeof(PlayerInventory).GetField("Registry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            registryField.SetValue(_inventory, _registry);
        }

        [TearDown]
        public void TearDown()
        {
            if (_inventory != null)
                Object.DestroyImmediate(_inventory.gameObject);
        }

        [Test]
        public void TryAddItem_GrantsOwnership()
        {
            var def = TestItemDefinitions.Create("TestSword", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 100);

            Assert.IsTrue(_inventory.TryAddItem(def));
            Assert.AreEqual(1, _inventory.Items.Count);
            Assert.IsTrue(_registry.IsHeldBy(_inventory.Items[0].InstanceId, _holderId));
        }

        [Test]
        public void TryAddExistingItem_GrantsOwnership()
        {
            var def = TestItemDefinitions.Create("TestShield", 1, 1, EquipmentSlot.OffHand, 0, 0, 0, 50);

            var item = ItemInstance.Create(def, 0, 0);
            Assert.IsTrue(_inventory.TryAddExistingItem(item));
            Assert.IsTrue(_registry.IsHeldBy(item.InstanceId, _holderId));
        }

        [Test]
        public void RemoveItemAt_ReleasesOwnership()
        {
            var def = TestItemDefinitions.Create("TestPotion", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 0);

            _inventory.TryAddItem(def);
            var instanceId = _inventory.Items[0].InstanceId;
            Assert.IsTrue(_registry.IsHeldBy(instanceId, _holderId));

            Assert.IsTrue(_inventory.RemoveItemAt(0, 0));
            Assert.IsFalse(_registry.IsHeldBy(instanceId, _holderId));
            Assert.AreEqual(0, _inventory.Items.Count);
        }

        [Test]
        public void ClearAll_ReleasesAllItems()
        {
            var def = TestItemDefinitions.Create("TestItem", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 10);
            var def2 = TestItemDefinitions.Create("TestItem2", 1, 1, EquipmentSlot.OffHand, 0, 0, 0, 10);

            _inventory.TryAddItem(def);
            _inventory.TryAddItem(def2);

            var ids = new List<string>();
            foreach (var item in _inventory.Items)
                if (!item.IsEmpty) ids.Add(item.InstanceId);

            _inventory.ClearAll();

            foreach (var id in ids)
                Assert.IsFalse(_registry.IsHeldBy(id, _holderId), $"Item {id} should have been released");
            Assert.AreEqual(0, _inventory.Items.Count);
        }

        [Test]
        public void Restore_GrantsAllRestoredItems()
        {
            var def = TestItemDefinitions.Create("SavedSword", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 100);

            var savedItem = ItemInstance.Create(def, 0, 0);
            var bag = new List<ItemInstance> { savedItem };
            var equipment = new List<ItemInstance> { ItemInstance.Empty };

            _inventory.Restore(bag, equipment);

            Assert.AreEqual(1, _inventory.Items.Count);
            Assert.IsTrue(_registry.IsHeldBy(savedItem.InstanceId, _holderId));
        }

        [Test]
        public void TryCraft_ReleasesIngredientsAndGrantsOutput()
        {
            var ironDef = TestItemDefinitions.Create("IronOre", 1, 1, EquipmentSlot.MainHand, 0, 0, 0, 0);
            var swordDef = TestItemDefinitions.Create("IronSword", 1, 2, EquipmentSlot.MainHand, 0, 0, 0, 100);

            var recipe = ScriptableObject.CreateInstance<MMO.Crafting.RecipeData>();
            var recipeSO = new UnityEditor.SerializedObject(recipe);
            recipeSO.FindProperty("_output").objectReferenceValue = swordDef;
            recipeSO.FindProperty("_outputCount").intValue = 1;
            var ingredientsProp = recipeSO.FindProperty("_ingredients");
            ingredientsProp.arraySize = 1;
            ingredientsProp.GetArrayElementAtIndex(0).FindPropertyRelative("_item").objectReferenceValue = ironDef;
            ingredientsProp.GetArrayElementAtIndex(0).FindPropertyRelative("_count").intValue = 2;
            recipeSO.ApplyModifiedPropertiesWithoutUndo();

            _inventory.TryAddItem(ironDef);
            _inventory.TryAddItem(ironDef);

            var ironIds = new List<string>();
            foreach (var item in _inventory.Items)
                if (!item.IsEmpty) ironIds.Add(item.InstanceId);

            Assert.IsTrue(_inventory.TryCraft(recipe));

            foreach (var id in ironIds)
                Assert.IsFalse(_registry.IsHeldBy(id, _holderId), $"Ingredient {id} should have been released");

            Assert.AreEqual(1, _inventory.Items.Count);
            Assert.IsTrue(_registry.IsHeldBy(_inventory.Items[0].InstanceId, _holderId));
        }
    }
}