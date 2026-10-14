using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Pure threat rules: generation from damage/heal/taunt, and the switching margin.
    /// </summary>
    public sealed class ThreatRulesTests
    {
        [Test]
        public void ThreatFor_Taunt_ReturnsTauntFloor()
        {
            Assert.AreEqual(ThreatRules.TauntFloor, ThreatRules.ThreatFor(AbilityLogic.Taunt, 0));
            Assert.AreEqual(ThreatRules.TauntFloor, ThreatRules.ThreatFor(AbilityLogic.Taunt, 500));
        }

        [Test]
        public void ThreatFor_Damage_ReturnsAmount()
        {
            Assert.AreEqual(100f, ThreatRules.ThreatFor(AbilityLogic.TargetedDamage, 100));
            Assert.AreEqual(100f, ThreatRules.ThreatFor(AbilityLogic.AreaDamage, 100));
        }

        [Test]
        public void ThreatFor_Heal_ReturnsHalfAmount()
        {
            Assert.AreEqual(50f, ThreatRules.ThreatFor(AbilityLogic.Heal, 100));
        }

        [Test]
        public void ThreatFor_SelfHeal_ReturnsZero()
        {
            Assert.AreEqual(0f, ThreatRules.ThreatFor(AbilityLogic.SelfHeal, 100));
        }

        [Test]
        public void ThreatFor_UnknownLogic_ReturnsZero()
        {
            Assert.AreEqual(0f, ThreatRules.ThreatFor((AbilityLogic)99, 100));
        }

        [Test]
        public void ShouldSwitch_IncumbentZero_AllowsSwitch()
        {
            Assert.IsTrue(ThreatRules.ShouldSwitch(0f, 1f));
        }

        [Test]
        public void ShouldSwitch_ChallengerExceedsMargin_AllowsSwitch()
        {
            Assert.IsTrue(ThreatRules.ShouldSwitch(100f, 130f));
        }

        [Test]
        public void ShouldSwitch_ChallengerWithinMargin_DeniesSwitch()
        {
            Assert.IsFalse(ThreatRules.ShouldSwitch(100f, 105f));
        }

        [Test]
        public void ShouldSwitch_ChallengerExactlyAtMargin_DeniesSwitch()
        {
            Assert.IsFalse(ThreatRules.ShouldSwitch(100f, 110f));
        }

        [Test]
        public void ShouldSwitch_ChallengerJustAboveMargin_AllowsSwitch()
        {
            Assert.IsTrue(ThreatRules.ShouldSwitch(100f, 110.01f));
        }
    }
}