using System;
using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The server-tracked cooldown system the issue asks for, driven entirely by a chosen
    /// <c>now</c>. No sleeping, no frame loops: every assertion is about what the tracker says
    /// at an exact instant, which is what makes the boundaries (ready exactly at the unlock
    /// tick, never shortened by a later start) checkable rather than approximate.
    /// </summary>
    public sealed class AbilityCooldownTrackerTests
    {
        private const double Now = 100d;

        [Test]
        public void NewTracker_StartsEverySlotAndTheGlobalCooldownReady()
        {
            var tracker = new AbilityCooldownTracker();

            for (int slot = 0; slot < tracker.SlotCount; slot++)
                Assert.IsTrue(tracker.IsReady(slot, Now), $"Slot {slot} must start unspent.");

            Assert.IsTrue(tracker.IsGlobalCooldownReady(Now),
                "Nothing has been cast, so the Global Cooldown cannot be running.");
            Assert.IsFalse(tracker.IsCasting(Now));
        }

        [Test]
        public void SlotCount_DefaultsToTheWidthOfTheBar()
        {
            Assert.AreEqual(AbilityLoadout.MaxSlots, new AbilityCooldownTracker().SlotCount);
        }

        [Test]
        public void SlotCount_RejectsANonPositiveSlotCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityCooldownTracker(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityCooldownTracker(-1));
        }

        [Test]
        public void StartCooldown_LocksTheSlotUntilTheCooldownElapses()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.StartCooldown(0, 6f, Now);

            Assert.IsFalse(tracker.IsReady(0, Now));
            Assert.IsFalse(tracker.IsReady(0, Now + 5.99d));
            Assert.IsTrue(tracker.IsReady(0, Now + 6d), "The slot unlocks exactly when the cooldown ends.");
        }

        [Test]
        public void StartCooldown_LeavesOtherSlotsAlone()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.StartCooldown(0, 10f, Now);

            Assert.IsTrue(tracker.IsReady(1, Now),
                "Casting from one slot must not lock the rest of the bar.");
        }

        [Test]
        public void StartCooldown_NeverShortensALongerLock()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.StartCooldown(0, 10f, Now);
            tracker.StartCooldown(0, 1f, Now + 1d);

            Assert.IsFalse(tracker.IsReady(0, Now + 5d),
                "A shorter cooldown starting later must not erase the recovery already owed.");
            Assert.IsTrue(tracker.IsReady(0, Now + 10d));
        }

        [Test]
        public void StartCooldown_TreatsNegativeCooldownsAsInstant()
        {
            // Negative only arrives via hand-edited YAML, and the answer is "no lock", not
            // "unlock in the past forever".
            var tracker = new AbilityCooldownTracker();

            tracker.StartCooldown(0, -5f, Now);

            Assert.IsTrue(tracker.IsReady(0, Now));
        }

        [Test]
        public void StartCooldown_RejectsASlotTheTrackerDoesNotOwn()
        {
            var tracker = new AbilityCooldownTracker();

            Assert.Throws<ArgumentOutOfRangeException>(() => tracker.StartCooldown(-1, 1f, Now));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => tracker.StartCooldown(AbilityLoadout.MaxSlots, 1f, Now));
        }

        [Test]
        public void IsReady_IsFalseForSlotsThatDoNotExist()
        {
            var tracker = new AbilityCooldownTracker();

            Assert.IsFalse(tracker.IsReady(-1, Now));
            Assert.IsFalse(tracker.IsReady(AbilityLoadout.MaxSlots, Now));
        }

        [Test]
        public void SecondsUntilReady_DrainsToZeroAsTimePasses()
        {
            var tracker = new AbilityCooldownTracker();
            tracker.StartCooldown(0, 4f, Now);

            Assert.AreEqual(4f, tracker.SecondsUntilReady(0, Now), 0.0001f);
            Assert.AreEqual(1.5f, tracker.SecondsUntilReady(0, Now + 2.5d), 0.0001f);
            Assert.AreEqual(0f, tracker.SecondsUntilReady(0, Now + 4d), 0.0001f,
                "A ready slot has nothing left to wait for.");
            Assert.AreEqual(0f, tracker.SecondsUntilReady(0, Now + 40d), 0.0001f,
                "Time past the unlock never goes negative.");
        }

        [Test]
        public void SecondsUntilReady_IsZeroForSlotsThatDoNotExist()
        {
            var tracker = new AbilityCooldownTracker();

            Assert.AreEqual(0f, tracker.SecondsUntilReady(-1, Now));
            Assert.AreEqual(0f, tracker.SecondsUntilReady(AbilityLoadout.MaxSlots, Now));
        }

        [Test]
        public void StartGlobalCooldown_LocksEverySlotAtOnce()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.StartGlobalCooldown(Now);

            Assert.IsFalse(tracker.IsGlobalCooldownReady(Now));
            Assert.AreEqual(AbilityCooldownTracker.GlobalCooldown,
                tracker.SecondsUntilGlobalCooldownReady(Now), 0.0001f);
            Assert.IsTrue(tracker.IsGlobalCooldownReady(Now + AbilityCooldownTracker.GlobalCooldown),
                "The GCD lifts on the tick it expires, exactly like a slot cooldown.");
        }

        [Test]
        public void StartGlobalCooldown_NeverShortensTheRunningLock()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.StartGlobalCooldown(Now);
            tracker.StartGlobalCooldown(Now + 1d);

            Assert.IsFalse(tracker.IsGlobalCooldownReady(Now + 2d),
                "Recasting must not push the shared lockout backwards.");
            Assert.IsTrue(tracker.IsGlobalCooldownReady(Now + AbilityCooldownTracker.GlobalCooldown));
        }

        [Test]
        public void GlobalCooldown_IsAPositiveConstant()
        {
            Assert.Greater(AbilityCooldownTracker.GlobalCooldown, 0f,
                "A zero-second GCD would not be a Global Cooldown.");
        }

        [Test]
        public void BeginCastUntil_LocksCastingUntilTheGivenInstant()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.BeginCastUntil(Now + 1.5d);

            Assert.IsTrue(tracker.IsCasting(Now));
            Assert.IsTrue(tracker.IsCasting(Now + 1.49d));
            Assert.IsFalse(tracker.IsCasting(Now + 1.5d),
                "The cast ends on the instant it was scheduled for.");
            Assert.AreEqual(1.5d, tracker.SecondsUntilCastCompletes(Now), 0.0001d);
            Assert.AreEqual(0d, tracker.SecondsUntilCastCompletes(Now + 9d), 0.0001d);
        }

        [Test]
        public void BeginCastUntil_ANullCastWindowIsAlreadyOver()
        {
            // A zero cast time resolves immediately, so it must never report as channelling —
            // otherwise every instant skill would refuse the next input.
            var tracker = new AbilityCooldownTracker();

            tracker.BeginCastUntil(Now);

            Assert.IsFalse(tracker.IsCasting(Now));
        }

        [Test]
        public void BeginCastUntil_KeepsTheFurthestWindowRunning()
        {
            var tracker = new AbilityCooldownTracker();

            tracker.BeginCastUntil(Now + 2d);
            tracker.BeginCastUntil(Now + 1d);

            Assert.IsTrue(tracker.IsCasting(Now + 1.5d),
                "A later, shorter window must not truncate one already channelling.");
        }
    }
}
