using System.Collections.Generic;
using MMO.Crafting;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class PlayerInventoryCraftTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void TryCraft_MissingOre_ReturnsFalseAndBagUnchanged()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inv = go.AddComponent<PlayerInventory>();

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

            bool result = inv.TryCraft(recipe);
            Assert.IsFalse(result);
            Assert.AreEqual(0, inv.Items.Count);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void TryCraft_ConsumesOreAndAddsSword()
        {
            GameObject go = new GameObject("Player");
            PlayerInventory inv = go.AddComponent<PlayerInventory>();

            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");
            inv.TryAddItem(ore);
            inv.TryAddItem(ore);
            inv.TryAddItem(ore);

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

            bool result = inv.TryCraft(recipe);
            Assert.IsTrue(result);
            Assert.AreEqual(1, inv.Items.Count);
            Assert.AreEqual("Iron Sword", inv.Items[0].ItemName);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(recipe);
        }
    }
}
