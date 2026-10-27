using System.Collections.Generic;
using MMO.Crafting;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pure rule behind the server's craft payload: which recipes a station of a given type
    /// offers a player at a given crafting level, and — as a display hint only, never as
    /// authorization — whether that player could afford each one right now.
    ///
    /// <para>
    /// Every case here runs against plain lists and in-memory <see cref="RecipeData"/> instances,
    /// so the whole decision the server makes before it answers an open request is testable with
    /// no network and no scene.
    /// </para>
    /// </summary>
    public sealed class CraftingOfferRulesTests
    {
        [TearDown]
        public void TearDown()
        {
            RecipeRegistry.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Available_FiltersByStationType()
        {
            var recipes = new[]
            {
                Recipe("Iron Sword", CraftingStationType.Anvil, 1),
                Recipe("Healing Potion", CraftingStationType.Alchemy, 1),
            };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, EmptyBag());

            Assert.AreEqual(1, offers.Count);
            Assert.AreEqual("Iron Sword", offers[0].RecipeName);
        }

        [Test]
        public void Available_ExcludesRecipesAboveLevel()
        {
            var recipes = new[] { Recipe("Iron Plate", CraftingStationType.Anvil, requiredLevel: 5) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 4, EmptyBag());

            Assert.IsEmpty(offers, "A recipe above the player's level is not offered at all.");
        }

        [Test]
        public void Available_MarksIngredientsNotMet_ButStillListsTheRecipe()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            var recipes = new[] { Recipe("Iron Sword", CraftingStationType.Anvil, 1, Ingredients(ore, 3)) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, EmptyBag());

            Assert.AreEqual(1, offers.Count, "An unaffordable recipe is shown, greyed out, not hidden.");
            Assert.IsFalse(offers[0].IngredientsMet);
        }

        [Test]
        public void Available_MarksIngredientsMet_WhenTheBagCarriesThem()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            var recipes = new[] { Recipe("Iron Sword", CraftingStationType.Anvil, 1, Ingredients(ore, 2)) };
            var bag = new List<ItemInstance> { ItemInstance.Create(ore, 0, 0), ItemInstance.Create(ore, 1, 0) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, bag);

            Assert.AreEqual(1, offers.Count);
            Assert.IsTrue(offers[0].IngredientsMet);
        }

        [Test]
        public void Available_KeepsCountingAfterAPartialStack_WhenTheBagIsShort()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Iron Ore");
            var recipes = new[] { Recipe("Iron Sword", CraftingStationType.Anvil, 1, Ingredients(ore, 3)) };
            var bag = new List<ItemInstance> { ItemInstance.Create(ore, 0, 0) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, bag);

            Assert.AreEqual(1, offers.Count);
            Assert.IsFalse(offers[0].IngredientsMet,
                "One of three ingots is not three ingots; the hint must follow CraftingValidator.");
        }

        [Test]
        public void Available_IgnoresRecipesWithNoStation()
        {
            var recipes = new[] { Recipe("Debug Thing", CraftingStationType.None, 1) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 9, EmptyBag());

            Assert.IsEmpty(offers, "A recipe with no station type is craftable nowhere.");
        }

        [Test]
        public void Available_CarriesTheOutputCountAndAlwaysReportsTheLevelAsMet()
        {
            var recipes = new[] { Recipe("Iron Bars", CraftingStationType.Furnace, 1, outputCount: 3) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Furnace, level: 1, EmptyBag());

            Assert.AreEqual(3, offers[0].OutputCount);
            Assert.IsTrue(offers[0].LevelMet, "Recipes that failed the level check never reach the payload.");
        }

        [Test]
        public void Available_WithNullBag_BehavesLikeAnEmptyBag()
        {
            var recipes = new[]
            {
                Recipe("Iron Sword", CraftingStationType.Anvil, 1,
                    Ingredients(TestItemDefinitions.Create("Iron Ore"), 2)),
            };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, bag: null);

            Assert.AreEqual(1, offers.Count);
            Assert.IsFalse(offers[0].IngredientsMet);
        }

        [Test]
        public void Available_SkipsNullEntriesAndEmptyLists()
        {
            var recipes = new RecipeData[] { null, Recipe("Iron Sword", CraftingStationType.Anvil, 1) };

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                recipes, CraftingStationType.Anvil, level: 1, EmptyBag());

            Assert.AreEqual(1, offers.Count, "A destroyed recipe must not throw or be offered.");
            Assert.IsEmpty(CraftingOfferRules.Available(
                System.Array.Empty<RecipeData>(), CraftingStationType.Anvil, 1, EmptyBag()));
        }

        [Test]
        public void Available_OverTheRegistry_OffersEveryRecipeThatStationSupports()
        {
            RecipeData sword = Recipe("Iron Sword", CraftingStationType.Anvil, 1);
            RecipeData potion = Recipe("Healing Potion", CraftingStationType.Alchemy, 1);
            RecipeData plate = Recipe("Iron Plate", CraftingStationType.Anvil, requiredLevel: 5);
            RecipeRegistry.Register(sword);
            RecipeRegistry.Register(potion);
            RecipeRegistry.Register(plate);

            IReadOnlyList<RecipeOffer> offers = CraftingOfferRules.Available(
                CraftingStationType.Anvil, level: 1, EmptyBag());

            Assert.AreEqual(1, offers.Count, "The registry overload applies the same station and level filters.");
            Assert.AreEqual("Iron Sword", offers[0].RecipeName);
        }

        [Test]
        public void Available_OverAnEmptyRegistry_ReturnsNoOffers()
        {
            Assert.IsEmpty(CraftingOfferRules.Available(CraftingStationType.Anvil, level: 1, EmptyBag()));
        }

        private static IReadOnlyList<ItemInstance> EmptyBag() => new List<ItemInstance>();

        private static RecipeIngredient[] Ingredients(ItemDefinition item, int count)
            => new[] { new RecipeIngredient { Item = item, Count = count } };

        private static RecipeData Recipe(
            string name,
            CraftingStationType stationType,
            int requiredLevel = 0,
            RecipeIngredient[] ingredients = null,
            int outputCount = 1)
        {
            RecipeData recipe = ScriptableObject.CreateInstance<RecipeData>();
            var inspector = new SerializedObject(recipe);
            inspector.FindProperty("_recipeName").stringValue = name;
            inspector.FindProperty("_stationType").intValue = (int)stationType;
            inspector.FindProperty("_requiredCraftingLevel").intValue = requiredLevel;
            inspector.FindProperty("_outputCount").intValue = outputCount;

            var array = inspector.FindProperty("_ingredients");
            RecipeIngredient[] elements = ingredients ?? System.Array.Empty<RecipeIngredient>();
            array.arraySize = elements.Length;
            for (int i = 0; i < elements.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Item").objectReferenceValue = elements[i].Item;
                element.FindPropertyRelative("Count").intValue = elements[i].Count;
            }

            inspector.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }
    }
}
