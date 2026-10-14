using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The death and zone-risk table, pinned in EditMode. Every rule is a pure function, so the
    /// whole consequence surface — which tier drops loot, which respawns, how much a repair takes —
    /// is asserted without a server, a scene or a spawned player.
    /// </summary>
    public sealed class RiskRulesTests
    {
        private ItemDefinition _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void ResolveDeath_Safe_IsNotPermanentAndKeepsLoot()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath(ZoneRiskTier.Safe);

            Assert.IsFalse(outcome.IsPermanent);
            Assert.IsFalse(outcome.DropInventory);
            Assert.AreEqual(RiskRules.SafeDurabilityDamage, outcome.DurabilityDamage);
            Assert.AreEqual(RiskRules.SafeRepairCost, outcome.RepairCost);
        }

        [Test]
        public void ResolveDeath_Yellow_IsNotPermanentAndKeepsLoot()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath(ZoneRiskTier.Yellow);

            Assert.IsFalse(outcome.IsPermanent);
            Assert.IsFalse(outcome.DropInventory);
            Assert.AreEqual(RiskRules.YellowDurabilityDamage, outcome.DurabilityDamage);
            Assert.AreEqual(RiskRules.YellowRepairCost, outcome.RepairCost);
        }

        [Test]
        public void ResolveDeath_Red_IsPermanentButKeepsLoot()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath(ZoneRiskTier.Red);

            Assert.IsTrue(outcome.IsPermanent, "A red-zone death costs progress, not gear.");
            Assert.IsFalse(outcome.DropInventory);
            Assert.AreEqual(RiskRules.RedDurabilityDamage, outcome.DurabilityDamage);
            Assert.AreEqual(0, outcome.RepairCost, "A permanent death is the cost; it does not also bill gold.");
        }

        [Test]
        public void ResolveDeath_Black_DropsEverythingAndRespawns()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath(ZoneRiskTier.Black);

            Assert.IsTrue(outcome.IsPermanent);
            Assert.IsTrue(outcome.DropInventory, "The black zone is full loot: bag and worn gear drop.");
            Assert.AreEqual(0, outcome.DurabilityDamage, "Dropped gear is gone; wearing it down would be moot.");
            Assert.AreEqual(0, outcome.RepairCost, "There is nothing left to repair.");
        }

        [Test]
        public void ResolveDeath_UndeclaredTier_FallsBackToSafe()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath((ZoneRiskTier)99);

            Assert.IsFalse(outcome.DropInventory, "A typo must never turn a hub into a full-loot arena.");
            Assert.AreEqual(RiskRules.SafeDurabilityDamage, outcome.DurabilityDamage);
        }

        [Test]
        public void ResolveDeath_OutOfRangeInteger_FallsBackToSafe()
        {
            DeathOutcome outcome = RiskRules.ResolveDeath(-5);

            Assert.IsFalse(outcome.IsPermanent);
            Assert.IsFalse(outcome.DropInventory);
            Assert.AreEqual(RiskRules.SafeRepairCost, outcome.RepairCost);
        }

        [Test]
        public void IsValidTier_AcceptsOnlyDeclaredTiers()
        {
            Assert.IsTrue(RiskRules.IsValidTier(ZoneRiskTier.Safe));
            Assert.IsTrue(RiskRules.IsValidTier(ZoneRiskTier.Black));
            Assert.IsFalse(RiskRules.IsValidTier((ZoneRiskTier)(-1)));
            Assert.IsFalse(RiskRules.IsValidTier((ZoneRiskTier)4));
        }

        [Test]
        public void WearDurability_SubtractsAndNeverGoesBelowZero()
        {
            Assert.AreEqual(15, RiskRules.WearDurability(20, 5));
            Assert.AreEqual(0, RiskRules.WearDurability(3, 10), "Durability floors at zero; it does not wrap.");
        }

        [Test]
        public void WearDurability_NegativeDamageIsANoOp()
        {
            Assert.AreEqual(20, RiskRules.WearDurability(20, -5));
        }

        [Test]
        public void ClampRepair_NeverTakesMoreThanTheWalletHolds()
        {
            Assert.AreEqual(50, RiskRules.ClampRepair(100, 50));
            Assert.AreEqual(30, RiskRules.ClampRepair(30, 50), "A repair can only take what is there.");
            Assert.AreEqual(0, RiskRules.ClampRepair(0, 50));
            Assert.AreEqual(0, RiskRules.ClampRepair(-10, 50));
        }

        [Test]
        public void Wear_IndestructibleItemIsLeftUntouched()
        {
            ItemInstance sword = NewInstance("Risk.Indestructible", maxDurability: 0);

            ItemInstance worn = RiskRules.Wear(sword, 10);

            Assert.AreEqual(0, worn.Durability);
            Assert.AreEqual(sword.ItemName, worn.ItemName);
        }

        [Test]
        public void Wear_DestructibleItemLosesDurability()
        {
            ItemInstance sword = NewInstance("Risk.Sword", maxDurability: 40);

            ItemInstance worn = RiskRules.Wear(sword, 15);

            Assert.AreEqual(25, worn.Durability);
        }

        [Test]
        public void Wear_ItemAtZeroReadsFromItsMaximum()
        {
            ItemInstance sword = NewInstance("Risk.Broken", maxDurability: 40);
            sword.Durability = 0;

            ItemInstance worn = RiskRules.Wear(sword, 10);

            Assert.AreEqual(30, worn.Durability,
                "A non-positive durability reads from the definition's maximum so the first death is not a free pass.");
        }

        private ItemInstance NewInstance(string name, int maxDurability)
        {
            _created = ScriptableObject.CreateInstance<ItemDefinition>();
            TestItemDefinitions.Write(_created, name, maxDurability: maxDurability);
            return ItemInstance.Create(_created, 0, 0);
        }
    }
}
