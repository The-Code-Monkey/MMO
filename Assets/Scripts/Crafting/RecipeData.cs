using System;
using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Crafting
{
    [Serializable]
    public struct RecipeIngredient
    {
        public ItemDefinition Item;
        [Min(1)] public int Count;
    }

    [CreateAssetMenu(fileName = "New Recipe Data", menuName = "MMO/Recipe Data")]
    public sealed class RecipeData : ScriptableObject
    {
        [SerializeField] private string _recipeName = string.Empty;
        [SerializeField] private RecipeIngredient[] _ingredients = Array.Empty<RecipeIngredient>();
        [SerializeField] private ItemDefinition _output;
        [SerializeField, Min(1)] private int _outputCount = 1;
        [SerializeField] private CraftingStationType _stationType = CraftingStationType.None;
        [SerializeField, Min(0)] private int _requiredCraftingLevel;
        [SerializeField, Min(0)] private int _craftingXp;

        public string RecipeName => _recipeName;
        public IReadOnlyList<RecipeIngredient> Ingredients => _ingredients ?? Array.Empty<RecipeIngredient>();
        public ItemDefinition Output => _output;
        public int OutputCount => _outputCount;
        public CraftingStationType StationType => _stationType;
        public int RequiredCraftingLevel => _requiredCraftingLevel;
        public int CraftingXp => _craftingXp;

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_recipeName))
                RecipeRegistry.Register(this);
        }

        private void OnDisable() => RecipeRegistry.Unregister(this);
    }
}
