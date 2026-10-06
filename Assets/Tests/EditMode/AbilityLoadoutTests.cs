using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The injection rule behind "you are what you wear": gear hands its <c>Abilities</c> array
    /// to the loadout, and the loadout decides what survives onto a fixed bar.
    ///
    /// Everything here is plain C# over three ScriptableObjects — no server, no clock, no scene —
    /// which is only possible because <see cref="AbilityLoadout"/> was kept free of networking.
    /// The two shipped pieces of gear are driven through the same path as a regression check
    /// against the real assets.
    /// </summary>
    public sealed class AbilityLoadoutTests
    {
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
        public void MaxSlots_IsSix()
        {
            // Six is baked into the replicated slot names on PlayerAbilities, so the two halves
            // agreeing on it is load-bearing, not a detail.
            Assert.AreEqual(6, AbilityLoadout.MaxSlots);
        }

        [Test]
        public void NewLoadout_IsAFullBarOfEmptySlots()
        {
            var loadout = new AbilityLoadout();

            Assert.AreEqual(AbilityLoadout.MaxSlots, loadout.Slots.Count,
                "The bar has a fixed width; clients index into it by slot number.");
            for (int slot = 0; slot < loadout.Slots.Count; slot++)
                Assert.IsNull(loadout.Slots[slot], $"Slot {slot} should start empty.");
        }

        [Test]
        public void GetSlot_ReturnsNullForIndexesOutsideTheBar()
        {
            var loadout = new AbilityLoadout();

            Assert.IsNull(loadout.GetSlot(-1));
            Assert.IsNull(loadout.GetSlot(AbilityLoadout.MaxSlots));
            Assert.IsNull(loadout.GetSlot(int.MaxValue));
        }

        [Test]
        public void RebuildFromGear_WithNoGearLeavesTheBarEmpty()
        {
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(null, null);

            for (int slot = 0; slot < loadout.Slots.Count; slot++)
                Assert.IsNull(loadout.Slots[slot]);
        }

        [Test]
        public void RebuildFromGear_FillsWeaponAbilitiesFirst()
        {
            AbilityData skill = NewAbility("Slash");
            WeaponData weapon = NewWeapon("Test Sword", skill);
            AbilityData potion = NewAbility("Mend");
            ArmorData armor = NewArmor("Test Plate", potion);
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(weapon, armor);

            // The weapon is the primary skill source, so it owns the first slot.
            Assert.AreSame(skill, loadout.GetSlot(0));
            Assert.AreSame(potion, loadout.GetSlot(1));
        }

        [Test]
        public void RebuildFromGear_CollapsesAGrantedTwiceAbilityIntoOneSlot()
        {
            AbilityData cleave = NewAbility("Cleave");
            WeaponData weapon = NewWeapon("Test Sword", cleave);
            ArmorData armor = NewArmor("Test Plate", cleave);
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(weapon, armor);

            Assert.AreSame(cleave, loadout.GetSlot(0));
            Assert.IsNull(loadout.GetSlot(1),
                "Wearing two pieces that grant the same skill must not waste a second slot on it.");
        }

        [Test]
        public void RebuildFromGear_DropsNullEntriesInTheAbilityArrays()
        {
            AbilityData skill = NewAbility("Slash");
            WeaponData weapon = NewWeapon("Test Sword", null, skill);
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(weapon, null);

            Assert.AreSame(skill, loadout.GetSlot(0),
                "A hole left by a deleted reference must not consume a slot.");
            Assert.IsNull(loadout.GetSlot(1));
        }

        [Test]
        public void RebuildFromGear_CapsTheBarAtMaxSlots()
        {
            var grants = new List<AbilityData>();
            for (int i = 0; i < AbilityLoadout.MaxSlots + 3; i++)
                grants.Add(NewAbility($"Skill {i}"));

            WeaponData weapon = NewWeapon("Test Sword", grants.ToArray());
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(weapon, null);

            Assert.AreEqual(AbilityLoadout.MaxSlots, CountFilled(loadout),
                "Gear past the end of the bar is dropped, not stacked past it.");
            for (int slot = 0; slot < AbilityLoadout.MaxSlots; slot++)
                Assert.IsNotNull(loadout.Slots[slot]);
        }

        [Test]
        public void RebuildFromGear_ReplacesThePreviousBar()
        {
            AbilityData slash = NewAbility("Slash");
            WeaponData sword = NewWeapon("Test Sword", slash);
            AbilityData bolt = NewAbility("Bolt");
            WeaponData staff = NewWeapon("Test Staff", bolt);
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(sword, null);
            loadout.RebuildFromGear(staff, null);

            Assert.AreSame(bolt, loadout.GetSlot(0));
            Assert.IsNull(loadout.GetSlot(1),
                "Re-equipping rebuilds the bar; skills from the old gear are gone.");
        }

        [Test]
        public void Clear_EmptiesEverySlot()
        {
            AbilityData skill = NewAbility("Slash");
            WeaponData weapon = NewWeapon("Test Sword", skill);
            var loadout = new AbilityLoadout();
            loadout.RebuildFromGear(weapon, null);

            loadout.Clear();

            Assert.IsNull(loadout.GetSlot(0));
            Assert.AreEqual(AbilityLoadout.MaxSlots, loadout.Slots.Count,
                "Clearing empties the slots; it does not shrink the bar.");
        }

        [Test]
        public void ShippedGear_InjectsTheShippedSkills()
        {
            // The end-to-end promise of the issue, against the assets that actually ship:
            // a Broadsword grants Cleave, an Iron Plate grants Second Wind, and both land in
            // the same bar in gear order.
            WeaponData broadsword = LoadWeapon("Assets/Data/Weapons/Broadsword.asset");
            ArmorData ironPlate = LoadArmor("Assets/Data/Armor/Iron Plate.asset");
            var loadout = new AbilityLoadout();

            loadout.RebuildFromGear(broadsword, ironPlate);

            Assert.AreEqual("Cleave", loadout.GetSlot(0)?.AbilityName);
            Assert.AreEqual("Second Wind", loadout.GetSlot(1)?.AbilityName);
            Assert.IsNull(loadout.GetSlot(2));
        }

        private static int CountFilled(AbilityLoadout loadout)
        {
            int filled = 0;
            for (int slot = 0; slot < loadout.Slots.Count; slot++)
            {
                if (loadout.Slots[slot] != null)
                    filled++;
            }

            return filled;
        }

        private AbilityData NewAbility(string abilityName)
        {
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = abilityName;

            var inspector = new SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }

        private WeaponData NewWeapon(string weaponName, params AbilityData[] abilities)
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = weaponName;

            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_weaponName").stringValue = weaponName;
            SetAbilities(inspector, abilities);
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(weapon);
            return weapon;
        }

        private ArmorData NewArmor(string armorName, params AbilityData[] abilities)
        {
            ArmorData armor = ScriptableObject.CreateInstance<ArmorData>();
            armor.name = armorName;

            var inspector = new SerializedObject(armor);
            inspector.FindProperty("_armorName").stringValue = armorName;
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

        private static WeaponData LoadWeapon(string assetPath)
        {
            WeaponData asset = AssetDatabase.LoadAssetAtPath<WeaponData>(assetPath);
            Assert.IsNotNull(asset, $"Expected a WeaponData asset at {assetPath}.");
            return asset;
        }

        private static ArmorData LoadArmor(string assetPath)
        {
            ArmorData asset = AssetDatabase.LoadAssetAtPath<ArmorData>(assetPath);
            Assert.IsNotNull(asset, $"Expected an ArmorData asset at {assetPath}.");
            return asset;
        }
    }
}
