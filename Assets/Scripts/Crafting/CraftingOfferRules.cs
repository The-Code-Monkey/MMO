using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Crafting
{
    /// <summary>
    /// The pure answer to "what does this station have for this player": which recipes a station
    /// of a given type offers at a given crafting level, and whether each could be afforded right
    /// now.
    ///
    /// <para>
    /// Everything here is a decision the server makes before it answers an open request, kept
    /// free of the network and of Unity objects so it can be tested as a list of inputs. The
    /// filters mirror <see cref="CraftingValidator.CanCraft"/> deliberately: station type matches
    /// (and is not <see cref="CraftingStationType.None"/>), the level is high enough, and then —
    /// unlike a craft check — an unaffordable recipe is still <b>offered</b>, with
    /// <see cref="RecipeOffer.IngredientsMet"/> false so the panel can grey it out instead of
    /// hiding it.
    /// </para>
    /// </summary>
    public static class CraftingOfferRules
    {
        /// <summary>
        /// Offers from <paramref name="recipes"/> that fit <paramref name="stationType"/> and
        /// <paramref name="level"/>. A null entry, a null bag, or an empty list is not an error.
        /// </summary>
        public static IReadOnlyList<RecipeOffer> Available(
            IReadOnlyList<RecipeData> recipes,
            CraftingStationType stationType,
            int level,
            IReadOnlyList<ItemInstance> bag)
        {
            var offers = new List<RecipeOffer>();

            if (recipes == null || stationType == CraftingStationType.None)
                return offers;

            // A player without an inventory yet is simply a player holding nothing.
            IReadOnlyList<ItemInstance> contents = bag ?? System.Array.Empty<ItemInstance>();

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeData recipe = recipes[i];
                if (recipe == null ||
                    recipe.StationType != stationType ||
                    level < recipe.RequiredCraftingLevel)
                    continue;

                offers.Add(new RecipeOffer
                {
                    RecipeName = recipe.RecipeName,
                    OutputCount = recipe.OutputCount,
                    LevelMet = true,
                    IngredientsMet = CraftingValidator.HasIngredients(contents, recipe),
                });
            }

            return offers;
        }

        /// <summary>The same filter applied to everything currently in <see cref="RecipeRegistry"/>.</summary>
        public static IReadOnlyList<RecipeOffer> Available(
            CraftingStationType stationType,
            int level,
            IReadOnlyList<ItemInstance> bag)
            => Available(RecipeRegistry.All, stationType, level, bag);
    }
}
