using System;
using System.Collections.Generic;
using MMO.Inventory;

namespace MMO.Crafting
{
    /// <summary>
    /// One ingredient as it was escrowed out of the bag: everything needed to put the exact same
    /// instance back (name, id, footprint, stack count and durability), including when its
    /// <see cref="ItemDefinition"/> no longer resolves after a content change.
    /// </summary>
    [Serializable]
    public struct CraftEscrowItem
    {
        public string ItemName;
        public string InstanceId;
        public int Width;
        public int Height;
        public int StackCount;
        public int Durability;

        public bool IsEmpty => string.IsNullOrEmpty(ItemName);

        /// <summary>Captures the instance as it left the bag. The grid cell is deliberately not kept.</summary>
        public static CraftEscrowItem From(ItemInstance item) => new CraftEscrowItem
        {
            ItemName = item.ItemName,
            InstanceId = item.InstanceId,
            Width = Math.Max(1, item.Width),
            Height = Math.Max(1, item.Height),
            StackCount = item.StackCount,
            Durability = item.Durability,
        };

        /// <summary>
        /// Rebuilds the instance at the cell the refund found for it. This does not consult the
        /// registry or an <see cref="ItemDefinition"/>, which is what lets a refund survive a
        /// deleted ingredient asset without dropping the player's materials.
        /// </summary>
        public ItemInstance ToInstance(int gridX, int gridY) => new ItemInstance
        {
            ItemName = ItemName,
            InstanceId = InstanceId,
            GridX = gridX,
            GridY = gridY,
            Width = Math.Max(1, Width),
            Height = Math.Max(1, Height),
            StackCount = StackCount,
            Durability = Durability,
        };
    }

    /// <summary>
    /// One timed craft order, stored on the player's save record. Flat and JsonUtility-friendly:
    /// public fields, no engine types, and no reference to a <see cref="RecipeData"/> — only the
    /// recipe name travels, and each end resolves it locally.
    ///
    /// <para>
    /// <see cref="DueUtcTicks"/> is absolute UTC, so a zone transfer, a disconnect and a restart
    /// all read the same deadline. The state is the idempotence marker: a completion records
    /// <see cref="CraftOrderState.Completed"/> durably before granting and
    /// <see cref="CraftOrderState.Claimed"/> after, so a reload that finds Completed finishes an
    /// interrupted grant and a reload that finds Claimed does nothing.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class CraftOrder
    {
        public string OrderId = string.Empty;
        public string RecipeName = string.Empty;
        public long DueUtcTicks;
        public CraftOrderState State;
        public List<CraftEscrowItem> Escrow = new();

        public static CraftOrder Create(
            string recipeName, long dueUtcTicks, IEnumerable<CraftEscrowItem> escrow)
        {
            var order = new CraftOrder
            {
                OrderId = Guid.NewGuid().ToString("N"),
                RecipeName = recipeName ?? string.Empty,
                DueUtcTicks = dueUtcTicks,
            };

            if (escrow != null)
                order.Escrow.AddRange(escrow);

            return order;
        }

        /// <summary>A detached copy for a save snapshot or a load, so neither side aliases the live list.</summary>
        public CraftOrder Clone()
        {
            var copy = new CraftOrder
            {
                OrderId = OrderId,
                RecipeName = RecipeName,
                DueUtcTicks = DueUtcTicks,
                State = State,
            };

            if (Escrow != null)
                copy.Escrow.AddRange(Escrow);

            return copy;
        }
    }
}
