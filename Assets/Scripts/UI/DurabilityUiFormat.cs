using MMO.Inventory;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// The player-facing half of continuum wear: one shared sentence for a worn item's condition,
    /// so the equipment row and any future tooltip cannot drift from the number combat uses.
    ///
    /// <para>
    /// The text is intentionally read-only and derived from the same
    /// <see cref="DegradationRules.Effectiveness"/> curve the server applies. An indestructible
    /// item (no durability ceiling) and an unknown definition both read as "nothing to show":
    /// there is no condition to report, and inventing one would imply wear that never happens.
    /// </para>
    /// </summary>
    public static class DurabilityUiFormat
    {
        /// <summary>
        /// "72% condition (92% effect)", or "Broken" at zero durability. Empty for an empty
        /// instance and for an item that never wears out.
        /// </summary>
        public static string Condition(ItemInstance item)
        {
            if (item.IsEmpty)
                return string.Empty;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(item.ItemName);
            if (definition == null || definition.MaxDurability <= 0)
                return string.Empty;

            if (DegradationRules.IsBroken(item.Durability, definition.MaxDurability))
                return "Broken";

            int condition = Mathf.RoundToInt(
                DegradationRules.DurabilityRatio(item.Durability, definition.MaxDurability) * 100f);
            int effect = Mathf.RoundToInt(
                DegradationRules.Effectiveness(item.Durability, definition.MaxDurability) * 100f);

            return $"{condition}% condition ({effect}% effect)";
        }
    }
}
