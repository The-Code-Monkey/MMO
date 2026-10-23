using MMO.Content;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The pure aura arithmetic: refresh versus stacking, tick cadence, expiry, crowd-control
    /// gating and the flat stat bonus. All times are supplied by the caller.
    /// </summary>
    public sealed class AuraRulesTests
    {
        [TearDown]
        public void TearDown() => TestContentData.Cleanup();

        [Test]
        public void Apply_NewAuraStartsAtOneStackAndBooksItsTimers()
        {
            AuraData poison = TestContentData.Aura("poison", AuraKind.Dot,
                durationSeconds: 10f, tickIntervalSeconds: 2f, magnitudePerTick: 4);

            AuraState state = AuraRules.Apply(default, poison, now: 5d);

            Assert.AreSame(poison, state.Data);
            Assert.AreEqual(1, state.Stacks);
            Assert.AreEqual(15d, state.ExpiresAt, 0.0001d);
            Assert.AreEqual(7d, state.NextTickAt, 0.0001d);
        }

        [Test]
        public void Apply_RefreshesWithoutStacking_WhenMaxStacksIsOne()
        {
            AuraData buff = TestContentData.Aura("buff", AuraKind.Buff,
                durationSeconds: 10f, tickIntervalSeconds: 2f, maxStacks: 1);

            AuraState first = AuraRules.Apply(default, buff, now: 0d);
            AuraState refreshed = AuraRules.Apply(first, buff, now: 3d);

            Assert.AreEqual(1, refreshed.Stacks);
            Assert.AreEqual(13d, refreshed.ExpiresAt, 0.0001d);
            Assert.AreEqual(5d, refreshed.NextTickAt, 0.0001d);
        }

        [Test]
        public void Apply_StacksUpToTheAuthoredMaximum()
        {
            AuraData buff = TestContentData.Aura("buff", AuraKind.Buff,
                durationSeconds: 10f, maxStacks: 3);

            AuraState state = default;
            for (int i = 0; i < 4; i++)
                state = AuraRules.Apply(state, buff, now: 0d);

            Assert.AreEqual(3, state.Stacks);
        }

        [Test]
        public void Tick_DotDealsOneTickPerInterval()
        {
            AuraData poison = TestContentData.Aura("poison", AuraKind.Dot,
                durationSeconds: 10f, tickIntervalSeconds: 2f, magnitudePerTick: 4, maxStacks: 3);

            AuraState state = AuraRules.Apply(default, poison, now: 0d);
            state = AuraRules.Apply(state, poison, now: 0d);

            AuraTickResult due = AuraRules.Tick(state, now: 2d);
            Assert.IsTrue(due.Ticked);
            Assert.AreEqual(8, due.Damage, "Two stacks double the per-tick damage.");
            Assert.AreEqual(0, due.Heal);
            Assert.AreEqual(4d, due.State.NextTickAt, 0.0001d);

            AuraTickResult early = AuraRules.Tick(due.State, now: 3d);
            Assert.IsFalse(early.Ticked);
            Assert.AreEqual(0, early.Damage);
        }

        [Test]
        public void Tick_HotHealsOnItsInterval()
        {
            AuraData renew = TestContentData.Aura("renew", AuraKind.Hot,
                durationSeconds: 10f, tickIntervalSeconds: 1f, magnitudePerTick: 3);

            AuraState state = AuraRules.Apply(default, renew, now: 0d);
            AuraTickResult result = AuraRules.Tick(state, now: 1d);

            Assert.IsTrue(result.Ticked);
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(3, result.Heal);
        }

        [Test]
        public void Tick_ReportsExpiryAtTheEndOfTheDuration()
        {
            AuraData poison = TestContentData.Aura("poison", AuraKind.Dot,
                durationSeconds: 5f, tickIntervalSeconds: 2f, magnitudePerTick: 1);

            AuraState state = AuraRules.Apply(default, poison, now: 0d);
            AuraTickResult result = AuraRules.Tick(state, now: 5d);

            Assert.IsTrue(result.Expired);
        }

        [Test]
        public void Tick_AnAuraWithNoIntervalNeverTicks()
        {
            AuraData buff = TestContentData.Aura("buff", AuraKind.Buff,
                durationSeconds: 5f, tickIntervalSeconds: 0f, flatStatBonus: 2);

            AuraState state = AuraRules.Apply(default, buff, now: 0d);
            AuraTickResult result = AuraRules.Tick(state, now: 4d);

            Assert.IsFalse(result.Ticked);
            Assert.IsFalse(result.Expired);
        }

        [Test]
        public void BlocksActions_IsTrueForStunOnly()
        {
            AuraState stun = AuraRules.Apply(default, TestContentData.Aura("stun", AuraKind.Stun), 0d);
            AuraState root = AuraRules.Apply(default, TestContentData.Aura("root", AuraKind.Root), 0d);
            AuraState buff = AuraRules.Apply(default, TestContentData.Aura("buff", AuraKind.Buff), 0d);

            Assert.IsTrue(AuraRules.BlocksActions(new[] { stun }));
            Assert.IsFalse(AuraRules.BlocksActions(new[] { root }));
            Assert.IsFalse(AuraRules.BlocksActions(new[] { buff }));
            Assert.IsFalse(AuraRules.BlocksActions(null));
        }

        [Test]
        public void BlocksMovement_IsTrueForStunOrRoot()
        {
            AuraState stun = AuraRules.Apply(default, TestContentData.Aura("stun", AuraKind.Stun), 0d);
            AuraState root = AuraRules.Apply(default, TestContentData.Aura("root", AuraKind.Root), 0d);
            AuraState buff = AuraRules.Apply(default, TestContentData.Aura("buff", AuraKind.Buff), 0d);

            Assert.IsTrue(AuraRules.BlocksMovement(new[] { stun }));
            Assert.IsTrue(AuraRules.BlocksMovement(new[] { root }));
            Assert.IsFalse(AuraRules.BlocksMovement(new[] { buff }));
            Assert.IsFalse(AuraRules.BlocksMovement(null));
        }

        [Test]
        public void TotalFlatBonus_ScalesWithStacksAndMatchesTheStat()
        {
            AuraData health = TestContentData.Aura("health", AuraKind.Buff,
                stat: AuraStatKind.MaxHealth, durationSeconds: 10f, flatStatBonus: 5, maxStacks: 3);
            AuraData mana = TestContentData.Aura("mana", AuraKind.Buff,
                stat: AuraStatKind.MaxMana, durationSeconds: 10f, flatStatBonus: 3);

            AuraState healthState = default;
            for (int i = 0; i < 3; i++)
                healthState = AuraRules.Apply(healthState, health, 0d);

            Assert.AreEqual(15, AuraRules.TotalFlatBonus(new[] { healthState }, AuraStatKind.MaxHealth));
            Assert.AreEqual(0, AuraRules.TotalFlatBonus(new[] { healthState }, AuraStatKind.MaxMana));
            Assert.AreEqual(3, AuraRules.TotalFlatBonus(
                new[] { AuraRules.Apply(default, mana, 0d) }, AuraStatKind.MaxMana));
        }
    }
}
