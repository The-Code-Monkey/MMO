using System.Collections.Generic;
using System.Linq;
using MMO.Crafting;
using MMO.Editor;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// Guards the shipped tutorial-forge content — the iron ore -> iron ingot recipe, the ingot it
    /// produces, and the furnace marker the player refines at — as data the game actually ships,
    /// not as a fixture a test builds. Every assertion reads the real assets through the
    /// AssetDatabase, so a missing file, a drifted field or a marker the scene lost fails here
    /// rather than at run time.
    /// </summary>
    public sealed class TutorialForgeContentTests
    {
        private const string OreItemPath = "Assets/Resources/Items/Iron Ore.asset";
        private const string IngotItemPath = "Assets/Resources/Items/Iron Ingot.asset";
        private const string IngotRecipePath = "Assets/Resources/Recipes/Iron Ingot.asset";

        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void IronIngotItem_ShipsAsAOneCellRefinedGood()
        {
            ItemDefinition ingot = LoadItem(IngotItemPath);

            Assert.AreEqual("Iron Ingot", ingot.ItemName);
            Assert.AreEqual(1, ingot.GridWidth);
            Assert.AreEqual(1, ingot.GridHeight);
        }

        [Test]
        public void IronIngotRecipe_RefinesTwoOreIntoOneIngotAtAFurnace()
        {
            RecipeData recipe = LoadRecipe(IngotRecipePath);
            ItemDefinition ore = LoadItem(OreItemPath);

            Assert.AreEqual("Iron Ingot", recipe.RecipeName);
            Assert.AreEqual(CraftingStationType.Furnace, recipe.StationType);
            Assert.AreEqual(1, recipe.OutputCount);
            Assert.IsNotNull(recipe.Output, "A recipe with no output can never be crafted.");
            Assert.AreEqual("Iron Ingot", recipe.Output.ItemName);

            IReadOnlyList<RecipeIngredient> ingredients = recipe.Ingredients;
            Assert.AreEqual(1, ingredients.Count, "The placeholder forge recipe refines a single input.");
            Assert.AreSame(ore, ingredients[0].Item, "The recipe must point at the shipped Iron Ore asset.");
            Assert.AreEqual(2, ingredients[0].Count);
        }

        [Test]
        public void ShippedRecipe_IsReachableThroughServerDiscovery()
        {
            RecipeRegistry.LoadAllFromResources();

            RecipeData resolved = RecipeRegistry.Resolve("Iron Ingot");
            Assert.IsNotNull(resolved,
                "Every recipe under Resources/Recipes must register when the server starts, or the "
                + "offer payload and craft validation never see it.");
            Assert.AreEqual(CraftingStationType.Furnace, resolved.StationType);
            CollectionAssert.Contains(
                RecipeRegistry.All.Select(recipe => recipe.RecipeName).ToList(),
                "Iron Ingot");
        }

        [Test]
        public void ATwoOreBag_MeetsTheShippedRecipe()
        {
            RecipeData recipe = LoadRecipe(IngotRecipePath);
            ItemDefinition ore = LoadItem(OreItemPath);

            var bag = new List<ItemInstance>
            {
                ItemInstance.Create(ore, 0, 0),
                ItemInstance.Create(ore, 1, 0),
            };

            Assert.IsTrue(CraftingValidator.CanCraft(bag, recipe, CraftingStationType.Furnace, recipe.RequiredCraftingLevel),
                "Two iron ore at a furnace must be enough to refine one ingot.");
        }

        [Test]
        public void TutorialScene_StandsAFurnaceBySpawn()
        {
            Scene scene = EditorSceneManager.OpenScene(ZoneSceneBaker.TutorialZonePath, OpenSceneMode.Additive);
            try
            {
                CraftingStationMarker[] stations = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CraftingStationMarker>(true))
                    .ToArray();

                Assert.IsTrue(
                    stations.Any(station => station.StationType == CraftingStationType.Furnace),
                    "The tutorial needs a furnace or the iron ore -> iron ingot recipe is unreachable.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ItemDefinition LoadItem(string assetPath)
        {
            ItemDefinition asset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected an ItemDefinition asset at {assetPath}. A null here means the file is missing or its "
                + "m_Script GUID does not match ItemDefinition.cs.meta.");

            return asset;
        }

        private static RecipeData LoadRecipe(string assetPath)
        {
            RecipeData asset = AssetDatabase.LoadAssetAtPath<RecipeData>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected a RecipeData asset at {assetPath}. A null here means the file is missing or its "
                + "m_Script GUID does not match RecipeData.cs.meta.");

            return asset;
        }
    }
}
