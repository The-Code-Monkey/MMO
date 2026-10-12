using MMO.Crafting;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class CraftingValidatorTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void CanCraft_FailsForMissingIngredient()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Sword";
            so.FindProperty("_output").objectReferenceValue = sword;
            so.FindProperty("_stationType").intValue = (int)CraftingStationType.Anvil;
            var ingredients = so.FindProperty("_ingredients");
            ingredients.arraySize = 1;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Item").objectReferenceValue = ore;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bag = new System.Collections.Generic.List<ItemInstance>();
            bool result = CraftingValidator.CanCraft(bag, recipe, CraftingStationType.Anvil, 0);
            Assert.IsFalse(result);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void CanCraft_SucceedsWithIngredients()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Sword";
            so.FindProperty("_output").objectReferenceValue = sword;
            so.FindProperty("_stationType").intValue = (int)CraftingStationType.Anvil;
            var ingredients = so.FindProperty("_ingredients");
            ingredients.arraySize = 1;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Item").objectReferenceValue = ore;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 3;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bag = new System.Collections.Generic.List<ItemInstance>();
            bag.Add(ItemInstance.Create(ore, 0, 0));
            bag.Add(ItemInstance.Create(ore, 1, 0));
            bag.Add(ItemInstance.Create(ore, 2, 0));
            bool result = CraftingValidator.CanCraft(bag, recipe, CraftingStationType.Anvil, 0);
            Assert.IsTrue(result);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void CanCraft_FailsForWrongStation()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Sword";
            so.FindProperty("_output").objectReferenceValue = sword;
            so.FindProperty("_stationType").intValue = (int)CraftingStationType.Anvil;
            var ingredients = so.FindProperty("_ingredients");
            ingredients.arraySize = 1;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Item").objectReferenceValue = ore;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bag = new System.Collections.Generic.List<ItemInstance>();
            bag.Add(ItemInstance.Create(ore, 0, 0));
            bool result = CraftingValidator.CanCraft(bag, recipe, CraftingStationType.Furnace, 0);
            Assert.IsFalse(result);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void CanCraft_FailsForUnderLevel()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Sword";
            so.FindProperty("_output").objectReferenceValue = sword;
            so.FindProperty("_stationType").intValue = (int)CraftingStationType.Anvil;
            so.FindProperty("_requiredCraftingLevel").intValue = 10;
            var ingredients = so.FindProperty("_ingredients");
            ingredients.arraySize = 1;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Item").objectReferenceValue = ore;
            ingredients.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            var bag = new System.Collections.Generic.List<ItemInstance>();
            bag.Add(ItemInstance.Create(ore, 0, 0));
            bool result = CraftingValidator.CanCraft(bag, recipe, CraftingStationType.Anvil, 5);
            Assert.IsFalse(result);
            Object.DestroyImmediate(recipe);
        }
    }
}
