using System.Collections.Generic;
using MMO.Crafting;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The queued craft's two inventory halves: taking the escrow out without releasing it, and
    /// putting the exact same instances back — including the all-or-nothing rule when the bag
    /// cannot take the whole refund.
    /// </summary>
    public sealed class PlayerInventoryEscrowTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void TryTakeIngredients_RemovesExactlyTheRecipeSetAndKeepsTheInstances()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            for (int i = 0; i < 4; i++)
                inventory.TryAddItem(ore);
            inventory.TryAddItem(ingot);
            inventory.TryAddItem(ingot);

            RecipeData recipe = CreateRecipe("Iron Sword", sword, Ingredient(ore, 3), Ingredient(ingot, 1));

            var taken = new List<ItemInstance>();
            Assert.IsTrue(inventory.TryTakeIngredients(recipe, taken));

            Assert.AreEqual(2, inventory.Items.Count, "one ore and the ingot are left");
            Assert.AreEqual(4, taken.Count);
            Assert.AreEqual(4, CountName(taken, "Iron Ore") + CountName(taken, "Iron Ingot"));
            Assert.AreEqual(3, CountName(taken, "Iron Ore"));
            Assert.AreEqual(1, CountName(taken, "Iron Ingot"));
            Assert.IsFalse(string.IsNullOrEmpty(taken[0].InstanceId), "escrow keeps the instance id");

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryTakeIngredients_MissingIngredient_LeavesBagAndOutputEmpty()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Sword", sword, Ingredient(ore, 3));

            var taken = new List<ItemInstance>();
            Assert.IsFalse(inventory.TryTakeIngredients(recipe, taken));

            Assert.AreEqual(1, inventory.Items.Count);
            Assert.IsEmpty(taken, "a failed take must not hand back a partial set");

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGrantEscrow_ReturnsTheSameInstanceIdsAndDurability()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore", maxDurability: 40);
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            for (int i = 0; i < 3; i++)
                inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Sword", sword, Ingredient(ore, 3));
            var taken = new List<ItemInstance>();
            Assert.IsTrue(inventory.TryTakeIngredients(recipe, taken));
            Assert.IsEmpty(inventory.Items);

            var escrow = new List<CraftEscrowItem>();
            for (int i = 0; i < taken.Count; i++)
                escrow.Add(CraftEscrowItem.From(taken[i]));

            Assert.IsTrue(inventory.TryGrantEscrow(escrow));
            Assert.AreEqual(3, inventory.Items.Count);

            for (int i = 0; i < taken.Count; i++)
            {
                Assert.AreEqual(taken[i].InstanceId, inventory.Items[i].InstanceId);
                Assert.AreEqual(40, inventory.Items[i].Durability);
            }

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGrantEscrow_WhenTheWholeSetCannotFit_LeavesTheBagUntouched()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            for (int i = 0; i < PlayerInventory.BaseSlotCount - 2; i++)
                Assert.IsTrue(inventory.TryAddItem(ore));

            var escrow = new List<CraftEscrowItem>
            {
                Escrow("Iron Ore"), Escrow("Iron Ore"), Escrow("Iron Ore"),
            };

            Assert.IsFalse(inventory.TryGrantEscrow(escrow));
            Assert.AreEqual(PlayerInventory.BaseSlotCount - 2, inventory.Items.Count,
                "a refund that cannot fit must not consume part of its own space");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryGrantEscrow_EmptyEscrow_IsANoOp()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            Assert.IsTrue(inventory.TryGrantEscrow(new List<CraftEscrowItem>()));
            Assert.IsTrue(inventory.TryGrantEscrow(null));
            Assert.IsEmpty(inventory.Items);

            Object.DestroyImmediate(go);
        }

        private static CraftEscrowItem Escrow(string itemName) => new()
        {
            ItemName = itemName,
            InstanceId = ItemInstance.NewInstanceId(),
            Width = 1,
            Height = 1,
            StackCount = 1,
        };

        private static RecipeIngredient Ingredient(ItemDefinition item, int count)
            => new() { Item = item, Count = count };

        private static int CountName(IReadOnlyList<ItemInstance> items, string itemName)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].ItemName == itemName)
                    total++;
            }

            return total;
        }

        private static RecipeData CreateRecipe(
            string name, ItemDefinition output, params RecipeIngredient[] ingredients)
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = name;
            so.FindProperty("_output").objectReferenceValue = output;
            so.FindProperty("_stationType").intValue = (int)CraftingStationType.Anvil;

            SerializedProperty array = so.FindProperty("_ingredients");
            array.arraySize = ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Item").objectReferenceValue = ingredients[i].Item;
                element.FindPropertyRelative("Count").intValue = ingredients[i].Count;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }
    }
}
