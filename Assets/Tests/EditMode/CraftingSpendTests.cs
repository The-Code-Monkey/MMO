using MMO.Crafting;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Pins the one ingredient-spend path (<see cref="PlayerInventory.TrySpendIngredients"/>) that
    /// both the instant craft and the craft queue call, and that a recipe declaring no duration
    /// still crafts instantly.
    /// </summary>
    public sealed class CraftingSpendTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void TrySpendIngredients_NullRecipe_ReturnsFalse()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);
            inventory.TryAddItem(TestItemDefinitions.Create("Iron Ore"));

            Assert.IsFalse(inventory.TrySpendIngredients(null));
            Assert.AreEqual(1, inventory.Items.Count);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TrySpendIngredients_MissingIngredient_ReturnsFalseAndBagUnchanged()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            inventory.TryAddItem(ore);
            inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Ingot", ingot, CraftingStationType.Furnace,
                Ingredient(ore, 3));

            Assert.IsFalse(inventory.TrySpendIngredients(recipe));
            Assert.AreEqual(2, inventory.Items.Count);
            Assert.AreEqual("Iron Ore", inventory.Items[0].ItemName);
            Assert.AreEqual("Iron Ore", inventory.Items[1].ItemName);

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TrySpendIngredients_NullIngredientItem_ReturnsFalseAndBagUnchanged()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            inventory.TryAddItem(ore);
            inventory.TryAddItem(ore);
            inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Ingot", ingot, CraftingStationType.Furnace,
                Ingredient(null, 3));

            Assert.IsFalse(inventory.TrySpendIngredients(recipe));
            Assert.AreEqual(3, inventory.Items.Count);

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TrySpendIngredients_RemovesExactlyTheIngredientSet()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            for (int i = 0; i < 4; i++)
                inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Sword", sword, CraftingStationType.Anvil,
                Ingredient(ore, 3), Ingredient(ingot, 1));

            Assert.IsFalse(inventory.TrySpendIngredients(recipe), "the ingot is missing");
            Assert.AreEqual(4, inventory.Items.Count);

            inventory.TryAddItem(ingot);
            inventory.TryAddItem(ingot);

            Assert.IsTrue(inventory.TrySpendIngredients(recipe));
            Assert.AreEqual(2, inventory.Items.Count, "one ore and one ingot are left");
            Assert.AreEqual(1, CountOf(inventory, "Iron Ore"));
            Assert.AreEqual(1, CountOf(inventory, "Iron Ingot"));
            Assert.AreEqual(0, CountOf(inventory, "Iron Sword"), "spending never grants the output");

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void TryCraft_ZeroDurationRecipe_StaysInstant()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            inventory.TryAddItem(ore);
            inventory.TryAddItem(ore);

            RecipeData recipe = CreateRecipe("Iron Ingot", ingot, CraftingStationType.Furnace,
                Ingredient(ore, 2));
            Assert.AreEqual(0f, recipe.CraftDurationSeconds);
            Assert.IsTrue(recipe.IsInstant);

            Assert.IsTrue(inventory.TryCraft(recipe));
            Assert.AreEqual(1, inventory.Items.Count);
            Assert.AreEqual("Iron Ingot", inventory.Items[0].ItemName);

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RecipeData_NegativeSerializedDuration_ClampsToZeroAndStaysInstant()
        {
            ItemDefinition ingot = TestItemDefinitions.Create("Iron Ingot");
            RecipeData recipe = CreateRecipe("Iron Ingot", ingot, CraftingStationType.Furnace);
            var so = new SerializedObject(recipe);
            so.FindProperty("_craftDurationSeconds").floatValue = -5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(0f, recipe.CraftDurationSeconds);
            Assert.IsTrue(recipe.IsInstant);

            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void TryCraft_OutputCannotFit_SpendsNothing()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inventory = NetworkBehaviourTestFactory.Attach<PlayerInventory>(go);

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition plate = TestItemDefinitions.Create("Iron Plate", width: 8, height: 6);
            for (int i = 0; i < PlayerInventory.BaseSlotCount; i++)
                Assert.IsTrue(inventory.TryAddItem(ore), $"ore {i} should fit");

            RecipeData recipe = CreateRecipe("Iron Plate", plate, CraftingStationType.Anvil,
                Ingredient(ore, 3));

            Assert.IsFalse(inventory.TryCraft(recipe));
            Assert.AreEqual(PlayerInventory.BaseSlotCount, inventory.Items.Count);

            Object.DestroyImmediate(recipe);
            Object.DestroyImmediate(go);
        }

        private static int CountOf(PlayerInventory inventory, string itemName)
        {
            int total = 0;
            for (int i = 0; i < inventory.Items.Count; i++)
            {
                if (inventory.Items[i].ItemName == itemName)
                    total++;
            }

            return total;
        }

        private static RecipeIngredient Ingredient(ItemDefinition item, int count)
            => new() { Item = item, Count = count };

        private static RecipeData CreateRecipe(
            string name,
            ItemDefinition output,
            CraftingStationType station,
            params RecipeIngredient[] ingredients)
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = name;
            so.FindProperty("_output").objectReferenceValue = output;
            so.FindProperty("_stationType").intValue = (int)station;

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
