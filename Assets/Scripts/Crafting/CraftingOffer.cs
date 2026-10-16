using System;

namespace MMO.Crafting
{
    /// <summary>
    /// One recipe as the server offers it to a panel: what would be made, how much of it, and
    /// two readiness hints.
    ///
    /// <para>
    /// This is a wire payload, so it follows the shape every other payload in the project follows
    /// (<c>ItemInstance</c>, the broadcast structs): public fields, no setters, no engine or
    /// FishNet types of its own — FishNet's generated serialiser reads public fields only. It
    /// carries names and counts, never <see cref="RecipeData"/> or <see cref="Inventory.ItemDefinition"/>
    /// references, because a <c>ScriptableObject</c> cannot cross the wire and each end resolves
    /// what it needs locally.
    /// </para>
    ///
    /// <para>
    /// <see cref="IngredientsMet"/> and <see cref="LevelMet"/> are a display hint the panel paints
    /// with, never an authorization: the client could lie about either and
    /// <c>PlayerCrafting.TryCraft</c> re-runs <see cref="CraftingValidator"/> on the server before
    /// anything is consumed or produced.
    /// </para>
    /// </summary>
    [Serializable]
    public struct RecipeOffer
    {
        /// <summary><see cref="RecipeData.RecipeName"/> of the offered recipe; the key a craft request repeats back.</summary>
        public string RecipeName;

        /// <summary>How many of <see cref="RecipeData.Output"/> one craft produces.</summary>
        public int OutputCount;

        /// <summary>True when the player's bag held every ingredient when the payload was built.</summary>
        public bool IngredientsMet;

        /// <summary>
        /// True when the player met <see cref="RecipeData.RequiredCraftingLevel"/> when the payload
        /// was built. Always true in what the server sends — an under-level recipe is filtered out
        /// — and kept so the panel can sort or group without re-deriving the rule.
        /// </summary>
        public bool LevelMet;
    }
}
