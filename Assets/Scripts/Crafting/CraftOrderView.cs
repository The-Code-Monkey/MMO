using System;

namespace MMO.Crafting
{
    /// <summary>
    /// One order as the server shows it to the crafting panel. A wire payload, so public fields
    /// only, names and counts instead of assets. <see cref="RemainingSeconds"/> is computed on the
    /// server; the panel shows it as-is and never compares its own clock to a deadline.
    /// </summary>
    [Serializable]
    public struct CraftOrderView
    {
        /// <summary>The order's stable id; what a claim or a cancel repeats back.</summary>
        public string OrderId;

        public string RecipeName;

        public CraftOrderState State;

        /// <summary>Seconds left on the server's clock; zero once the order is due.</summary>
        public float RemainingSeconds;

        public int OutputCount;

        /// <summary>The output's item name, empty when the recipe no longer resolves.</summary>
        public string OutputItemName;
    }
}
