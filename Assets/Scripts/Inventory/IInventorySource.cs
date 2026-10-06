using System;

namespace MMO.Inventory
{
    /// <summary>
    /// The bag a grid view binds to: its geometry, a cell lookup, the owner-side equip request a
    /// right-click sends, and the one change event that repaints the whole grid.
    ///
    /// <para>
    /// <see cref="PlayerInventory"/> is the networked implementation — it already raises
    /// <see cref="ContainersChanged"/> from the <c>SyncList.OnChange</c> callbacks of both
    /// replicated containers, so a bound grid repaints only when the server actually moves
    /// something. The interface exists so a view can be driven from a plain C# fake in a test
    /// without a server, a socket, or a spawned player.
    /// </para>
    /// </summary>
    public interface IInventorySource
    {
        /// <summary>Bag columns.</summary>
        int GridWidth { get; }

        /// <summary>Bag rows.</summary>
        int GridHeight { get; }

        /// <summary>The item covering a bag cell, or an empty instance when the cell is free.</summary>
        ItemInstance GetItemAt(int gridX, int gridY);

        /// <summary>Owner-side intent: ask the server to equip whatever sits at this bag cell.</summary>
        void RequestEquip(int gridX, int gridY);

        /// <summary>Raised whenever either replicated container changes.</summary>
        event Action ContainersChanged;
    }
}
