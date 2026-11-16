using UnityEngine;

namespace MMO.Inventory
{
    /// <summary>
    /// Continuum wear: how much of an item's stat contribution survives as its durability falls
    /// between full and zero. This is deliberately separate from the binary usable/broken gate —
    /// a sword at 1% durability still swings, it just hits softer; a broken sword contributes
    /// nothing at all.
    ///
    /// <para>
    /// The curve is one pure function so every reader agrees: combat multiplies its weapon damage
    /// by it, the equipment-bonus sum multiplies health/mana bonuses by it, and the UI prints the
    /// same number without a second formula. Durability is an integer and a definition may be
    /// indestructible (<see cref="ItemDefinition.MaxDurability"/> 0), so both of those cases are
    /// pinned here rather than re-derived at each call site.
    /// </para>
    ///
    /// <para>
    /// The floor exists so wear can never make a worn weapon useless while it is still usable —
    /// that is the break rule's job, not the curve's. A zero-durability item is the one exception:
    /// it contributes nothing, because the break rule refuses it everywhere and a hidden floor
    /// would let a "broken" item keep hitting.
    /// </para>
    /// </summary>
    public static class DegradationRules
    {
        /// <summary>
        /// Effectiveness of a worn item at one point of durability: 70% of its authored stats.
        /// The single tuning constant for the continuum; everything else is derived.
        /// </summary>
        public const float MinEffectiveness = 0.70f;

        /// <summary>True when the item has no durability ceiling and never wears out.</summary>
        public static bool IsIndestructible(int maxDurability) => maxDurability <= 0;

        /// <summary>
        /// True when a destructible item has reached zero durability and must be refused by
        /// consumers. An indestructible item (max 0) is never broken, whatever its stored value.
        /// </summary>
        public static bool IsBroken(int durability, int maxDurability)
            => maxDurability > 0 && durability <= 0;

        /// <summary>
        /// The fraction of an item's stats that still applies: <c>1</c> at full durability,
        /// <see cref="MinEffectiveness"/> at one point left, and <c>0</c> once broken.
        ///
        /// <para>
        /// The curve is measured across the usable range, not from zero, so the floor lands
        /// exactly on the last point of durability instead of near it. A one-point ceiling is
        /// trivially full — there is no room to degrade. Out-of-range values clamp: durability
        /// above the ceiling reads as full, and an indestructible item always reads as full.
        /// </para>
        /// </summary>
        public static float Effectiveness(int durability, int maxDurability)
        {
            if (maxDurability <= 0)
                return 1f;

            if (durability <= 0)
                return 0f;

            if (maxDurability == 1)
                return 1f;

            float remaining = Mathf.Clamp01((float)(durability - 1) / (maxDurability - 1));
            return Mathf.Lerp(MinEffectiveness, 1f, remaining);
        }

        /// <summary>
        /// A base stat scaled by the item's condition, rounded once so combat, the stat sum and
        /// the tooltip all print the same integer. Non-positive bases are returned untouched:
        /// stats in this project are additive bonuses, and scaling a penalty by condition would
        /// quietly turn a worn-out item into a buff.
        /// </summary>
        public static int Effective(int baseValue, int durability, int maxDurability)
        {
            if (baseValue <= 0)
                return baseValue;

            return Mathf.RoundToInt(baseValue * Effectiveness(durability, maxDurability));
        }

        /// <summary>Remaining durability as a 0..1 fraction, for UI; indestructible items read as 1.</summary>
        public static float DurabilityRatio(int durability, int maxDurability)
        {
            if (maxDurability <= 0)
                return 1f;

            return Mathf.Clamp01((float)durability / maxDurability);
        }

        /// <summary>
        /// Effectiveness of one item instance, resolving its definition locally. An empty
        /// instance contributes nothing (there is nothing worn); an unknown definition is treated
        /// as full effectiveness, because a missing asset must never nerf a player's gear.
        /// </summary>
        public static float ItemEffectiveness(ItemInstance item)
        {
            if (item.IsEmpty)
                return 0f;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(item.ItemName);
            return definition == null
                ? 1f
                : Effectiveness(item.Durability, definition.MaxDurability);
        }

        /// <summary>
        /// An item instance's contribution to a stat, scaled by condition. This is the call a
        /// consumer makes when it has an instance rather than a definition, and the reason a
        /// broken item contributes zero rather than its floor value.
        /// </summary>
        public static int EffectiveItemStat(int baseValue, ItemInstance item)
        {
            if (item.IsEmpty)
                return baseValue;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(item.ItemName);
            return definition == null
                ? baseValue
                : Effective(baseValue, item.Durability, definition.MaxDurability);
        }
    }
}
