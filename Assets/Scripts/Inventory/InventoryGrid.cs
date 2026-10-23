using System;
using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>
    /// The geometry of one inventory grid: which rectangles are in bounds, which are free, and
    /// which item sits under a given cell. Pure C# with no FishNet or <c>MonoBehaviour</c> in
    /// sight, which is exactly what makes it reachable from an EditMode test — a grid only ever
    /// holds plain <see cref="ItemInstance"/> values, so a test can lay out a bag and ask where
    /// a two-cell sword fits without a server, a socket, or a spawned player.
    ///
    /// The grid does not own the items; callers pass their list in on every call. The list a
    /// server passes is its <c>SyncList&lt;ItemInstance&gt;</c>, which implements
    /// <see cref="IReadOnlyList{T}"/>, so validation always runs against the authoritative
    /// contents rather than a copy that could have gone stale.
    /// </summary>
    public sealed class InventoryGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _extraCells;

        /// <summary>Grid columns.</summary>
        public int Width => _width;

        /// <summary>
        /// Visible rows: the rectangular rows plus one more when the grid ends in a partial row
        /// of extra cells (a mount's 5 carrying slots).
        /// </summary>
        public int Height => _height + (_extraCells > 0 ? 1 : 0);

        /// <summary>Total cells a player can fill.</summary>
        public int CellCount => _width * _height + _extraCells;

        /// <param name="width">Grid columns. Must be at least one.</param>
        /// <param name="height">Grid rows. Must be at least one.</param>
        /// <param name="extraCells">
        /// Cells appended after the last full row, left to right. Zero keeps the grid rectangular.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">A dimension is less than one.</exception>
        public InventoryGrid(int width, int height, int extraCells = 0)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width), width, "A grid needs at least one column.");
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height), height, "A grid needs at least one row.");
            if (extraCells < 0 || extraCells > width)
                throw new ArgumentOutOfRangeException(nameof(extraCells), extraCells,
                    "A partial row fits between zero and one row of cells.");

            _width = width;
            _height = height;
            _extraCells = extraCells;
        }

        /// <summary>
        /// True when a <paramref name="width"/> x <paramref name="height"/> rectangle anchored at
        /// (<paramref name="x"/>, <paramref name="y"/>) fits inside the grid at all. In-bounds is
        /// not the same as free — see <see cref="CanPlace"/> — but out of bounds is never free.
        /// </summary>
        public bool Contains(int x, int y, int width, int height, int cellLimit = int.MaxValue)
        {
            if (width < 1 || height < 1)
                return false;

            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (!IsCellAvailable(x + dx, y + dy, cellLimit))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when a single cell exists: any cell of the rectangular rows, or one of the
        /// trailing partial-row cells. The three corners left over by a 5-cell row under an
        /// 8-wide bag are not cells at all, so nothing can be placed in them.
        /// </summary>
        private bool IsCellInside(int x, int y)
        {
            if (x < 0 || y < 0)
                return false;

            if (y < _height)
                return x < _width;

            return y == _height && x < _extraCells;
        }

        /// <summary>
        /// A cell that exists and falls inside the caller's capacity limit. Cells are ordered
        /// row-major with the partial row last, so a limit is exactly "the first N cells" — which
        /// is how a dismounted player is refused the mount's trailing slots while still able to
        /// read items already sitting in them.
        /// </summary>
        private bool IsCellAvailable(int x, int y, int cellLimit)
        {
            return IsCellInside(x, y) && CellIndex(x, y) < cellLimit;
        }

        private int CellIndex(int x, int y)
        {
            return y * _width + x;
        }

        /// <summary>
        /// True when a rectangle at (<paramref name="x"/>, <paramref name="y"/>) is both in
        /// bounds and clear of every non-empty item in <paramref name="items"/>. This is the
        /// "verifies inventory space" step the server runs before moving anything.
        /// </summary>
        /// <param name="items">Current occupants. Null is treated as an empty grid.</param>
        /// <param name="cellLimit">Cells 0..limit-1 are placeable; the rest exist but are closed.</param>
        public bool CanPlace(
            IReadOnlyList<ItemInstance> items, int x, int y, int width, int height,
            int cellLimit = int.MaxValue)
        {
            if (!Contains(x, y, width, height, cellLimit))
                return false;

            if (items == null)
                return true;

            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance occupant = items[i];
                if (occupant.IsEmpty)
                    continue;

                // A degenerate footprint (width or height below one, which only hand-edited or
                // corrupt data could produce) is treated as one cell: too small to trust, big
                // enough to protect the cell it claims.
                if (Overlaps(x, y, width, height,
                        occupant.GridX, occupant.GridY,
                        Math.Max(1, occupant.Width), Math.Max(1, occupant.Height)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Scans row by row, left to right, for the first rectangle of the requested size that
        /// <see cref="CanPlace"/> accepts, which gives every caller the same deterministic
        /// packing — the server and a test looking at the same bag always agree on where a
        /// dropped item should land. Returns false, with the out parameters at the origin, when
        /// the grid cannot hold the item at all.
        /// </summary>
        /// <param name="items">Current occupants. Null is treated as an empty grid.</param>
        /// <param name="cellLimit">Cells 0..limit-1 are placeable; the rest exist but are closed.</param>
        public bool TryFindFreeSpace(
            IReadOnlyList<ItemInstance> items, int width, int height, out int gridX, out int gridY,
            int cellLimit = int.MaxValue)
        {
            gridX = 0;
            gridY = 0;

            if (width < 1 || height < 1)
                return false;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (CanPlace(items, x, y, width, height, cellLimit))
                    {
                        gridX = x;
                        gridY = y;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The index in <paramref name="items"/> of the item covering the given cell, or -1 when
        /// the cell is empty or outside the grid. The server uses this to turn the cell a client
        /// right-clicked into the item that currently occupies it, so a stale click can never
        /// reach past the list the server is looking at.
        /// </summary>
        public int FindAt(IReadOnlyList<ItemInstance> items, int x, int y)
        {
            if (items == null || !Contains(x, y, 1, 1))
                return -1;

            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance item = items[i];
                if (item.IsEmpty)
                    continue;

                int width = Math.Max(1, item.Width);
                int height = Math.Max(1, item.Height);
                if (x >= item.GridX && x < item.GridX + width &&
                    y >= item.GridY && y < item.GridY + height)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Overlaps(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            return ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;
        }

        public static bool CanMergeStacks(ItemInstance existing, ItemInstance incoming, ItemDefinition definition)
        {
            if (existing.IsEmpty || incoming.IsEmpty || definition == null)
                return false;

            if (!InventoryValidator.CanStack(definition, ItemDefinitionRegistry.Resolve(existing.ItemName)))
                return false;

            int existingCount = existing.StackCount < 1 ? 1 : existing.StackCount;
            int incomingCount = incoming.StackCount < 1 ? 1 : incoming.StackCount;
            return existingCount + incomingCount <= definition.MaxStackSize;
        }

        public static ItemInstance MergeStacks(ItemInstance existing, ItemInstance incoming, ItemDefinition definition)
        {
            if (existing.IsEmpty)
                return incoming;

            if (incoming.IsEmpty || definition == null)
                return existing;

            if (!InventoryValidator.CanStack(definition, ItemDefinitionRegistry.Resolve(existing.ItemName)))
                return existing;

            int existingCount = existing.StackCount < 1 ? 1 : existing.StackCount;
            int incomingCount = incoming.StackCount < 1 ? 1 : incoming.StackCount;
            int total = existingCount + incomingCount;
            if (total > definition.MaxStackSize)
                total = definition.MaxStackSize;

            existing.StackCount = total;
            return existing;
        }

        public static (ItemInstance remainder, ItemInstance taken) SplitStack(ItemInstance source, int takeCount, ItemDefinition definition)
        {
            if (source.IsEmpty || definition == null || takeCount <= 0)
                return (source, ItemInstance.Empty);

            int sourceCount = source.StackCount < 1 ? 1 : source.StackCount;
            int toTake = takeCount > sourceCount ? sourceCount : takeCount;

            ItemInstance taken = source;
            taken.StackCount = toTake;

            ItemInstance remainder = ItemInstance.Empty;
            if (sourceCount > toTake)
            {
                remainder = source;
                remainder.StackCount = sourceCount - toTake;
            }

            return (remainder, taken);
        }
    }
}
