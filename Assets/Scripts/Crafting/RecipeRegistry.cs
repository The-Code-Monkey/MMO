using System.Collections.Generic;
using UnityEngine;

namespace MMO.Crafting
{
    public static class RecipeRegistry
    {
        private static readonly Dictionary<string, RecipeData> Registered = new();

        public static int Count => Registered.Count;

        public static void Register(RecipeData recipe)
        {
            if (recipe == null)
                return;

            string name = recipe.RecipeName;
            if (string.IsNullOrEmpty(name))
                return;

            if (Registered.TryGetValue(name, out RecipeData existing) && existing != recipe)
            {
                Debug.LogWarning(
                    $"{nameof(RecipeRegistry)}: '{name}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{recipe.name}'.");
            }

            Registered[name] = recipe;
        }

        public static void Unregister(RecipeData recipe)
        {
            if (recipe == null || string.IsNullOrEmpty(recipe.RecipeName))
                return;

            if (Registered.TryGetValue(recipe.RecipeName, out RecipeData existing) && existing == recipe)
                Registered.Remove(recipe.RecipeName);
        }

        public static RecipeData Resolve(string recipeName)
        {
            if (string.IsNullOrEmpty(recipeName))
                return null;

            if (Registered.TryGetValue(recipeName, out RecipeData definition) && definition != null)
                return definition;

            RecipeData loaded = Resources.Load<RecipeData>(recipeName);
            if (loaded != null)
                Registered[recipeName] = loaded;

            return loaded;
        }

        public static void Clear() => Registered.Clear();
    }
}
