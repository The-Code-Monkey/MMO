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
            if (loaded == null)
                loaded = Resources.Load<RecipeData>($"{RecipesFolder}/{recipeName}");
            if (loaded != null)
                Registered[recipeName] = loaded;

            return loaded;
        }

        public static void Clear() => Registered.Clear();

        /// <summary>
        /// A point-in-time copy of every registered recipe, for callers that need to walk the
        /// whole set — the server building a station's offer payload, most notably. It is a
        /// snapshot, so a recipe registered or destroyed while the caller walks it is the next
        /// snapshot's business, never a collection-modified surprise mid-loop.
        /// </summary>
        public static IReadOnlyList<RecipeData> All
        {
            get
            {
                var snapshot = new List<RecipeData>(Registered.Count);
                foreach (KeyValuePair<string, RecipeData> entry in Registered)
                {
                    if (entry.Value != null)
                        snapshot.Add(entry.Value);
                }

                return snapshot;
            }
        }

        /// <summary>
        /// The <c>Resources</c> subfolder the shipped recipe assets live under — the recipe
        /// above is <c>Resources/Recipes/Iron Ingot.asset</c>. Both discovery
        /// (<see cref="LoadAllFromResources"/>) and the <see cref="Resolve"/> fallback read from
        /// here, so a shipped recipe's asset path always starts with it.
        /// </summary>
        public const string RecipesFolder = "Recipes";

        /// <summary>
        /// Loads every <see cref="RecipeData"/> asset under <c>Resources/Recipes</c>, registering
        /// the whole set at once.
        ///
        /// <para>
        /// Each asset already registers itself on load, so this only forces the load to happen:
        /// nothing else in the project walks <c>Resources</c> for recipes, and a recipe nobody
        /// loads never reaches <see cref="All"/> — which is what the server builds its offer
        /// payload from, and what <c>PlayerCrafting.TryCraft</c> resolves a craft request against.
        /// Safe to call repeatedly, since re-registering the same instance just overwrites the
        /// dictionary entry with itself.
        /// </para>
        /// </summary>
        public static void LoadAllFromResources()
        {
            foreach (RecipeData recipe in Resources.LoadAll<RecipeData>(RecipesFolder))
                Register(recipe);
        }
    }
}
