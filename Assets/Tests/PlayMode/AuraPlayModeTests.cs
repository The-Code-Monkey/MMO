using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The aura shell over a live host: DoT ticks and kill credit, stun action gates, root
    /// movement gating, and the death clear.
    /// </summary>
    public sealed class AuraPlayModeTests
    {
        private static NetworkObject _nodePrefab;

        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerStats _stats;
        private PlayerCombat _combat;
        private PlayerAbilities _abilities;
        private PlayerGathering _gathering;
        private PlayerMover _mover;
        private PlayerInventory _inventory;
        private AuraController _auras;
        private ResourceNode _node;
        private readonly List<Object> _scratch = new();

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

            if (_node != null)
                Object.Destroy(_node.gameObject);

            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.Destroy(owned);
            }

            _scratch.Clear();
            _node = null;
            _player = null;
            _stats = null;
            _combat = null;
            _abilities = null;
            _gathering = null;
            _mover = null;
            _inventory = null;
            _auras = null;

            yield return null;
        }

        [UnityTest]
        public IEnumerator PoisonKillsAnNpcAndCreditsTheApplier()
        {
            yield return StartHostWithPlayer();
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NPCStats npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();
            AuraController npcAuras = npc.GetComponent<AuraController>();
            Assert.IsNotNull(npcAuras, "The enemy prefab must carry an AuraController.");

            PlayerInventory credited = null;
            npc.Died += source => credited = source;

            AuraData poison = CreateAura(
                "TestPoison", AuraKind.Dot,
                durationSeconds: 5f, tickIntervalSeconds: 0.1f, magnitudePerTick: NPCStats.MaxHealth);

            npcAuras.Apply(poison, _inventory);
            yield return WaitUntil(() => !npc.IsAlive, 300);

            Assert.AreEqual(0, npc.Health);
            Assert.AreSame(_inventory, credited, "The poison kill must credit the player who applied it.");
            Assert.AreSame(_inventory, npc.LastAttacker);
            yield return WaitUntil(() => npcAuras.Visible.Count == 0, 60);
            Assert.IsEmpty(npcAuras.Visible, "A dead target clears its auras.");
        }

        [UnityTest]
        public IEnumerator StunBlocksAttackAndCast()
        {
            yield return StartHostWithPlayer();

            AbilityData heal = CreateSelfHeal("Test Stun Heal", manaCost: 10, power: 30);
            WeaponData weapon = CreateWeapon(heal, damage: 20, range: 10f);
            _abilities.EquipGear(weapon, null);
            _combat.Equip(weapon);

            _stats.TakeDamage(40);
            Assert.AreEqual(60, _stats.Health);

            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);
            NPCStats npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();
            npc.transform.position = _player.transform.position + _player.transform.forward * 2f;
            _combat.Sight = new FakeSight(clear: true);
            int npcHealth = npc.Health;

            AuraData stun = CreateAura("TestStun", AuraKind.Stun, durationSeconds: 5f, tickIntervalSeconds: 0f);
            _auras.Apply(stun);
            Assert.IsTrue(_auras.BlocksActions);

            _abilities.RequestCast(0, _player.transform.position);
            _combat.Attack(npc.NetworkObject);

            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.AreEqual(60, _stats.Health, "A stunned player must not heal from a cast.");
            Assert.AreEqual(PlayerStats.MaxMana, _stats.Mana, "A blocked cast must not spend mana.");
            Assert.AreEqual(npcHealth, npc.Health, "A stunned player must not land an attack.");
        }

        [UnityTest]
        public IEnumerator StunStopsGathering()
        {
            yield return StartHostWithPlayer();
            _node = SpawnNode(_player.transform.position);

            _gathering.StartGathering(_node.NetworkObject);
            Assert.AreSame(_node, _gathering.GatheringNode,
                "With no stun the node is a valid gathering session.");

            AuraData stun = CreateAura("TestGatherStun", AuraKind.Stun, durationSeconds: 5f, tickIntervalSeconds: 0f);
            _auras.Apply(stun);

            yield return null;
            Assert.IsNull(_gathering.GatheringNode, "A stun must stop an active gathering session.");
        }

        [UnityTest]
        public IEnumerator RootStopsMovementUntilItExpires()
        {
            yield return StartHostWithPlayer();
            float baseSpeed = _mover.MaxSpeed;

            AuraData root = CreateAura("TestRoot", AuraKind.Root, durationSeconds: 0.25f, tickIntervalSeconds: 0f);
            _auras.Apply(root);

            Assert.IsTrue(_auras.BlocksMovement);
            Assert.AreEqual(0f, _mover.MaxSpeed, "A root drives the aura speed multiplier to zero.");

            yield return WaitUntil(() => !_auras.BlocksMovement, 300);
            Assert.Greater(_mover.MaxSpeed, 0f);
            Assert.AreEqual(baseSpeed, _mover.MaxSpeed, 0.001f);
        }

        [UnityTest]
        public IEnumerator DeathClearsAuras()
        {
            yield return StartHostWithPlayer();

            AuraData stun = CreateAura("TestDeathStun", AuraKind.Stun, durationSeconds: 5f, tickIntervalSeconds: 0f);
            _auras.Apply(stun);
            Assert.IsTrue(_auras.BlocksActions);

            _stats.TakeDamage(PlayerStats.MaxHealth * 10);
            yield return WaitUntil(() => !_auras.BlocksActions, 60);

            Assert.IsFalse(_auras.BlocksActions, "Death must clear the stun.");
            Assert.IsEmpty(_auras.Visible);
        }

        private IEnumerator StartHostWithPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player);

            _stats = _player.GetComponent<PlayerStats>();
            _combat = _player.GetComponent<PlayerCombat>();
            _abilities = _player.GetComponent<PlayerAbilities>();
            _gathering = _player.GetComponent<PlayerGathering>();
            _mover = _player.GetComponent<PlayerMover>();
            _inventory = _player.GetComponent<PlayerInventory>();
            _auras = _player.GetComponent<AuraController>();

            Assert.IsNotNull(_stats);
            Assert.IsNotNull(_combat);
            Assert.IsNotNull(_abilities);
            Assert.IsNotNull(_gathering);
            Assert.IsNotNull(_mover);
            Assert.IsNotNull(_inventory);
            Assert.IsNotNull(_auras, "The player prefab must carry an AuraController.");

            yield return WaitUntil(() => _player.IsClientInitialized, 300);
        }

        private ResourceNode SpawnNode(Vector3 position)
        {
            if (_nodePrefab == null)
            {
                _nodePrefab = ResourceNodePrefabFactory.Create();
                _nodePrefab.gameObject.SetActive(false);
                ResourceNodePrefabFactory.Register(_manager.SpawnablePrefabs, _nodePrefab, initializeAdded: true);
            }

            GameObject instance = Object.Instantiate(_nodePrefab.gameObject, position, Quaternion.identity);
            instance.name = _nodePrefab.name;
            instance.SetActive(true);
            _manager.ServerManager.Spawn(instance);
            return instance.GetComponent<ResourceNode>();
        }

        private AuraData CreateAura(
            string auraId,
            AuraKind kind,
            float durationSeconds,
            float tickIntervalSeconds,
            int magnitudePerTick = 0,
            AuraStatKind stat = AuraStatKind.None,
            int flatStatBonus = 0)
        {
            var aura = ScriptableObject.CreateInstance<AuraData>();
            aura.name = auraId;

            var inspector = new SerializedObject(aura);
            inspector.FindProperty("_auraId").stringValue = auraId;
            inspector.FindProperty("_kind").enumValueIndex = (int)kind;
            inspector.FindProperty("_stat").enumValueIndex = (int)stat;
            inspector.FindProperty("_durationSeconds").floatValue = durationSeconds;
            inspector.FindProperty("_tickIntervalSeconds").floatValue = tickIntervalSeconds;
            inspector.FindProperty("_magnitudePerTick").intValue = magnitudePerTick;
            inspector.FindProperty("_flatStatBonus").intValue = flatStatBonus;
            inspector.FindProperty("_maxStacks").intValue = 1;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(aura);
            return aura;
        }

        private AbilityData CreateSelfHeal(string name, int manaCost, int power)
        {
            var ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = name;

            var inspector = new SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = name;
            inspector.FindProperty("_manaCost").intValue = manaCost;
            inspector.FindProperty("_cooldown").floatValue = 30f;
            inspector.FindProperty("_castTime").floatValue = 0f;
            inspector.FindProperty("_castRange").floatValue = 0f;
            inspector.FindProperty("_effectRadius").floatValue = 0f;
            inspector.FindProperty("_logic").enumValueIndex = (int)AbilityLogic.SelfHeal;
            inspector.FindProperty("_effectPower").intValue = power;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }

        private WeaponData CreateWeapon(AbilityData ability, int damage, float range)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = "Aura Test Weapon";

            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_weaponName").stringValue = weapon.name;
            inspector.FindProperty("_autoAttackDamage").intValue = damage;
            inspector.FindProperty("_attackRange").floatValue = range;
            SerializedProperty abilities = inspector.FindProperty("_abilities");
            abilities.arraySize = 1;
            abilities.GetArrayElementAtIndex(0).objectReferenceValue = ability;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(weapon);
            return weapon;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
