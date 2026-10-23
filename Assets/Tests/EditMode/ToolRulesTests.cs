using System.Collections.Generic;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The tool gate: whether the tools a player carries satisfy what a resource asks for.
    ///
    /// <para>
    /// Two conditions, and both have to hold — the right kind of tool, reaching the right tier.
    /// This is the rule that stops a T1 pickaxe from emptying a T2 vein, so it is proved here in
    /// isolation: no server, no scene, no frame, just a bag laid out and a question asked.
    /// </para>
    /// </summary>
    public sealed class ToolRulesTests
    {
        private readonly List<ItemDefinition> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            // The tool fixtures register themselves, and the registry is global: leaving them
            // behind would make every later test see a bag full of pickaxes it never made.
            foreach (ItemDefinition definition in _scratch)
            {
                if (definition != null)
                    Object.DestroyImmediate(definition);
            }

            _scratch.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Satisfies_NoToolAndNoTier_IsSatisfiedByBareHands()
        {
            Assert.IsTrue(ToolRules.Satisfies(null, null, ToolKind.None, 0),
                "A resource that names no tool and asks for no tier is harvestable by hand.");
        }

        [Test]
        public void Satisfies_NamedToolAndNoTier_NeedsThatKind()
        {
            ItemDefinition pickaxe = Tool("GatePickaxe", ToolKind.Pickaxe, 0);
            ItemDefinition axe = Tool("GateAxe", ToolKind.Axe, 0);

            List<ItemInstance> withPickaxe = new() { Item(pickaxe.ItemName) };
            List<ItemInstance> withAxe = new() { Item(axe.ItemName) };

            Assert.IsTrue(ToolRules.Satisfies(withPickaxe, null, ToolKind.Pickaxe, 0));
            Assert.IsFalse(ToolRules.Satisfies(withAxe, null, ToolKind.Pickaxe, 0),
                "An axe is not a pickaxe, however good an axe it is.");
            Assert.IsFalse(ToolRules.Satisfies(null, null, ToolKind.Pickaxe, 0),
                "A node that names a tool cannot be worked by a player holding nothing.");
        }

        [Test]
        public void Satisfies_NamedToolAndTier_RefusesAToolBelowThatTier()
        {
            ItemDefinition tier1 = Tool("TierOnePickaxe", ToolKind.Pickaxe, 1);
            ItemDefinition tier2 = Tool("TierTwoPickaxe", ToolKind.Pickaxe, 2);
            ItemDefinition tier3 = Tool("TierThreePickaxe", ToolKind.Pickaxe, 3);

            Assert.IsFalse(ToolRules.Satisfies(new List<ItemInstance> { Item(tier1.ItemName) }, null,
                    ToolKind.Pickaxe, 2),
                "A T1 pickaxe must never open a T2 vein.");
            Assert.IsTrue(ToolRules.Satisfies(new List<ItemInstance> { Item(tier2.ItemName) }, null,
                ToolKind.Pickaxe, 2));
            Assert.IsTrue(ToolRules.Satisfies(new List<ItemInstance> { Item(tier3.ItemName) }, null,
                ToolKind.Pickaxe, 2),
                "A T3 pickaxe opens a T2 vein — the requirement is a floor, not an exact match.");
        }

        [Test]
        public void Satisfies_NamedToolAndTier_RefusesTheWrongKindAtTheRightTier()
        {
            ItemDefinition axe = Tool("TierTwoAxe", ToolKind.Axe, 2);

            Assert.IsFalse(ToolRules.Satisfies(new List<ItemInstance> { Item(axe.ItemName) }, null,
                    ToolKind.Pickaxe, 2),
                "Tier and kind are two conditions, not one: a T2 axe is still not a pickaxe.");
        }

        [Test]
        public void Satisfies_NoToolButATier_NeedsAToolOfThatTier()
        {
            ItemDefinition tier1 = Tool("AnyKindTierOne", ToolKind.Axe, 1);
            ItemDefinition tier2 = Tool("AnyKindTierTwo", ToolKind.Sickle, 2);

            Assert.IsFalse(ToolRules.Satisfies(null, null, ToolKind.None, 2),
                "Bare hands reach no tier, so a resource that asks for one has to be answered with a tool.");
            Assert.IsFalse(ToolRules.Satisfies(new List<ItemInstance> { Item(tier1.ItemName) }, null,
                ToolKind.None, 2),
                "The tier is the floor, whatever kind the tool happens to be.");
            Assert.IsTrue(ToolRules.Satisfies(new List<ItemInstance> { Item(tier2.ItemName) }, null,
                ToolKind.None, 2),
                "A resource that names no kind accepts any tool of at least the tier asked for.");
        }

        [Test]
        public void Satisfies_ACarriedToolCounts()
        {
            ItemDefinition pickaxe = Tool("CarriedPickaxe", ToolKind.Pickaxe, 1);

            Assert.IsTrue(ToolRules.Satisfies(new List<ItemInstance> { Item(pickaxe.ItemName) }, null,
                ToolKind.Pickaxe, 1));
        }

        [Test]
        public void Satisfies_AWornToolCounts()
        {
            ItemDefinition pickaxe = Tool("WornPickaxe", ToolKind.Pickaxe, 1);

            Assert.IsTrue(ToolRules.Satisfies(null, new List<ItemInstance> { Item(pickaxe.ItemName) },
                    ToolKind.Pickaxe, 1),
                "A wielded pickaxe counts as much as a carried one — the server does not care which.");
        }

        [Test]
        public void Satisfies_NullContainers_AreRefusedNotThrown()
        {
            // The server validates against live SyncList views; one of them may not exist yet on a
            // player that has not finished spawning. A null must read as "not held".
            Assert.IsFalse(ToolRules.Satisfies(null, null, ToolKind.Pickaxe, 1));
        }

        [Test]
        public void Satisfies_IgnoresEmptySlots()
        {
            ItemDefinition pickaxe = Tool("SparsePickaxe", ToolKind.Pickaxe, 1);

            List<ItemInstance> bag = new() { ItemInstance.Empty, Item(pickaxe.ItemName) };

            Assert.IsTrue(ToolRules.Satisfies(bag, null, ToolKind.Pickaxe, 1));
        }

        [Test]
        public void Satisfies_IgnoresItemsThatAreNotTools()
        {
            ItemDefinition ore = NotATool("GateOre");

            Assert.IsFalse(ToolRules.Satisfies(new List<ItemInstance> { Item(ore.ItemName) }, null,
                ToolKind.Pickaxe, 0),
                "An item that is not a tool never satisfies a tool requirement.");
        }

        [Test]
        public void Satisfies_IgnoresAnItemWithNoDefinition()
        {
            List<ItemInstance> bag = new() { Item("No Such Item Exists") };

            Assert.IsFalse(ToolRules.Satisfies(bag, null, ToolKind.Pickaxe, 0),
                "An item nobody can resolve is not a tool; a missing definition is data to refuse, not to throw on.");
        }

        [Test]
        public void BestTier_ReturnsTheHighestToolOfThatKind()
        {
            ItemDefinition tier1 = Tool("BestTierOne", ToolKind.Pickaxe, 1);
            ItemDefinition tier3 = Tool("BestTierThree", ToolKind.Pickaxe, 3);
            ItemDefinition tier4 = Tool("BestTierFourAxe", ToolKind.Axe, 4);

            List<ItemInstance> bag = new() { Item(tier1.ItemName), Item(tier3.ItemName), Item(tier4.ItemName) };

            Assert.AreEqual(3, ToolRules.BestTier(bag, null, ToolKind.Pickaxe),
                "The axe is a better axe, but it is not a pickaxe.");
        }

        [Test]
        public void BestTier_CountsAWornTool()
        {
            ItemDefinition tier1 = Tool("WornTierOne", ToolKind.Pickaxe, 1);
            ItemDefinition tier2 = Tool("WornTierTwo", ToolKind.Pickaxe, 2);

            Assert.AreEqual(2, ToolRules.BestTier(null, new List<ItemInstance> { Item(tier2.ItemName) },
                ToolKind.Pickaxe));
            Assert.AreEqual(2, ToolRules.BestTier(new List<ItemInstance> { Item(tier1.ItemName) },
                new List<ItemInstance> { Item(tier2.ItemName) }, ToolKind.Pickaxe),
                "The best pickaxe counts wherever it is worn or carried.");
        }

        [Test]
        public void BestTier_WithoutAnyTool_IsNoTool()
        {
            Assert.AreEqual(ToolRules.NoTool, ToolRules.BestTier(null, null, ToolKind.Pickaxe));
            Assert.AreEqual(ToolRules.NoTool, ToolRules.BestToolTier(null, null));
        }

        [Test]
        public void BestToolTier_CountsAnyKind()
        {
            ItemDefinition axe = Tool("AnyKindAxe", ToolKind.Axe, 2);

            Assert.AreEqual(2, ToolRules.BestToolTier(new List<ItemInstance> { Item(axe.ItemName) }, null));
        }

        [Test]
        public void KindOfAndTierOf_ReadTheDefinition()
        {
            ItemDefinition pickaxe = Tool("MetadataPickaxe", ToolKind.Pickaxe, 2);

            Assert.AreEqual(ToolKind.Pickaxe, ToolRules.KindOf(pickaxe.ItemName));
            Assert.AreEqual(2, ToolRules.TierOf(pickaxe.ItemName));
        }

        [Test]
        public void KindOfAndTierOf_ForAnUnknownItem_AreNoneAndZero()
        {
            Assert.AreEqual(ToolKind.None, ToolRules.KindOf("No Such Item Exists"));
            Assert.AreEqual(0, ToolRules.TierOf("No Such Item Exists"));
            Assert.AreEqual(ToolKind.None, ToolRules.KindOf(null));
            Assert.AreEqual(0, ToolRules.TierOf(null));
        }

        private ItemDefinition Tool(string name, ToolKind kind, int tier)
        {
            ItemDefinition definition = TestItemDefinitions.Create(name, toolKind: kind, toolTier: tier);
            _scratch.Add(definition);
            return definition;
        }

        private ItemDefinition NotATool(string name)
        {
            ItemDefinition definition = TestItemDefinitions.Create(name);
            _scratch.Add(definition);
            return definition;
        }

        private static ItemInstance Item(string itemName)
        {
            return new ItemInstance { ItemName = itemName };
        }
    }
}
