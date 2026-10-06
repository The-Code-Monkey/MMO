using MMO.Crafting;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class RecipeRegistryTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void RegisterAndResolve_ByName()
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Bar";
            so.ApplyModifiedPropertiesWithoutUndo();
            RecipeRegistry.Register(recipe);

            Assert.AreEqual(1, RecipeRegistry.Count);
            Assert.AreSame(recipe, RecipeRegistry.Resolve("Iron Bar"));

            RecipeRegistry.Unregister(recipe);
            Object.DestroyImmediate(recipe);
        }

        [Test]
        public void Clear_EmptiesRegistry()
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("_recipeName").stringValue = "Iron Bar";
            so.ApplyModifiedPropertiesWithoutUndo();
            RecipeRegistry.Register(recipe);
            RecipeRegistry.Clear();
            Assert.AreEqual(0, RecipeRegistry.Count);
            Object.DestroyImmediate(recipe);
        }
    }
}
