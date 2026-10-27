using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Pure boss phase transitions: Melee → Adds at 60%, Adds → Enrage at 25% (with no adds),
    /// Enrage → Defeated at 0%. All pure, no Unity, no clock.
    /// </summary>
    public sealed class BossStateMachineTests
    {
        [Test]
        public void NewMachine_StartsInMelee()
        {
            var boss = new BossStateMachine();

            Assert.AreEqual(BossPhase.Melee, boss.Phase);
        }

        [Test]
        public void Boss_Transitions_Melee_Adds_Enrage_Defeated()
        {
            var boss = new BossStateMachine();

            // At 60% health, transitions to Adds phase
            Assert.AreEqual(BossPhase.Adds, boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 1));

            // At 25% health with no adds, transitions to Enrage
            Assert.AreEqual(BossPhase.Enrage, boss.Evaluate(healthFraction: 0.25f, liveAdds: 0, secondsInPhase: 1));

            // At 0% health, transitions to Defeated
            Assert.AreEqual(BossPhase.Defeated, boss.Evaluate(healthFraction: 0f, liveAdds: 0, secondsInPhase: 1));
        }

        [Test]
        public void AddsPhase_RequiresZeroLiveAddsBeforeEnrage()
        {
            var boss = new BossStateMachine();
            boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 1); // Enter Adds

            // Still have live adds -> stays in Adds
            Assert.AreEqual(BossPhase.Adds, boss.Evaluate(healthFraction: 0.20f, liveAdds: 3, secondsInPhase: 5));

            // Adds dead -> transitions to Enrage
            Assert.AreEqual(BossPhase.Enrage, boss.Evaluate(healthFraction: 0.20f, liveAdds: 0, secondsInPhase: 5));
        }

        [Test]
        public void MeleePhase_StaysUntilHealthThreshold()
        {
            var boss = new BossStateMachine();

            Assert.AreEqual(BossPhase.Melee, boss.Evaluate(healthFraction: 1.0f, liveAdds: 0, secondsInPhase: 1));
            Assert.AreEqual(BossPhase.Melee, boss.Evaluate(healthFraction: 0.61f, liveAdds: 0, secondsInPhase: 10));
            Assert.AreEqual(BossPhase.Adds, boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 15));
        }

        [Test]
        public void EnragePhase_StaysUntilDefeated()
        {
            var boss = new BossStateMachine();
            boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 1); // Adds
            boss.Evaluate(healthFraction: 0.25f, liveAdds: 0, secondsInPhase: 1); // Enrage

            Assert.AreEqual(BossPhase.Enrage, boss.Evaluate(healthFraction: 0.20f, liveAdds: 0, secondsInPhase: 1));
            Assert.AreEqual(BossPhase.Enrage, boss.Evaluate(healthFraction: 0.01f, liveAdds: 0, secondsInPhase: 5));
            Assert.AreEqual(BossPhase.Defeated, boss.Evaluate(healthFraction: 0f, liveAdds: 0, secondsInPhase: 10));
        }

        [Test]
        public void DefeatedPhase_IsTerminal()
        {
            var boss = new BossStateMachine();
            boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 1); // Adds
            boss.Evaluate(healthFraction: 0.25f, liveAdds: 0, secondsInPhase: 1); // Enrage
            boss.Evaluate(healthFraction: 0f, liveAdds: 0, secondsInPhase: 1); // Defeated

            Assert.AreEqual(BossPhase.Defeated, boss.Evaluate(healthFraction: -1f, liveAdds: 0, secondsInPhase: 1));
        }

        [Test]
        public void Reset_ReturnsToMelee()
        {
            var boss = new BossStateMachine();
            boss.Evaluate(healthFraction: 0.60f, liveAdds: 0, secondsInPhase: 1);
            boss.Evaluate(healthFraction: 0f, liveAdds: 0, secondsInPhase: 1);

            boss.Reset();

            Assert.AreEqual(BossPhase.Melee, boss.Phase);
        }
    }
}