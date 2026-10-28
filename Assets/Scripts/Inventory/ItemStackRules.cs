namespace MMO.Inventory
{
    /// <summary>
    /// Pure rules for combining and splitting item stacks. Kept out of
    /// <see cref="InventoryGrid"/> so the grid owns geometry only and stack rules can be tested
    /// without laying out a bag.
    /// </summary>
    public static class ItemStackRules
    {
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

        public static bool CanMergeStacks(ItemInstance existing, ItemInstance incoming, ItemDefinition definition)
        {
            if (existing.IsEmpty || incoming.IsEmpty || definition == null)
                return false;

            if (!CanStack(definition, ItemDefinitionRegistry.Resolve(existing.ItemName)))
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

            if (!CanStack(definition, ItemDefinitionRegistry.Resolve(existing.ItemName)))
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
