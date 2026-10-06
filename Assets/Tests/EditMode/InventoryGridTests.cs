using System;
using System.Collections.Generic;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The geometry of the bag: in-bounds, occupied, free. Every rule here is what the server
    /// consults when it "verifies inventory space" before an equip, so the tests deliberately
    /// work against plain <see cref="ItemInstance"/> lists — the same values a
    /// <c>SyncList</c> would hold — with no FishNet object in sight.
    /// </summary>
    public sealed class InventoryGridTests
    {
        [Test]
        public void Grid_RejectsNonPositiveDimensions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(0, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(8, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryGrid(-1, -1));
        }

        [Test]
        public void Grid_ExposesItsDimensionsAndCellCount()
        {
            var grid = new InventoryGrid(8, 6);

            Assert.AreEqual(8, grid.Width);
            Assert.AreEqual(6, grid.Height);
            Assert.AreEqual(48, grid.CellCount, "The issue's grid must account for every cell a bag can hold.");
        }

        [Test]
        public void Contains_RespectsAllFourEdges()
        {
            var grid = new InventoryGrid(4, 3);

            Assert.IsTrue(grid.Contains(0, 0, 1, 1), "Origin is in bounds.");
            Assert.IsTrue(grid.Contains(3, 2, 1, 1), "The last cell is in bounds.");
            Assert.IsTrue(grid.Contains(2, 1, 2, 2), "A rectangle ending exactly on the edge fits.");
            Assert.IsFalse(grid.Contains(-1, 0, 1, 1), "Left of the grid is out.");
            Assert.IsFalse(grid.Contains(0, -1, 1, 1), "Above the grid is out.");
            Assert.IsFalse(grid.Contains(3, 0, 2, 1), "Past the right edge is out.");
            Assert.IsFalse(grid.Contains(0, 2, 1, 2), "Past the bottom edge is out.");
        }

        [Test]
        public void Contains_RejectsDegenerateFootprints()
        {
            var grid = new InventoryGrid(4, 3);

            Assert.IsFalse(grid.Contains(0, 0, 0, 1), "A zero-wide item occupies no cells and would stack invisibly.");
            Assert.IsFalse(grid.Contains(0, 0, 1, 0));
        }

        [Test]
        public void CanPlace_AcceptsAnythingOnAnEmptyGrid()
        {
            var grid = new InventoryGrid(4, 3);

            Assert.IsTrue(grid.CanPlace(null, 0, 0, 4, 3), "Null items means an empty grid.");
            Assert.IsTrue(grid.CanPlace(new List<ItemInstance>(), 2, 1, 2, 2));
        }

        [Test]
        public void CanPlace_RejectsOverlapWithAnOccupant()
        {
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Sword", 1, 1, width: 2, height: 2) };

            Assert.IsFalse(grid.CanPlace(items, 2, 2, 1, 1), "The cell inside the sword's footprint is taken.");
            Assert.IsFalse(grid.CanPlace(items, 0, 1, 2, 2), "Sharing any column of the footprint is still overlap.");
        }

        [Test]
        public void CanPlace_AcceptsFreeCellsBesideAnOccupant()
        {
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Sword", 0, 0, width: 2, height: 2) };

            Assert.IsTrue(grid.CanPlace(items, 2, 0, 1, 1));
            Assert.IsTrue(grid.CanPlace(items, 0, 2, 4, 1));
        }

        [Test]
        public void CanPlace_TreatsADegenerateOccupantAsOneCell()
        {
            // Footprints below one cell can only come from corrupted data; protecting the cell
            // they claim is the safe answer — treating them as free would let items stack.
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Corrupt", 1, 1, width: 0, height: 0) };

            Assert.IsFalse(grid.CanPlace(items, 1, 1, 1, 1));
        }

        [Test]
        public void TryFindFreeSpace_PlacesItemsRowMajorFromTheOrigin()
        {
            var grid = new InventoryGrid(4, 3);
            var items = new List<ItemInstance>();

            Assert.IsTrue(grid.TryFindFreeSpace(items, 1, 1, out int firstX, out int firstY));
            Assert.AreEqual((0, 0), (firstX, firstY), "The first free cell is the origin.");

            items.Add(Item("One", firstX, firstY));
            Assert.IsTrue(grid.TryFindFreeSpace(items, 1, 1, out int secondX, out int secondY));
            Assert.AreEqual((1, 0), (secondX, secondY), "Scan order is left to right along the top row.");
        }

        [Test]
        public void TryFindFreeSpace_LandsNextToAWiderItem()
        {
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Board", 0, 0, width: 2, height: 1) };

            Assert.IsTrue(grid.TryFindFreeSpace(items, 1, 1, out int x, out int y));
            Assert.AreEqual((2, 0), (x, y), "The row scan must skip the occupied cells and continue right.");
        }

        [Test]
        public void TryFindFreeSpace_FailsWhenTheItemIsTooBigForTheGrid()
        {
            var grid = new InventoryGrid(4, 3);

            Assert.IsFalse(grid.TryFindFreeSpace(new List<ItemInstance>(), 5, 1, out _, out _));
            Assert.IsFalse(grid.TryFindFreeSpace(new List<ItemInstance>(), 1, 4, out _, out _));
        }

        [Test]
        public void TryFindFreeSpace_FailsForDegenerateSizes()
        {
            var grid = new InventoryGrid(4, 3);

            Assert.IsFalse(grid.TryFindFreeSpace(new List<ItemInstance>(), 0, 1, out _, out _));
            Assert.IsFalse(grid.TryFindFreeSpace(new List<ItemInstance>(), 1, 0, out _, out _));
        }

        [Test]
        public void TryFindFreeSpace_FailsWhenTheGridIsFull()
        {
            var grid = new InventoryGrid(2, 2);
            List<ItemInstance> items = new()
            {
                Item("A", 0, 0), Item("B", 1, 0), Item("C", 0, 1), Item("D", 1, 1),
            };

            Assert.IsFalse(grid.TryFindFreeSpace(items, 1, 1, out _, out _),
                "A full bag must refuse the item rather than place it out of bounds.");
        }

        [Test]
        public void FindAt_ResolvesAnyCellOfAMultiCellItem()
        {
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Spear", 1, 1, width: 1, height: 2), Item("Coin", 0, 0) };

            Assert.AreEqual(0, grid.FindAt(items, 1, 1), "Top cell of the spear.");
            Assert.AreEqual(0, grid.FindAt(items, 1, 2), "Bottom cell of the same spear.");
            Assert.AreEqual(1, grid.FindAt(items, 0, 0));
        }

        [Test]
        public void FindAt_ReturnsMinusOneForEmptyOrOutOfRangeCells()
        {
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { Item("Coin", 0, 0) };

            Assert.AreEqual(-1, grid.FindAt(items, 1, 1), "An unoccupied cell holds no item.");
            Assert.AreEqual(-1, grid.FindAt(items, -1, 0), "Out of bounds is never a hit.");
            Assert.AreEqual(-1, grid.FindAt(items, 0, 3));
            Assert.AreEqual(-1, grid.FindAt(null, 0, 0));
        }

        [Test]
        public void FindAt_SkipsEmptySlotsPaddedIntoTheList()
        {
            // Equipment lists are seeded with Empty entries; a vacant slot must never resolve
            // to an index the caller would treat as an item.
            var grid = new InventoryGrid(4, 3);
            List<ItemInstance> items = new() { ItemInstance.Empty, Item("Coin", 2, 2) };

            Assert.AreEqual(-1, grid.FindAt(items, 0, 0));
            Assert.AreEqual(1, grid.FindAt(items, 2, 2));
        }

        private static ItemInstance Item(string name, int x, int y, int width = 1, int height = 1)
        {
            return new ItemInstance { ItemName = name, GridX = x, GridY = y, Width = width, Height = height };
        }
    }
}
