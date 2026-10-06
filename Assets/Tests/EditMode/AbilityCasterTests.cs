using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The whole server-side cast pipeline — equip, validate, pay, book, resolve — as one plain
    /// object driven by a chosen clock.
    ///
    /// This is where the issue's requirements meet each other: gear fills the slots, the cast is
    /// refused for mana, distance, the Global Cooldown or the slot's own cooldown, an accepted
    /// cast spends exactly its cost and queues its effect for the moment the cast time elapses.
    /// A fake mana pool stands in for <c>PlayerStats</c> so no FishNet attribute or live server
    /// stands between a test and the logic it is checking.
    /// </summary>
    public sealed class AbilityCasterTests
    {
        private const double Now = 100d;
        private static readonly Vector3 Here = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 There = new Vector3(5f, 0f, 0f);

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
        public void NewCaster_StartsWithAnEmptyBarAndNothingRunning()
        {
            var caster = new AbilityCaster();

            for (int slot = 0; slot < AbilityLoadout.MaxSlots; slot++)
                Assert.IsNull(caster.Slots[slot]);

            Assert.IsTrue(caster.Cooldowns.IsReady(0, Now));
            Assert.IsTrue(caster.Cooldowns.IsGlobalCooldownReady(Now));
            Assert.AreEqual(0, caster.PendingCastCount);
        }

        [Test]
        public void EquipGear_InjectsGearAbilitiesIntoTheBar()
        {
            AbilityData skill = NewAbility("Slash");
            WeaponData weapon = NewWeapon(skill);
            AbilityData heal = NewAbility("Second Wind");
            ArmorData armor = NewArmor(heal);
            var caster = new AbilityCaster();

            caster.EquipGear(weapon, armor);

            Assert.AreSame(skill, caster.Slots[0]);
            Assert.AreSame(heal, caster.Slots[1]);
        }

        [Test]
        public void TryCast_RefusesASlotTheBarDoesNotFill()
        {
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(NewAbility("Slash")), null);

            AbilityCastResult result = caster.TryCast(
                1, currentMana: 50, Here, There, Now, out int manaCost);

            Assert.AreEqual(AbilityCastResult.EmptySlot, result);
            Assert.AreEqual(0, manaCost, "A refused cast charges nothing.");
            Assert.AreEqual(0, caster.PendingCastCount, "A refused cast queues nothing.");
            Assert.IsTrue(caster.Cooldowns.IsReady(0, Now), "A refused cast books no cooldown.");
            Assert.IsTrue(caster.Cooldowns.IsGlobalCooldownReady(Now),
                "A refused cast does not start the Global Cooldown.");
        }

        [Test]
        public void TryCast_ReportsAnInvalidSlotIndex()
        {
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(NewAbility("Slash")), null);

            Assert.AreEqual(AbilityCastResult.InvalidSlot,
                caster.TryCast(-1, 50, Here, There, Now, out int manaCost));
            Assert.AreEqual(0, manaCost);
        }

        [Test]
        public void TryCast_RefusesACastTheCasterCannotPayFor()
        {
            AbilityData skill = NewAbility("Fireball", manaCost: 20);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            AbilityCastResult result = caster.TryCast(0, 19, Here, There, Now, out int manaCost);

            Assert.AreEqual(AbilityCastResult.NotEnoughMana, result);
            Assert.AreEqual(0, manaCost);
            Assert.IsTrue(caster.Cooldowns.IsReady(0, Now),
                "You do not lock your own skill by failing to afford it.");
        }

        [Test]
        public void TryCast_RefusesATargetBeyondTheCastRange()
        {
            AbilityData skill = NewAbility("Fireball", castRange: 4f);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            // There is 5 metres away; the skill reaches 4.
            AbilityCastResult result = caster.TryCast(0, 50, Here, There, Now, out int manaCost);

            Assert.AreEqual(AbilityCastResult.OutOfRange, result);
            Assert.AreEqual(0, manaCost);
            Assert.AreEqual(0, caster.PendingCastCount);
        }

        [Test]
        public void TryCast_AcceptedCastReturnsItsExactCost()
        {
            AbilityData skill = NewAbility("Fireball", manaCost: 20, cooldown: 6f, castTime: 1.5f);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            AbilityCastResult result = caster.TryCast(
                0, currentMana: 50, Here, There, Now, out int manaCost);

            Assert.AreEqual(AbilityCastResult.Success, result);
            Assert.AreEqual(20, manaCost,
                "The caller charges what the asset says, not what the client guessed.");
        }

        [Test]
        public void TryCast_AcceptedCastStartsTheSlotCooldownTheGlobalCooldownAndTheChannel()
        {
            AbilityData skill = NewAbility("Fireball", cooldown: 6f, castTime: 1.5f);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            caster.TryCast(0, 50, Here, There, Now, out _);

            // The channel runs for the cast time...
            Assert.IsTrue(caster.Cooldowns.IsCasting(Now));
            Assert.IsFalse(caster.Cooldowns.IsCasting(Now + 1.5d));
            // ...the Global Cooldown starts the moment the cast does...
            Assert.IsFalse(caster.Cooldowns.IsGlobalCooldownReady(Now));
            // ...and the slot's own cooldown counts from the moment the effect lands.
            Assert.IsFalse(caster.Cooldowns.IsReady(0, Now + 1.5d));
            Assert.IsTrue(caster.Cooldowns.IsReady(0, Now + 1.5d + 6d));
        }

        [Test]
        public void TryCast_RefusesASecondCastWhileChannelling()
        {
            AbilityData channel = NewAbility("Fireball", castTime: 2f);
            AbilityData instant = NewAbility("Slash");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(channel, instant), null);

            caster.TryCast(0, 50, Here, There, Now, out _);
            AbilityCastResult second = caster.TryCast(1, 50, Here, There, Now + 0.5d, out int manaCost);

            Assert.AreEqual(AbilityCastResult.AlreadyCasting, second,
                "Nothing else fires until the cast in progress resolves.");
            Assert.AreEqual(0, manaCost);
        }

        [Test]
        public void TryCast_RefusesASecondCastDuringTheGlobalCooldownEvenInAnotherSlot()
        {
            AbilityData first = NewAbility("Slash");
            AbilityData second = NewAbility("Bolt");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(first, second), null);

            caster.TryCast(0, 50, Here, There, Now, out _);
            AbilityCastResult result = caster.TryCast(1, 50, Here, There, Now + 0.5d, out int manaCost);

            Assert.AreEqual(AbilityCastResult.GlobalCooldownActive, result);
            Assert.AreEqual(0, manaCost);
        }

        [Test]
        public void TryCast_RefusesAReCastOfTheSameSlotAfterTheGlobalCooldownButBeforeItsOwn()
        {
            // The distinction the two cooldowns exist for: the GCD is short and shared, the
            // slot's cooldown is long and private.
            AbilityData skill = NewAbility("Slash", cooldown: 10f);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            caster.TryCast(0, 50, Here, There, Now, out _);

            double afterGlobalCooldown = Now + AbilityCooldownTracker.GlobalCooldown;
            Assert.AreEqual(AbilityCastResult.SlotOnCooldown,
                caster.TryCast(0, 50, Here, There, afterGlobalCooldown, out _),
                "The GCD having lifted does not free a slot that is still recovering.");
            Assert.AreEqual(AbilityCastResult.Success,
                caster.TryCast(0, 50, Here, There, Now + 10d, out _));
        }

        [Test]
        public void TryCast_QueuesAnInstantCastThatIsImmediatelyDue()
        {
            AbilityData skill = NewAbility("Slash");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            caster.TryCast(0, 50, Here, There, Now, out _);

            List<AbilityCastEffect> due = caster.TakeDueEffects(Now);

            Assert.AreEqual(1, due.Count, "A zero cast time resolves on the very next poll.");
            Assert.AreSame(skill, due[0].Ability);
            Assert.AreEqual(0, due[0].Slot);
            Assert.AreEqual(There, due[0].TargetPosition,
                "The effect resolves against where the caster aimed when they cast.");
            Assert.AreEqual(0, caster.PendingCastCount);
        }

        [Test]
        public void TryCast_WithACastTimeResolvesOnlyOnceTheCastTimeElapses()
        {
            AbilityData skill = NewAbility("Fireball", castTime: 1.5f);
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            caster.TryCast(0, 50, Here, There, Now, out _);

            Assert.IsEmpty(caster.TakeDueEffects(Now + 1.49d),
                "A cast with a cast time must not land early.");
            Assert.AreEqual(1, caster.PendingCastCount);

            List<AbilityCastEffect> due = caster.TakeDueEffects(Now + 1.5d);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(0, caster.PendingCastCount, "A resolved cast leaves the queue exactly once.");
            Assert.IsEmpty(caster.TakeDueEffects(Now + 9d),
                "Draining twice must not resolve the same cast twice.");
        }

        [Test]
        public void TakeDueEffects_KeepsStartOrderWhenSeveralAreWaiting()
        {
            // Two casts can only be queued if the first was instant and never drained, and
            // effects must then land in the order they were started.
            AbilityData skill = NewAbility("Slash");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(skill), null);

            caster.TryCast(0, 50, Here, There, Now, out _);
            caster.TryCast(0, 50, Here, new Vector3(9f, 0f, 0f),
                Now + AbilityCooldownTracker.GlobalCooldown, out _);

            List<AbilityCastEffect> due = caster.TakeDueEffects(Now + AbilityCooldownTracker.GlobalCooldown);

            Assert.AreEqual(2, due.Count);
            Assert.AreEqual(There, due[0].TargetPosition, "Oldest cast resolves first.");
            Assert.AreEqual(new Vector3(9f, 0f, 0f), due[1].TargetPosition);
        }

        [Test]
        public void EquipGear_DropsCastsThatWereStillChannelling()
        {
            AbilityData oldSkill = NewAbility("Fireball", castTime: 2f);
            AbilityData newSkill = NewAbility("Slash");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(oldSkill), null);
            caster.TryCast(0, 50, Here, There, Now, out _);

            caster.EquipGear(NewWeapon(newSkill), null);

            Assert.AreEqual(0, caster.PendingCastCount,
                "A cast started by gear the player just took off must never resolve.");
            Assert.IsEmpty(caster.TakeDueEffects(Now + 5d));
            Assert.AreSame(newSkill, caster.Slots[0]);
        }

        [Test]
        public void EquipGear_KeepsTheCooldownsTheOldGearHadEarned()
        {
            AbilityData oldSkill = NewAbility("Fireball", cooldown: 30f, castTime: 0f);
            AbilityData newSkill = NewAbility("Slash");
            var caster = new AbilityCaster();
            caster.EquipGear(NewWeapon(oldSkill), null);
            caster.TryCast(0, 50, Here, There, Now, out _);

            caster.EquipGear(NewWeapon(newSkill), null);

            // Cooldowns belong to the slot, not to the asset that filled it — otherwise
            // swapping gear would be a way to reset your own recovery timer.
            Assert.AreEqual(AbilityCastResult.SlotOnCooldown,
                caster.TryCast(0, 50, Here, There, Now + 2d, out _));
            Assert.AreEqual(AbilityCastResult.Success,
                caster.TryCast(0, 50, Here, There, Now + 30d, out _));
        }

        private AbilityData NewAbility(
            string abilityName,
            int manaCost = 0,
            float cooldown = 0f,
            float castTime = 0f,
            float castRange = 20f)
        {
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = abilityName;

            var inspector = new SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.FindProperty("_manaCost").intValue = manaCost;
            inspector.FindProperty("_cooldown").floatValue = cooldown;
            inspector.FindProperty("_castTime").floatValue = castTime;
            inspector.FindProperty("_castRange").floatValue = castRange;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }

        private WeaponData NewWeapon(params AbilityData[] abilities)
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();

            var inspector = new SerializedObject(weapon);
            SetAbilities(inspector, abilities);
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(weapon);
            return weapon;
        }

        private ArmorData NewArmor(params AbilityData[] abilities)
        {
            ArmorData armor = ScriptableObject.CreateInstance<ArmorData>();

            var inspector = new SerializedObject(armor);
            SetAbilities(inspector, abilities);
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(armor);
            return armor;
        }

        private static void SetAbilities(SerializedObject inspector, AbilityData[] abilities)
        {
            SerializedProperty property = inspector.FindProperty("_abilities");
            property.arraySize = abilities.Length;
            for (int i = 0; i < abilities.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = abilities[i];
        }
    }
}
