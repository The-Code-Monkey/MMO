using System.Collections.Generic;
using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The gathering loop's arithmetic as pure functions: reach, tool, tick timing and the
    /// durability/respawn numbers behind <see cref="ResourceNode"/> and
    /// <see cref="PlayerGathering"/>. Nothing here spawns, connects or waits — the whole point
    /// of the rules living outside the networked components is that an EditMode test can prove
    /// the decision surface without a server, a scene or a frame.
    /// </summary>
    public sealed class GatheringRulesTests
    {
        #region Reach.
        [Test]
        public void IsWithinReach_InsideTheReach_IsInReach()
        {
            Vector3 gatherer = Vector3.zero;
            Vector3 node = new Vector3(0f, 0f, 2f);

            Assert.IsTrue(GatheringRules.IsWithinReach(gatherer, node, 3f));
        }

        [Test]
        public void IsWithinReach_OutsideTheReach_IsNotInReach()
        {
            Vector3 gatherer = Vector3.zero;
            Vector3 node = new Vector3(0f, 0f, 10f);

            Assert.IsFalse(GatheringRules.IsWithinReach(gatherer, node, 3f));
        }

        [Test]
        public void IsWithinReach_ExactlyOnTheBoundary_Misses()
        {
            Vector3 gatherer = Vector3.zero;
            Vector3 node = new Vector3(0f, 0f, 3f);

            // "Strictly closer than the reach", the same boundary the attack loop draws, so a
            // floating-point rounding cannot tip either interaction in or out inconsistently.
            Assert.IsFalse(GatheringRules.IsWithinReach(gatherer, node, 3f));
        }

        [Test]
        public void IsWithinReach_AReachOfZero_NeverReaches()
        {
            Assert.IsFalse(GatheringRules.IsWithinReach(Vector3.zero, Vector3.zero, 0f));
        }

        [Test]
        public void IsWithinReach_ANegativeReach_NeverReaches()
        {
            Assert.IsFalse(GatheringRules.IsWithinReach(Vector3.zero, Vector3.zero, -1f),
                "Reach is a distance, never a signed quantity.");
        }

        [Test]
        public void IsWithinReach_MeasuresTrueDistanceNotAxisAlignedDistance()
        {
            Vector3 gatherer = Vector3.zero;
            Vector3 node = new Vector3(3f, 0f, 4f);

            Assert.IsTrue(GatheringRules.IsWithinReach(gatherer, node, 6f),
                "3-4-5 triangle: the straight-line distance is 5, under the 6 metre reach.");
            Assert.IsFalse(GatheringRules.IsWithinReach(gatherer, node, 4f));
        }
        #endregion

        #region Tool requirements.
        [Test]
        public void HasRequiredTool_NoRequirement_IsAlwaysSatisfied()
        {
            Assert.IsTrue(GatheringRules.HasRequiredTool(null, null, string.Empty));
            Assert.IsTrue(GatheringRules.HasRequiredTool(null, null, null),
                "An absent requirement and an empty one mean the same thing: bare hands are fine.");
        }

        [Test]
        public void HasRequiredTool_TheToolIsInTheBag_IsSatisfied()
        {
            List<ItemInstance> bag = new() { Item("Iron Ore"), Item("Pickaxe") };

            Assert.IsTrue(GatheringRules.HasRequiredTool(bag, null, "Pickaxe"));
        }

        [Test]
        public void HasRequiredTool_TheToolIsEquipped_IsSatisfied()
        {
            List<ItemInstance> equipment = new() { Item("Pickaxe") };

            Assert.IsTrue(GatheringRules.HasRequiredTool(null, equipment, "Pickaxe"),
                "A wielded pickaxe counts as much as a carried one.");
        }

        [Test]
        public void HasRequiredTool_TheToolIsNowhere_IsRefused()
        {
            List<ItemInstance> bag = new() { Item("Iron Ore"), Item("Bread") };
            List<ItemInstance> equipment = new() { Item("Iron Helm") };

            Assert.IsFalse(GatheringRules.HasRequiredTool(bag, equipment, "Pickaxe"));
        }

        [Test]
        public void HasRequiredTool_AnEmptyBagAndEmptyEquipment_AreRefused()
        {
            Assert.IsFalse(GatheringRules.HasRequiredTool(
                new List<ItemInstance>(), new List<ItemInstance>(), "Pickaxe"));
        }

        [Test]
        public void HasRequiredTool_NullContainers_AreRefusedNotThrown()
        {
            // The server validates against live SyncList views; one of them may not exist yet
            // on a player that has not finished spawning. A null must read as "not held".
            Assert.IsFalse(GatheringRules.HasRequiredTool(null, null, "Pickaxe"));
        }

        [Test]
        public void HasRequiredTool_MatchesTheKeyExactly()
        {
            // Item keys are wire identities: two keys that differ only by case or by a plural
            // are two different items, and "pickaxe" must not satisfy "Pickaxe".
            List<ItemInstance> bag = new() { Item("pickaxe"), Item("Iron Ore") };

            Assert.IsFalse(GatheringRules.HasRequiredTool(bag, null, "Pickaxe"));
            Assert.IsTrue(GatheringRules.HasRequiredTool(bag, null, "pickaxe"));
        }

        [Test]
        public void HasRequiredTool_IgnoresEmptySlots()
        {
            List<ItemInstance> bag = new() { ItemInstance.Empty, Item("Pickaxe") };

            Assert.IsTrue(GatheringRules.HasRequiredTool(bag, null, "Pickaxe"));
        }
        #endregion

        #region The tick timer.
        [Test]
        public void IsTickDue_BeforeTheTickIsNotDue()
        {
            Assert.IsFalse(GatheringRules.IsTickDue(nextTickAt: 101.2d, now: 100d));
        }

        [Test]
        public void IsTickDue_TheMomentTheTickLands_IsDue()
        {
            Assert.IsTrue(GatheringRules.IsTickDue(nextTickAt: 100d, now: 100d),
                "The boundary belongs to the tick: a due time reached is a due time met.");
        }

        [Test]
        public void IsTickDue_AfterTheTickIsDue()
        {
            Assert.IsTrue(GatheringRules.IsTickDue(nextTickAt: 100d, now: 100.5d));
        }

        [Test]
        public void ComputeNextTick_AddsTheInterval()
        {
            Assert.AreEqual(101.2d, GatheringRules.ComputeNextTick(100d, 1.2f), 0.000001d);
        }

        [Test]
        public void ComputeNextTick_ZeroInterval_LandsOnNow()
        {
            Assert.AreEqual(100d, GatheringRules.ComputeNextTick(100d, 0f), 0.000001d,
                "A zero-interval node harvests as fast as the server can tick it.");
        }

        [Test]
        public void ComputeNextTick_ANegativeInterval_NeverBooksAPastTick()
        {
            // A misauthored negative interval must not move the next tick behind the clock —
            // that would make the loop look permanently due and fire every frame.
            Assert.AreEqual(100d, GatheringRules.ComputeNextTick(100d, -5f), 0.000001d);
        }
        #endregion

        #region Durability and respawn arithmetic.
        [Test]
        public void ComputeRemaining_SpendsOneHarvest()
        {
            Assert.AreEqual(4, GatheringRules.ComputeRemaining(5));
        }

        [Test]
        public void ComputeRemaining_ClampsAtZero()
        {
            Assert.AreEqual(0, GatheringRules.ComputeRemaining(1));
        }

        [Test]
        public void ComputeRemaining_ANegativeInput_ReadsAsEmpty()
        {
            // Durability below zero would be a node the server believes is "about to fill up";
            // it floors at zero so the depleted flag is the only thing that can ever restore it.
            Assert.AreEqual(0, GatheringRules.ComputeRemaining(-3));
        }

        [Test]
        public void ComputeRespawnAt_AddsTheWindow()
        {
            Assert.AreEqual(130d, GatheringRules.ComputeRespawnAt(100d, 30f), 0.000001d);
        }

        [Test]
        public void ComputeRespawnAt_ANegativeWindow_RespawnsImmediately()
        {
            Assert.AreEqual(100d, GatheringRules.ComputeRespawnAt(100d, -10f), 0.000001d);
        }

        [Test]
        public void IsRespawnDue_BeforeTheWindowEnds_IsNotDue()
        {
            Assert.IsFalse(GatheringRules.IsRespawnDue(respawnAt: 130d, now: 100d));
        }

        [Test]
        public void IsRespawnDue_WhenTheWindowEnds_IsDue()
        {
            Assert.IsTrue(GatheringRules.IsRespawnDue(respawnAt: 130d, now: 130d));
        }
        #endregion

        private static ItemInstance Item(string itemName)
        {
            return new ItemInstance { ItemName = itemName };
        }
    }
}
