using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Content;
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
    /// The end-to-end loot paths: a kill awards the killer, a full bag falls back to a world bag,
    /// and a chest can only be opened once.
    /// </summary>
    public sealed class LootPlayModeTests
    {
        private static NetworkObject _chestPrefab;

        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerCombat _combat;
        private PlayerInventory _inventory;
        private WeaponData _weapon;
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

            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.Destroy(owned);
            }

            _scratch.Clear();
            _weapon = null;
            _player = null;
            _combat = null;
            _inventory = null;

            yield return null;
        }

        [UnityTest]
        public IEnumerator KillingAnNpc_AwardsItsTableToTheKiller()
        {
            yield return StartHostWithPlayer();
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NPCStats npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();
            Assert.IsNotNull(npc);

            NpcLoot loot = npc.GetComponent<NpcLoot>();
            Assert.IsNotNull(loot, "The shared enemy prefab must carry NpcLoot.");

            ItemDefinition drop = CreateItem("LootTestOre");
            loot.SetTable(CreateLootTable("LootTestKill", drop));

            yield return ArmAndKill(npc);

            Assert.AreEqual(1, _inventory.CountItem(drop.ItemName),
                "The killer's bag must receive the rolled drop.");
        }

        [UnityTest]
        public IEnumerator KillingAnNpc_WithAFullBag_SpawnsALootBagWithTheLeftovers()
        {
            yield return StartHostWithPlayer();
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NPCStats npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();
            NpcLoot loot = npc.GetComponent<NpcLoot>();
            Assert.IsNotNull(loot);

            ItemDefinition filler = CreateItem("LootTestFiller");
            while (_inventory.TryAddItem(filler))
            {
            }

            ItemDefinition drop = CreateItem("LootTestOverflow");
            loot.SetTable(CreateLootTable("LootTestOverflowTable", drop));

            yield return ArmAndKill(npc);
            yield return WaitUntil(() => FindSpawnedBag() != null, 300);

            LootBag bag = FindSpawnedBag();
            Assert.IsNotNull(bag, "A full bag must send the drop to a world loot bag.");
            Assert.AreEqual(1, bag.Contents.Count);
            Assert.AreEqual(drop.ItemName, bag.Contents[0].ItemName);
        }

        [UnityTest]
        public IEnumerator Chest_OpensOnceAndRefusesASecondRequest()
        {
            yield return StartHostWithPlayer();

            ItemDefinition drop = CreateItem("LootTestChestOre");
            WeightedLootTable table = CreateLootTable("LootTestChest", drop);
            LootChest chest = SpawnChest(table, _player.transform.position + _player.transform.forward * 2f);

            chest.RequestOpenFromLocalPlayer();
            yield return WaitUntil(() => chest.Opened, 300);

            Assert.IsTrue(chest.Opened);
            Assert.AreEqual(1, _inventory.CountItem(drop.ItemName));

            chest.RequestOpenFromLocalPlayer();
            yield return null;
            yield return null;

            Assert.IsTrue(chest.Opened);
            Assert.AreEqual(1, _inventory.CountItem(drop.ItemName),
                "A second request must not roll the table again.");
        }

        private IEnumerator StartHostWithPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player);

            _combat = _player.GetComponent<PlayerCombat>();
            _inventory = _player.GetComponent<PlayerInventory>();
            Assert.IsNotNull(_combat);
            Assert.IsNotNull(_inventory);

            yield return WaitUntil(() => _player.IsClientInitialized, 300);
        }

        private IEnumerator ArmAndKill(NPCStats npc)
        {
            npc.transform.position = _player.transform.position + _player.transform.forward * 2f;
            _combat.Sight = new FakeSight(clear: true);

            _weapon = CreateWeapon(NPCStats.MaxHealth, range: 10f);
            _combat.Equip(_weapon);

            _combat.Attack(npc.NetworkObject);
            yield return WaitUntil(() => !npc.IsAlive, 300);
            yield return null;
        }

        private LootChest SpawnChest(WeightedLootTable table, Vector3 position)
        {
            if (_chestPrefab == null)
            {
                GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prefab.name = "LootChestTest";
                _chestPrefab = prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<LootChest>();

                // The shared spawnable collection has no removal API, so the template lives for
                // the whole test session instead of being destroyed between tests.
                prefab.SetActive(false);
                _manager.SpawnablePrefabs.AddObject(_chestPrefab, checkForDuplicates: true, initializeAdded: true);
            }

            LootChest template = _chestPrefab.GetComponent<LootChest>();
            template.SetTable(table);
            template.SetInteractRange(10f);

            GameObject instance = Object.Instantiate(_chestPrefab.gameObject, position, Quaternion.identity);
            instance.name = _chestPrefab.name;
            instance.SetActive(true);
            _manager.ServerManager.Spawn(instance);
            return instance.GetComponent<LootChest>();
        }

        private LootBag FindSpawnedBag()
        {
            if (_manager == null || !_manager.IsServerStarted)
                return null;

            foreach (NetworkObject nob in _manager.ServerManager.Objects.Spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.TryGetComponent(out LootBag bag))
                    return bag;
            }

            return null;
        }

        private ItemDefinition CreateItem(string name)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.name = name;

            var inspector = new SerializedObject(item);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_gridWidth").intValue = 1;
            inspector.FindProperty("_gridHeight").intValue = 1;
            inspector.FindProperty("_maxDurability").intValue = 0;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            ItemDefinitionRegistry.Register(item);
            _scratch.Add(item);
            return item;
        }

        private WeightedLootTable CreateLootTable(string tableId, ItemDefinition item)
        {
            var table = ScriptableObject.CreateInstance<WeightedLootTable>();
            table.name = tableId;

            var inspector = new SerializedObject(table);
            inspector.FindProperty("_tableId").stringValue = tableId;
            SerializedProperty entries = inspector.FindProperty("_entries");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("Item").objectReferenceValue = item;
            entry.FindPropertyRelative("Chance").floatValue = 1f;
            entry.FindPropertyRelative("MinCount").intValue = 1;
            entry.FindPropertyRelative("MaxCount").intValue = 1;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            LootTableRegistry.Register(table);
            _scratch.Add(table);
            return table;
        }

        private WeaponData CreateWeapon(int damage, float range)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = "Loot Test Weapon";

            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_autoAttackDamage").intValue = damage;
            inspector.FindProperty("_attackRange").floatValue = range;
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
