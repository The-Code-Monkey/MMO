using System;
using System.Collections.Generic;

namespace MMO.Inventory
{
    /// <summary>Pure placement and equip/unequip plans over plain item lists.</summary>
    public static class InventoryOperations
    {
        /// <summary>
        /// Stages the bag without <paramref name="bagIndex"/> and verifies the equipped slot exists
        /// and any displaced item has room. Mutates only <paramref name="scratch"/>.
        /// </summary>
        public static bool TryPlanEquip(
            IReadOnlyList<ItemInstance> items,
            IReadOnlyList<ItemInstance> equipment,
            InventoryGrid grid,
            int cellLimit,
            int bagIndex,
            ItemDefinition definition,
            List<ItemInstance> scratch,
            out EquipmentSlot slot,
            out int displacedX,
            out int displacedY)
        {
            slot = EquipmentSlot.None;
            displacedX = 0;
            displacedY = 0;

            if (items == null || equipment == null || grid == null || definition == null || scratch == null)
                return false;

            slot = definition.EquipSlot;
            if (!EquipmentSlots.IsValid(slot) || (int)slot >= equipment.Count)
                return false;

            scratch.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                if (i != bagIndex)
                    scratch.Add(items[i]);
            }

            ItemInstance displaced = equipment[(int)slot];
            return displaced.IsEmpty ||
                grid.TryFindFreeSpace(
                    scratch,
                    Math.Max(1, displaced.Width),
                    Math.Max(1, displaced.Height),
                    out displacedX,
                    out displacedY,
                    cellLimit);
        }

        public static bool TryPlanUnequip(
            IReadOnlyList<ItemInstance> items,
            InventoryGrid grid,
            int cellLimit,
            ItemInstance worn,
            out int x,
            out int y)
        {
            x = 0;
            y = 0;

            if (items == null || grid == null || worn.IsEmpty)
                return false;

            return grid.TryFindFreeSpace(
                items, Math.Max(1, worn.Width), Math.Max(1, worn.Height), out x, out y, cellLimit);
        }

        public static bool TryPlanFreeSpace(
            IReadOnlyList<ItemInstance> items,
            InventoryGrid grid,
            int cellLimit,
            ItemInstance item,
            out int x,
            out int y)
        {
            x = 0;
            y = 0;

            if (items == null || grid == null || item.IsEmpty)
                return false;

            return grid.TryFindFreeSpace(
                items, Math.Max(1, item.Width), Math.Max(1, item.Height), out x, out y, cellLimit);
        }
    }
}
