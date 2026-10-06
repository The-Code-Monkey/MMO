using System;
using System.Collections.Generic;
using System.Reflection;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The server's validation gate, one rejection at a time: mana cost, distance, the Global
    /// Cooldown, the per-slot cooldown and the structural sanity of the request itself.
    ///
    /// Each case is constructed so that exactly one rule is broken, and a few cases deliberately
    /// break several at once to pin which reason wins — a cast refuser whose verdict order can
    /// drift is a refuser whose combat log lies.
    /// </summary>
    public sealed class AbilityCastRulesTests
    {
        private const double Now = 100d;

        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _scratch.Clear();
        }

        [Test]
        public void Validate_AcceptsACastThatBreaksNoRules()
        {
            AbilityData fireball = NewAbility("Fireball", manaCost: 20, castRange: 20f);
            AbilityCooldownTracker tracker = new();
            AbilityCastResult result = AbilityCastRules.Validate(
                0, Slots(fireball), tracker, currentMana: 50, distanceToTarget: 10f, now: Now);

            Assert.AreEqual(AbilityCastResult.Success, result);
        }

        [Test]
        public void Validate_RejectsSlotIndexesOutsideTheBar()
        {
            AbilityData skill = NewAbility("Slash");
            AbilityCooldownTracker tracker = new();

            Assert.AreEqual(AbilityCastResult.InvalidSlot,
                AbilityCastRules.Validate(-1, Slots(skill), tracker, 50, 0f, Now));
            Assert.AreEqual(AbilityCastResult.InvalidSlot,
                AbilityCastRules.Validate(1, Slots(skill), tracker, 50, 0f, Now));
            Assert.AreEqual(AbilityCastResult.InvalidSlot,
                AbilityCastRules.Validate(int.MaxValue, Slots(skill), tracker, 50, 0f, Now));
        }

        [Test]
        public void Validate_RejectsAnEmptySlot()
        {
            // Slot 0 filled, slot 1 left as the null every empty bar slot starts as.
            AbilityCooldownTracker tracker = new();

            Assert.AreEqual(AbilityCastResult.EmptySlot,
                AbilityCastRules.Validate(1, Slots(NewAbility("Slash")), tracker, 50, 0f, Now));
        }

        [Test]
        public void Validate_RejectsACastWhileAnotherIsStillChannelling()
        {
            AbilityData skill = NewAbility("Fireball", castTime: 1.5f);
            AbilityCooldownTracker tracker = new();
            tracker.BeginCastUntil(Now + 1.5d);

            Assert.AreEqual(AbilityCastResult.AlreadyCasting,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 50, 0f, Now));
            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 50, 0f, Now + 1.5d),
                "The instant the channel ends the next cast is legal again.");
        }

        [Test]
        public void Validate_RejectsACastDuringTheGlobalCooldown()
        {
            AbilityData skill = NewAbility("Slash");
            AbilityCooldownTracker tracker = new();
            tracker.StartGlobalCooldown(Now);

            Assert.AreEqual(AbilityCastResult.GlobalCooldownActive,
                AbilityCastRules.Validate(0, Slots(skill), tracker, currentMana: 50, distanceToTarget: 0f, now: Now));
            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 50, 0f,
                    Now + AbilityCooldownTracker.GlobalCooldown));
        }

        [Test]
        public void Validate_RejectsACastWhoseSlotIsStillOnCooldown()
        {
            AbilityData skill = NewAbility("Slash");
            AbilityCooldownTracker tracker = new();
            tracker.StartCooldown(0, 5f, Now);

            Assert.AreEqual(AbilityCastResult.SlotOnCooldown,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 50, 0f, Now));
            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 50, 0f, Now + 5d));
        }

        [Test]
        public void Validate_RejectsACastTheCasterCannotPayFor()
        {
            AbilityData skill = NewAbility("Fireball", manaCost: 20);

            Assert.AreEqual(AbilityCastResult.NotEnoughMana,
                AbilityCastRules.Validate(0, Slots(skill), new AbilityCooldownTracker(), 19, 0f, Now));
            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(skill), new AbilityCooldownTracker(), 20, 0f, Now),
                "Exactly enough mana is enough; the check is a floor, not a buffer.");
        }

        [Test]
        public void Validate_RejectsANegativeManaPayload()
        {
            // A malformed pool is a refusal, never a free cast.
            AbilityData freeSkill = NewAbility("Slash", manaCost: 0);

            Assert.AreEqual(AbilityCastResult.NotEnoughMana,
                AbilityCastRules.Validate(0, Slots(freeSkill), new AbilityCooldownTracker(), -1, 0f, Now));
        }

        [Test]
        public void Validate_RejectsATargetBeyondTheCastRange()
        {
            AbilityData skill = NewAbility("Fireball", castRange: 12f);

            Assert.AreEqual(AbilityCastResult.OutOfRange,
                AbilityCastRules.Validate(0, Slots(skill), new AbilityCooldownTracker(), 50, 12.1f, Now));
            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(skill), new AbilityCooldownTracker(), 50, 12f, Now),
                "Standing exactly at cast range is still in range.");
        }

        [Test]
        public void Validate_SkipsTheRangeCheckForSelfTargetedSkills()
        {
            // Second Wind is cast on oneself; a client that reported its own position anyway
            // must not be able to fail the cast by aiming badly.
            AbilityData selfHeal = NewAbility("Second Wind", castRange: 0f, logic: AbilityLogic.SelfHeal);

            Assert.AreEqual(AbilityCastResult.Success,
                AbilityCastRules.Validate(0, Slots(selfHeal), new AbilityCooldownTracker(), 50, 999f, Now));
        }

        [Test]
        public void Validate_RangeChecksEveryNonSelfSkillIncludingUnknownLogics()
        {
            // An asset hand-edited to a logic value nobody has implemented still aimed
            // somewhere, so it keeps the safety check rather than skipping it. The value is
            // poked through reflection because it is deliberately outside the enum.
            AbilityData unknown = NewAbility("Mystery", castRange: 5f);
            typeof(AbilityData)
                .GetField("_logic", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(unknown, (AbilityLogic)99);

            Assert.AreEqual(AbilityCastResult.OutOfRange,
                AbilityCastRules.Validate(0, Slots(unknown), new AbilityCooldownTracker(), 50, 6f, Now));
        }

        [Test]
        public void Validate_ReportsTheEarliestBrokenRuleFirst()
        {
            // The ordering contract: structure beats timing, timing beats resources,
            // resources beat geometry.
            AbilityData skill = NewAbility("Fireball", manaCost: 20, castRange: 5f);
            AbilityCooldownTracker tracker = new();
            tracker.BeginCastUntil(Now + 1d);
            tracker.StartGlobalCooldown(Now);
            tracker.StartCooldown(0, 3f, Now);

            Assert.AreEqual(AbilityCastResult.InvalidSlot,
                AbilityCastRules.Validate(9, Slots(skill), tracker, 0, 999f, Now),
                "A request for a slot that does not exist is wrong before anything else is.");
            Assert.AreEqual(AbilityCastResult.EmptySlot,
                AbilityCastRules.Validate(1, Slots(skill), tracker, 0, 999f, Now),
                "An empty slot is wrong before the timing checks run.");
            Assert.AreEqual(AbilityCastResult.AlreadyCasting,
                AbilityCastRules.Validate(0, Slots(skill), tracker, 0, 999f, Now),
                "A channelling caster reports that before the GCD it also has running.");
            Assert.AreEqual(AbilityCastResult.NotEnoughMana,
                AbilityCastRules.Validate(0, Slots(skill), new AbilityCooldownTracker(), 0, 999f, Now),
                "Mana outranks range: pay first, aim second.");
        }

        [Test]
        public void Validate_RejectsNullRequestsWithoutAnswering()
        {
            // Nulls here are a wiring bug on the server, not a cast a player could ever send,
            // so they must not be silently "refused" like a legitimate bad input.
            AbilityCooldownTracker tracker = new();

            Assert.Throws<ArgumentNullException>(
                () => AbilityCastRules.Validate(0, null, tracker, 50, 0f, Now));
            Assert.Throws<ArgumentNullException>(
                () => AbilityCastRules.Validate(0, Slots(NewAbility("Slash")), null, 50, 0f, Now));
        }

        private AbilityData NewAbility(
            string abilityName,
            int manaCost = 0,
            float cooldown = 0f,
            float castTime = 0f,
            float castRange = 0f,
            AbilityLogic logic = AbilityLogic.TargetedDamage)
        {
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = abilityName;

            var inspector = new UnityEditor.SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.FindProperty("_manaCost").intValue = manaCost;
            inspector.FindProperty("_cooldown").floatValue = cooldown;
            inspector.FindProperty("_castTime").floatValue = castTime;
            inspector.FindProperty("_castRange").floatValue = castRange;
            inspector.FindProperty("_logic").enumValueIndex = (int)logic;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }

        /// <summary>A six-wide bar with one ability parked in slot 0, like a real loadout.</summary>
        private static IReadOnlyList<AbilityData> Slots(AbilityData slot0)
        {
            var slots = new AbilityData[AbilityLoadout.MaxSlots];
            slots[0] = slot0;
            return slots;
        }
    }
}
