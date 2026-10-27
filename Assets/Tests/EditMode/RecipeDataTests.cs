using System.Reflection;
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

            // CreateInstance ran OnEnable while the serialized name was still empty. Re-run it now
            // that the name is set, mirroring how Unity loads an asset with its name already there.
            Invoke(recipe, "OnEnable");

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
            Invoke(recipe, "OnEnable");
            Assert.AreEqual(1, RecipeRegistry.Count);

            Invoke(recipe, "OnDisable");
            Assert.AreEqual(0, RecipeRegistry.Count);

            Object.DestroyImmediate(recipe);
            RecipeRegistry.Clear(); // ensure clean
        }

        private static void Invoke(object target, string message)
            => target.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
    }
}
