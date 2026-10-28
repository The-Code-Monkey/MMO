using System.Collections.Generic;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The stack rules: whether two item instances may share one stack, how many fit, and how a
    /// stack splits. They live apart from grid geometry so they can be tested without laying out
    /// a bag.
    /// </summary>
    public sealed class InventoryStackTests
    {
        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _created)
            {
                if (definition != null)
                    Object.DestroyImmediate(definition);
            }

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void CanStack_TrueForTheSameStackableItem()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);

            Assert.IsTrue(ItemStackRules.CanStack(ore, ore));
        }

        [Test]
        public void CanStack_FalseForDifferentOrNonStackableItems()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemDefinition other = Stackable("Copper Ore", 5);
            ItemDefinition sword = TestItemDefinitions.Create("Iron Sword");

            Assert.IsFalse(ItemStackRules.CanStack(ore, other));
            Assert.IsFalse(ItemStackRules.CanStack(sword, sword));
            Assert.IsFalse(ItemStackRules.CanStack(null, ore));
        }

        [Test]
        public void MaxStack_IsOneForNonStackableOrMissingDefinitions()
        {
            ItemDefinition sword = Track(TestItemDefinitions.Create("Iron Sword"));
            ItemDefinition ore = Stackable("Iron Ore", 5);

            Assert.AreEqual(1, ItemStackRules.MaxStack(sword));
            Assert.AreEqual(1, ItemStackRules.MaxStack(null));
            Assert.AreEqual(5, ItemStackRules.MaxStack(ore));
        }

        [Test]
        public void CanMergeStacks_FalseForEmptyInputs()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);

            Assert.IsFalse(ItemStackRules.CanMergeStacks(ItemInstance.Empty, Ore(ore, 1), ore));
            Assert.IsFalse(ItemStackRules.CanMergeStacks(Ore(ore, 1), ItemInstance.Empty, ore));
            Assert.IsFalse(ItemStackRules.CanMergeStacks(Ore(ore, 1), Ore(ore, 1), null));
        }

        [Test]
        public void CanMergeStacks_FalseForNonStackableDefinition()
        {
            ItemDefinition sword = Track(TestItemDefinitions.Create("Iron Sword"));

            Assert.IsFalse(ItemStackRules.CanMergeStacks(Ore(sword, 1), Ore(sword, 1), sword));
        }

        [Test]
        public void CanMergeStacks_TrueWithinTheStackLimit()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);

            Assert.IsTrue(ItemStackRules.CanMergeStacks(Ore(ore, 2), Ore(ore, 3), ore));
        }

        [Test]
        public void CanMergeStacks_FalseWhenTheCombinedCountExceedsTheLimit()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);

            Assert.IsFalse(ItemStackRules.CanMergeStacks(Ore(ore, 3), Ore(ore, 3), ore));
        }

        [Test]
        public void MergeStacks_CombinesCountsAndClampsToTheLimit()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);

            ItemInstance merged = ItemStackRules.MergeStacks(Ore(ore, 2), Ore(ore, 9), ore);

            Assert.AreEqual(5, merged.StackCount);
        }

        [Test]
        public void MergeStacks_ReturnsTheExistingStackForAnEmptyIncomingItem()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemInstance existing = Ore(ore, 2);

            ItemInstance merged = ItemStackRules.MergeStacks(existing, ItemInstance.Empty, ore);

            Assert.AreEqual(existing.ItemName, merged.ItemName);
            Assert.AreEqual(2, merged.StackCount);
        }

        [Test]
        public void SplitStack_TakesTheRequestedCountAndLeavesARemainder()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemInstance source = Ore(ore, 5);

            (ItemInstance remainder, ItemInstance taken) = ItemStackRules.SplitStack(source, 2, ore);

            Assert.AreEqual(3, remainder.StackCount);
            Assert.AreEqual(2, taken.StackCount);
            Assert.AreEqual(ore.ItemName, taken.ItemName);
        }

        [Test]
        public void SplitStack_TakingEverythingLeavesNoRemainder()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemInstance source = Ore(ore, 3);

            (ItemInstance remainder, ItemInstance taken) = ItemStackRules.SplitStack(source, 3, ore);

            Assert.IsTrue(remainder.IsEmpty);
            Assert.AreEqual(3, taken.StackCount);
        }

        [Test]
        public void SplitStack_RequestAboveTheSourceCountTakesEverything()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemInstance source = Ore(ore, 2);

            (ItemInstance remainder, ItemInstance taken) = ItemStackRules.SplitStack(source, 99, ore);

            Assert.IsTrue(remainder.IsEmpty);
            Assert.AreEqual(2, taken.StackCount);
        }

        [Test]
        public void SplitStack_NonPositiveTakeReturnsTheSourceUntouched()
        {
            ItemDefinition ore = Stackable("Iron Ore", 5);
            ItemInstance source = Ore(ore, 4);

            (ItemInstance remainder, ItemInstance taken) = ItemStackRules.SplitStack(source, 0, ore);

            Assert.AreEqual(4, remainder.StackCount);
            Assert.IsTrue(taken.IsEmpty);
        }

        private ItemDefinition Stackable(string name, int maxStackSize)
        {
            return Track(TestItemDefinitions.Create(name, isStackable: true, maxStackSize: maxStackSize));
        }

        private ItemDefinition Track(ItemDefinition definition)
        {
            _created.Add(definition);
            return definition;
        }

        private static ItemInstance Ore(ItemDefinition definition, int count)
            => ItemInstance.Create(definition, 0, 0, count);
    }
}
