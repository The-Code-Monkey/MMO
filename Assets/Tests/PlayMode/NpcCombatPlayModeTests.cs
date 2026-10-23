using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The end-to-end proof for the first RPG content prerequisite: a spawned <see cref="NPCStats"/>
    /// enemy is a real combat target, and the death carries the killer to whoever subscribes.
    /// </summary>
    public sealed class NpcCombatPlayModeTests
    {
        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerCombat _combat;
        private NPCStats _npc;
        private WeaponData _weapon;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            if (_weapon != null)
                Object.Destroy(_weapon);

            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerAttack_KillsAnNpcAndReportsTheKiller()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player, "A connecting client must receive a player object.");

            _combat = _player.GetComponent<PlayerCombat>();
            Assert.IsNotNull(_combat);

            yield return WaitUntil(() => _player.IsClientInitialized, 300);

            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);
            _npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();
            Assert.IsNotNull(_npc, "The server-spawned enemy must carry NPCStats.");

            _npc.transform.position = _player.transform.position + _player.transform.forward * 2f;
            _combat.Sight = new FakeSight(clear: true);

            _weapon = CreateWeapon(NPCStats.MaxHealth, range: 10f);
            _combat.Equip(_weapon);

            PlayerInventory killer = _player.GetComponent<PlayerInventory>();
            PlayerInventory credited = null;
            _npc.Died += source => credited = source;

            _combat.Attack(_npc.NetworkObject);

            yield return WaitUntil(() => !_npc.IsAlive, 300);

            Assert.AreEqual(0, _npc.Health);
            Assert.AreSame(killer, credited,
                "The death hook must carry the player whose damage landed the killing blow.");
            Assert.AreSame(killer, _npc.LastAttacker);
        }

        private static WeaponData CreateWeapon(int damage, float range)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = "NPC Combat Test Weapon";

            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_autoAttackDamage").intValue = damage;
            inspector.FindProperty("_attackRange").floatValue = range;
            inspector.ApplyModifiedPropertiesWithoutUndo();
            return weapon;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
