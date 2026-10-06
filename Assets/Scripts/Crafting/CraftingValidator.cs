using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Crafting
{
    public static class CraftingValidator
    {
        public static bool CanCraft(
            IReadOnlyList<ItemInstance> bag,
            RecipeData recipe,
            CraftingStationType stationType,
            int craftingLevel)
        {
            if (recipe == null || recipe.Output == null)
                return false;

            if (recipe.StationType == CraftingStationType.None ||
                recipe.StationType != stationType)
                return false;

            if (craftingLevel < recipe.RequiredCraftingLevel)
                return false;

            return HasIngredients(bag, recipe);
        }

        public static bool HasIngredients(IReadOnlyList<ItemInstance> bag, RecipeData recipe)
        {
            IReadOnlyList<RecipeIngredient> ingredients = recipe.Ingredients;
            for (int i = 0; i < ingredients.Count; i++)
            {
                RecipeIngredient ingredient = ingredients[i];
                if (ingredient.Item == null || Count(bag, ingredient.Item.ItemName) < ingredient.Count)
                    return false;
            }

            return true;
        }

        public static int Count(IReadOnlyList<ItemInstance> bag, string itemName)
        {
            int total = 0;
            for (int i = 0; i < bag.Count; i++)
            {
                if (!bag[i].IsEmpty && bag[i].ItemName == itemName)
                    total++;
            }

            return total;
        }
    }
}
