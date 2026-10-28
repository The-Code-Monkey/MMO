using System;
using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Social
{
    /// <summary>
    /// A read-only grid over one side of a trade offer. Like <see cref="GuildBankTabSource"/> it
    /// owns no rules: the frame paints the items the server replicated, and a right-click becomes a
    /// removal intent the shell turns into a fresh offer.
    /// </summary>
    public sealed class TradeOfferSource : IInventorySource
    {
        private static readonly IReadOnlyList<ItemInstance> Empty = Array.Empty<ItemInstance>();

        private IReadOnlyList<ItemInstance> _offer = Empty;

        public int GridWidth => PlayerInventory.GridWidth;

        public int GridHeight => PlayerInventory.VisibleGridHeight;

        public IReadOnlyList<ItemInstance> Offer => _offer;

        /// <summary>Raised when the bound offer is replaced, so the grid repaints.</summary>
        public event Action ContainersChanged;

        /// <summary>Right-click on one of our offered items: (bagX, bagY) to remove it.</summary>
        public Action<int, int> RemoveRequested;

        /// <summary>Replaces the painted offer; null means empty.</summary>
        public void SetOffer(IReadOnlyList<ItemInstance> offer)
        {
            _offer = offer ?? Empty;
            ContainersChanged?.Invoke();
        }

        public ItemInstance GetItemAt(int gridX, int gridY)
        {
            for (int i = 0; i < _offer.Count; i++)
            {
                ItemInstance item = _offer[i];
                int width = item.Width > 0 ? item.Width : 1;
                int height = item.Height > 0 ? item.Height : 1;
                if (gridX >= item.GridX && gridX < item.GridX + width &&
                    gridY >= item.GridY && gridY < item.GridY + height)
                    return item;
            }

            return ItemInstance.Empty;
        }

        public void RequestEquip(int gridX, int gridY)
            => RemoveRequested?.Invoke(gridX, gridY);
    }
}
