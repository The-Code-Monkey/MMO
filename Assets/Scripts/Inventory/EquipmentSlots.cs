using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>
    /// The definition list for <see cref="EquipmentSlot"/>: which slots exist, how many there
    /// are, and whether a value is one of them.
    ///
    /// A plain enum happily accepts a cast from any integer, so <c>(EquipmentSlot)99</c> compiles
    /// and would index straight past the end of the equipment list. Every server-side write goes
    /// through <see cref="IsValid"/> first, which turns a malformed value into a refused action
    /// instead of an out-of-range exception on the authoritative machine.
    /// </summary>
    public static class EquipmentSlots
    {
        /// <summary>How many explicit slots a player has: Main Hand, Off Hand, Head, Chest, Feet, Mount.</summary>
        public const int Count = 6;

        private static readonly EquipmentSlot[] AllSlots =
        {
            EquipmentSlot.MainHand,
            EquipmentSlot.OffHand,
            EquipmentSlot.Head,
            EquipmentSlot.Chest,
            EquipmentSlot.Feet,
            EquipmentSlot.Mount,
        };

        /// <summary>
        /// Every slot exactly once, in the order the enum declares them. Exposed read-only so
        /// iterating the slots cannot be used to rewrite what the server thinks a slot is.
        /// </summary>
        public static IReadOnlyList<EquipmentSlot> All => AllSlots;

        /// <summary>
        /// True when <paramref name="slot"/> is one of the five declared slots. Anything a
        /// client sent that fails this is dropped before it reaches an index.
        /// </summary>
        public static bool IsValid(EquipmentSlot slot)
        {
            int value = (int)slot;
            return value >= 0 && value < Count;
        }
    }
}
