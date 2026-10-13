namespace MMO.Inventory
{
    /// <summary>
    /// The rules the server applies before it lets an item be equipped, kept as pure functions
    /// so every rule has an EditMode test with no network in the way — the project's testing
    /// contract asks for exactly that, and the game's own example lives here:
    /// <c>InventoryValidator.CanEquip</c> refuses a level 5 player's level 10 staff.
    ///
    /// Grid space is the other half of the server's verification, but it belongs to
    /// <see cref="InventoryGrid"/>, which owns the geometry; this class owns only the questions
    /// about the item and the player themselves.
    /// </summary>
    public static class InventoryValidator
    {
        /// <summary>
        /// True when a player of <paramref name="playerLevel"/> meets
        /// <paramref name="definition"/>'s <see cref="ItemDefinition.RequiredLevel"/>.
        /// A null definition is never equippable: an item the server cannot resolve is data
        /// the server must refuse, not interpret.
        /// </summary>
        public static bool CanEquip(int playerLevel, ItemDefinition definition)
        {
            if (definition == null)
                return false;

            return playerLevel >= definition.RequiredLevel;
        }

        public static bool CanStack(ItemDefinition source, ItemDefinition target)
        {
            if (source == null || target == null)
                return false;

            return source.IsStackable && target.IsStackable && source.ItemName == target.ItemName;
        }

        public static int MaxStack(ItemDefinition definition)
        {
            if (definition == null)
                return 1;

            return definition.IsStackable ? definition.MaxStackSize : 1;
        }
    }
}
