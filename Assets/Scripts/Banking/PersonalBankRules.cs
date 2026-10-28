using MMO.Inventory;

namespace MMO.Banking
{
    /// <summary>
    /// The personal bank's grid geometry and the space checks a deposit or withdrawal needs. Pure,
    /// like <see cref="MMO.Social.GuildBankRules"/>, so the placement rules are EditMode-testable
    /// without a server.
    ///
    /// The bank is deliberately the same 8x6 shape as the backpack: the generic bank window paints
    /// both panes with the same <c>InventoryGridView</c>, so an item occupies the same cells in
    /// either place.
    /// </summary>
    public static class PersonalBankRules
    {
        public const int BankWidth = PlayerInventory.GridWidth;
        public const int BankHeight = PlayerInventory.GridHeight;

        private static readonly InventoryGrid BankGrid = new InventoryGrid(BankWidth, BankHeight);
        private static readonly InventoryGrid BagGrid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);

        /// <summary>The item covering a bank cell, or an empty instance when the cell is free.</summary>
        public static ItemInstance ItemAt(System.Collections.Generic.IReadOnlyList<ItemInstance> bank, int gridX, int gridY)
        {
            if (bank == null)
                return ItemInstance.Empty;

            int index = BankGrid.FindAt(bank, gridX, gridY);
            return index >= 0 ? bank[index] : ItemInstance.Empty;
        }

        /// <summary>The bank entry index covering a cell, or -1.</summary>
        public static int IndexOf(System.Collections.Generic.IReadOnlyList<ItemInstance> bank, int gridX, int gridY)
            => bank == null ? -1 : BankGrid.FindAt(bank, gridX, gridY);

        /// <summary>True when the bank can hold the item somewhere.</summary>
        public static bool BankHasRoom(System.Collections.Generic.IReadOnlyList<ItemInstance> bank, ItemInstance item)
            => !item.IsEmpty &&
               item.Width >= 1 && item.Height >= 1 &&
               BankGrid.TryFindFreeSpace(bank, item.Width, item.Height, out _, out _);

        /// <summary>The first free bank cell the item fits in.</summary>
        public static bool TryFindBankCell(
            System.Collections.Generic.IReadOnlyList<ItemInstance> bank, ItemInstance item, out int x, out int y)
        {
            x = 0;
            y = 0;
            return !item.IsEmpty &&
                   item.Width >= 1 && item.Height >= 1 &&
                   BankGrid.TryFindFreeSpace(bank, item.Width, item.Height, out x, out y);
        }

        /// <summary>True when the backpack can hold the item somewhere.</summary>
        public static bool BagHasRoom(System.Collections.Generic.IReadOnlyList<ItemInstance> bag, ItemInstance item)
            => !item.IsEmpty &&
               item.Width >= 1 && item.Height >= 1 &&
               BagGrid.TryFindFreeSpace(bag, item.Width, item.Height, out _, out _);
    }
}
