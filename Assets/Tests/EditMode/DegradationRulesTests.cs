using System.Collections.Generic;
using MMO.Inventory;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Continuum wear: the effectiveness curve itself, the instance helpers that resolve
    /// definitions, the equipment-stat sum that consumes it, and the one UI sentence that has to
    /// agree with the server's arithmetic.
    /// </summary>
    public sealed class DegradationRulesTests
    {
        private readonly List<ItemDefinition> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _created)
            {
                if (definition != null)
                    Object.DestroyImmediate(definition);
            }

            _created.Clear();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Effectiveness_FullDurabilityIsOne()
        {
            Assert.AreEqual(1f, DegradationRules.Effectiveness(10, 10), 0.0001f);
        }

        [Test]
        public void Effectiveness_LastPointIsTheFloor()
        {
            Assert.AreEqual(DegradationRules.MinEffectiveness,
                DegradationRules.Effectiveness(1, 10), 0.0001f,
                "One point left is exactly the configured floor, not a value near it.");
        }

        [Test]
        public void Effectiveness_BrokenContributesNothing()
        {
            Assert.AreEqual(0f, DegradationRules.Effectiveness(0, 10), 0.0001f,
                "A broken item must contribute no stats; the floor is only for usable wear.");
            Assert.IsTrue(DegradationRules.IsBroken(0, 10));
        }

        [Test]
        public void Effectiveness_IndestructibleIsAlwaysOne()
        {
            Assert.AreEqual(1f, DegradationRules.Effectiveness(0, 0), 0.0001f);
            Assert.AreEqual(1f, DegradationRules.Effectiveness(25, 0), 0.0001f);
            Assert.IsFalse(DegradationRules.IsBroken(0, 0),
                "An item with no ceiling can never be broken, whatever it stores.");
        }

        [Test]
        public void Effectiveness_ClampsOverMaxDurability()
        {
            Assert.AreEqual(1f, DegradationRules.Effectiveness(999, 10), 0.0001f);
        }

        [Test]
        public void Effective_RoundsOnce()
        {
            // 6/10 leaves 5/9 of the usable range: 0.7 + 0.3 * 5/9 = 0.8667 -> 17.33 -> 17.
            Assert.AreEqual(17, DegradationRules.Effective(20, 6, 10));
        }

        [Test]
        public void Effective_NonPositiveBaseIsReturnedUntouched()
        {
            Assert.AreEqual(0, DegradationRules.Effective(0, 1, 10));
            Assert.AreEqual(-5, DegradationRules.Effective(-5, 1, 10));
        }

        [Test]
        public void EffectiveItemStat_ScalesARegisteredItem()
        {
            ItemDefinition sword = NewDefinition("Degr.Sword", maxDurability: 10);
            ItemInstance worn = ItemInstance.Create(sword, 0, 0);
            worn.Durability = 1;

            Assert.AreEqual(7, DegradationRules.EffectiveItemStat(10, worn),
                "A nearly-broken weapon keeps the floor of its damage.");
        }

        [Test]
        public void EffectiveItemStat_EmptyInstanceKeepsTheBase()
        {
            Assert.AreEqual(10, DegradationRules.EffectiveItemStat(10, ItemInstance.Empty));
        }

        [Test]
        public void EffectiveItemStat_UnknownDefinitionKeepsTheBase()
        {
            var unknown = new ItemInstance { ItemName = "Degr.Missing", Durability = 0 };
            Assert.AreEqual(10, DegradationRules.EffectiveItemStat(10, unknown),
                "A missing asset must not nerf a player's gear.");
        }

        [Test]
        public void EquipmentBonuses_ScaleWithCondition()
        {
            ItemDefinition plate = NewDefinition("Degr.Plate", maxDurability: 10, maxHealthBonus: 40);
            ItemInstance worn = ItemInstance.Create(plate, 0, 0);
            worn.Durability = 1;

            var equipment = new List<ItemInstance> { worn };

            Assert.AreEqual(28, EquipmentBonusCalculator.MaxHealthBonus(equipment),
                "40 health at the floor is 70%, rounded once.");
        }

        [Test]
        public void EquipmentBonuses_FullAndIndestructibleItemsKeepTheirFullBonus()
        {
            ItemDefinition plate = NewDefinition("Degr.FullPlate", maxDurability: 10, maxHealthBonus: 40);
            ItemDefinition ring = NewDefinition("Degr.Ring", maxDurability: 0, maxManaBonus: 15);

            var equipment = new List<ItemInstance>
            {
                ItemInstance.Create(plate, 0, 0),
                ItemInstance.Create(ring, 0, 0),
                ItemInstance.Empty,
            };

            Assert.AreEqual(40, EquipmentBonusCalculator.MaxHealthBonus(equipment));
            Assert.AreEqual(15, EquipmentBonusCalculator.MaxManaBonus(equipment));
        }

        [Test]
        public void UiCondition_ReportsConditionAndEffect()
        {
            ItemDefinition plate = NewDefinition("Degr.UiPlate", maxDurability: 10);
            ItemInstance worn = ItemInstance.Create(plate, 0, 0);
            worn.Durability = 5;

            Assert.AreEqual("50% condition (83% effect)", DurabilityUiFormat.Condition(worn));
        }

        [Test]
        public void UiCondition_SaysBrokenAndHidesIndestructibleItems()
        {
            ItemDefinition sword = NewDefinition("Degr.UiSword", maxDurability: 10);
            ItemInstance broken = ItemInstance.Create(sword, 0, 0);
            broken.Durability = 0;

            ItemDefinition rock = NewDefinition("Degr.UiRock", maxDurability: 0);

            Assert.AreEqual("Broken", DurabilityUiFormat.Condition(broken));
            Assert.AreEqual(string.Empty, DurabilityUiFormat.Condition(ItemInstance.Create(rock, 0, 0)));
            Assert.AreEqual(string.Empty, DurabilityUiFormat.Condition(ItemInstance.Empty));
        }

        private ItemDefinition NewDefinition(string name, int maxDurability, int maxHealthBonus = 0, int maxManaBonus = 0)
        {
            ItemDefinition definition = TestItemDefinitions.Create(
                name,
                maxHealthBonus: maxHealthBonus,
                maxManaBonus: maxManaBonus,
                maxDurability: maxDurability);
            _created.Add(definition);
            return definition;
        }
    }
}
