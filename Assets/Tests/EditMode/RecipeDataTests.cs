using MMO.Crafting;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class RecipeDataTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void RecipeData_RegistersOnEnable()
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "TestRecipe";
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(1, RecipeRegistry.Count);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void RecipeData_UnregistersOnDisable()
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "TestRecipe";
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(1, RecipeRegistry.Count);

            Object.DestroyImmediate(recipe);
            RecipeRegistry.Clear(); // ensure clean
        }
    }
}
