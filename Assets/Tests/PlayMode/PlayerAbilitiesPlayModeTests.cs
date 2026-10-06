using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The equipment-injected framework over a live FishNet connection: gear fills the player's
    /// slots, the owner sends a cast intent, and the server is the only side that decides
    /// anything — validating the request, spending the mana, refusing the follow-up cast on the
    /// cooldown it just started, and applying the effect.
    ///
    /// <para>
    /// The skill under test is a self-heal on purpose. Damaging another player would need a
    /// second networked client (a host cannot be made to misbehave against itself in-process),
    /// while a heal exercises the identical path — RPC, ownership check, validation, mana,
    /// cooldown booking, effect resolution — against vitals this test can already reach.
    /// </para>
    /// </summary>
    public sealed class PlayerAbilitiesPlayModeTests
    {
        private NetworkManager _manager;
        private readonly List<Object> _scratch = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // NetworkManager.Awake runs on the frame the object activates and the join gate
            // subscribes in Start, so give the manager a frame before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            // Stopping is asynchronous; let it finish so the next test starts genuinely offline.
            yield return null;

            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _scratch.Clear();
        }

        [UnityTest]
        public IEnumerator EquipAndCast_IsValidatedSpentAndAppliedByTheServer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            PlayerStats stats = player.GetComponent<PlayerStats>();
            PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
            Assert.IsNotNull(stats, "The prefab's networked stats have to travel with it.");
            Assert.IsNotNull(abilities,
                "The equipment-injected ability slots belong on the player object the server spawns.");

            // Gear: a weapon granting one self-heal with a real cost and a long cooldown.
            AbilityData heal = NewAbility(
                "Test Heal", manaCost: 10, cooldown: 30f, castTime: 0f, logic: AbilityLogic.SelfHeal,
                effectPower: 30);
            WeaponData weapon = NewWeapon(heal);

            // Server-side equip: the host is the server, so this is the real entry point the
            // inventory system will call, not a shortcut around validation.
            abilities.EquipGear(weapon, null);
            Assert.AreEqual("Test Heal", abilities.GetSlotName(0),
                "Equipping gear must publish the populated bar to the owner.");

            // Something to heal back.
            stats.RequestDamage(40);
            yield return WaitUntil(() => stats.Health == 60, 300);
            Assert.AreEqual(60, stats.Health);

            // Intent out, server validates, spends mana, resolves the effect.
            abilities.RequestCast(0, player.transform.position);
            yield return WaitUntil(() => stats.Health == 90, 300);

            Assert.AreEqual(90, stats.Health,
                "The server must apply the heal; the client only asked for it.");
            Assert.AreEqual(PlayerStats.MaxMana - 10, stats.Mana,
                "The cast must cost exactly what the ability asset says.");

            // The slot cooldown and the Global Cooldown the first cast started refuse the
            // second one, so nothing is spent or applied twice.
            abilities.RequestCast(0, player.transform.position);
            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.AreEqual(90, stats.Health,
                "A second cast must be refused while the ability is still on cooldown.");
            Assert.AreEqual(PlayerStats.MaxMana - 10, stats.Mana,
                "A refused cast must not charge the caster again.");
            Assert.IsFalse(abilities.Cooldowns.IsReady(0, Time.timeAsDouble),
                "The server's tracked cooldown is what refused it.");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }

        private AbilityData NewAbility(
            string abilityName,
            int manaCost,
            float cooldown,
            float castTime,
            AbilityLogic logic,
            int effectPower)
        {
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = abilityName;

            var inspector = new SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.FindProperty("_manaCost").intValue = manaCost;
            inspector.FindProperty("_cooldown").floatValue = cooldown;
            inspector.FindProperty("_castTime").floatValue = castTime;
            inspector.FindProperty("_logic").enumValueIndex = (int)logic;
            inspector.FindProperty("_effectPower").intValue = effectPower;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }

        private WeaponData NewWeapon(params AbilityData[] abilities)
        {
            WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();

            var inspector = new SerializedObject(weapon);
            SerializedProperty property = inspector.FindProperty("_abilities");
            property.arraySize = abilities.Length;
            for (int i = 0; i < abilities.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = abilities[i];
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(weapon);
            return weapon;
        }
    }
}
