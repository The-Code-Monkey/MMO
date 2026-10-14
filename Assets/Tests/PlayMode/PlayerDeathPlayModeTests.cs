using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Auction;
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
    /// The risk tiers end to end over a live host connection: a real death crosses
    /// <c>PlayerStats.Died</c>, <see cref="PlayerDeath"/> reads the zone under the corpse, and the
    /// consequence lands — a safe-zone death wears the worn gear and bills the wallet, a black-zone
    /// death spills bag and gear into a networked <see cref="LootBag"/> and wipes the containers.
    /// Every death still resurrects, because a tier decides consequences, not resurrection.
    ///
    /// <para>
    /// Zones are whole-map (a <see cref="ZoneRiskConfig"/> with no collider) so the tier is
    /// deterministic regardless of where the prefab's spawn point happens to be.
    /// </para>
    /// </summary>
    public sealed class PlayerDeathPlayModeTests
    {
        private NetworkManager _manager;
        private PlayerStats _stats;
        private PlayerInventory _inventory;
        private PlayerWallet _wallet;
        private readonly List<GameObject> _scratchObjects = new();
        private readonly List<ItemDefinition> _definitions = new();

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
            foreach (GameObject go in _scratchObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _scratchObjects.Clear();

            foreach (LootBag bag in Object.FindObjectsByType<LootBag>(FindObjectsSortMode.None))
            {
                if (bag != null)
                    Object.DestroyImmediate(bag.gameObject);
            }

            foreach (ItemDefinition definition in _definitions)
            {
                if (definition != null)
                    Object.DestroyImmediate(definition);
            }

            _definitions.Clear();
            ItemDefinitionRegistry.Clear();

            NetworkManagerTestHelper.StopIfListening(_manager);

            // Stopping is asynchronous; let it finish so the next test starts genuinely offline.
            yield return null;
        }

        [UnityTest]
        public IEnumerator SafeZoneDeath_WearsWornGearAndChargesTheRepairBill()
        {
            CreateZone(ZoneRiskTier.Safe);
            yield return SpawnPlayer();

            ItemDefinition helm = CreateDefinition("Safe Helm", EquipmentSlot.Head, maxDurability: 20);
            Assert.IsTrue(_inventory.TryAddItem(helm));
            Assert.IsTrue(_inventory.TryEquip(0, 0));
            _wallet.SetGold(100);

            _stats.TakeDamage(PlayerStats.MaxHealth);

            yield return WaitUntil(() => _inventory.GetEquipped(EquipmentSlot.Head).Durability == 15, 300);

            ItemInstance worn = _inventory.GetEquipped(EquipmentSlot.Head);
            Assert.AreEqual("Safe Helm", worn.ItemName, "A safe-zone death drops nothing; the gear stays worn.");
            Assert.AreEqual(15, worn.Durability, "Safe-zone death wears each worn destructible item by the safe amount.");
            Assert.AreEqual(50, _wallet.Gold, "The repair bill is sunk from the wallet, not invented.");
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.Health, "The player stands back up after a safe-zone death.");
            Assert.IsNull(FindSingleLootBag(), "A safe-zone death leaves no loot on the ground.");
        }

        [UnityTest]
        public IEnumerator BlackZoneDeath_DropsGearIntoALootBagAndRespawnsThePlayer()
        {
            CreateZone(ZoneRiskTier.Black);
            yield return SpawnPlayer();

            ItemDefinition blade = CreateDefinition("Dropped Blade", EquipmentSlot.MainHand, maxDurability: 30);
            ItemDefinition charm = CreateDefinition("Dropped Charm");

            Assert.IsTrue(_inventory.TryAddItem(blade));
            Assert.IsTrue(_inventory.TryEquip(0, 0));
            Assert.IsTrue(_inventory.TryAddItem(charm));
            Assert.AreEqual(1, _inventory.Items.Count, "Only the charm is still in the bag once the blade is worn.");

            _stats.TakeDamage(PlayerStats.MaxHealth);

            yield return WaitUntil(
                () => _inventory.Items.Count == 0 && _inventory.GetEquipped(EquipmentSlot.MainHand).IsEmpty, 300);

            LootBag bag = FindSingleLootBag();
            Assert.IsNotNull(bag, "A black-zone death must leave a loot bag on the ground.");
            yield return WaitUntil(() => bag != null && bag.Contents.Count == 2, 300);

            var names = new List<string>();
            int droppedDurability = -1;
            foreach (ItemInstance item in bag.Contents)
            {
                names.Add(item.ItemName);
                if (item.ItemName == "Dropped Blade")
                    droppedDurability = item.Durability;
            }

            CollectionAssert.Contains(names, "Dropped Blade");
            CollectionAssert.Contains(names, "Dropped Charm");
            Assert.AreEqual(30, droppedDurability, "Dropped gear keeps the wear it had; the bag is not a repair.");

            Assert.AreEqual(0, _inventory.Items.Count, "A full-loot death empties the bag.");
            Assert.IsTrue(_inventory.GetEquipped(EquipmentSlot.MainHand).IsEmpty, "A full-loot death empties the worn slots.");
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.Health, "The player still respawns after a full-loot death.");
        }

        [UnityTest]
        public IEnumerator LootingTheBag_ReturnsTheDroppedGearAndDespawnsTheBag()
        {
            CreateZone(ZoneRiskTier.Black);
            yield return SpawnPlayer();

            ItemDefinition blade = CreateDefinition("Lootable Blade");
            Assert.IsTrue(_inventory.TryAddItem(blade));

            _stats.TakeDamage(PlayerStats.MaxHealth);
            yield return WaitUntil(() => _inventory.Items.Count == 0, 300);

            LootBag bag = FindSingleLootBag();
            Assert.IsNotNull(bag, "The drop must exist before it can be looted.");

            // The host owns its own player, so this travels the real path:
            // RequestLoot -> [ServerRpc] -> owner + range check -> Loot.
            _inventory.RequestLoot(bag);

            yield return WaitUntil(() => _inventory.Items.Count == 1, 300);

            Assert.AreEqual("Lootable Blade", _inventory.GetInventoryItem(0).ItemName,
                "The dropped item returns with its identity intact.");
            Assert.IsTrue(bag == null || !bag.IsSpawned, "An emptied bag despawns so it cannot be farmed twice.");
        }

        private IEnumerator SpawnPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            _inventory = player.GetComponent<PlayerInventory>();
            Assert.IsNotNull(_inventory, "The prefab's inventory container has to travel with it.");
            _stats = player.GetComponent<PlayerStats>();
            Assert.IsNotNull(_stats, "The prefab's stats have to travel with it.");
            _wallet = player.GetComponent<PlayerWallet>();
            Assert.IsNotNull(_wallet, "The prefab's wallet has to travel with it to pay the repair bill.");
            Assert.IsNotNull(player.GetComponent<PlayerDeath>(), "The prefab must carry the death resolver.");
        }

        private ZoneRiskConfig CreateZone(ZoneRiskTier tier)
        {
            var go = new GameObject($"Zone-{tier}");
            _scratchObjects.Add(go);

            var zone = go.AddComponent<ZoneRiskConfig>();
            zone.SetTier(tier);
            return zone;
        }

        private static LootBag FindSingleLootBag()
        {
            LootBag[] bags = Object.FindObjectsByType<LootBag>(FindObjectsSortMode.None);
            return bags.Length > 0 ? bags[0] : null;
        }

        private ItemDefinition CreateDefinition(
            string name,
            EquipmentSlot equipSlot = EquipmentSlot.MainHand,
            int maxDurability = 0)
        {
            // SerializedObject is the Inspector's own write path, so the fixture is proven
            // against Unity's serialization rather than a field poke.
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_equipSlot").intValue = (int)equipSlot;
            inspector.FindProperty("_gridWidth").intValue = 1;
            inspector.FindProperty("_gridHeight").intValue = 1;
            inspector.FindProperty("_maxDurability").intValue = maxDurability;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            ItemDefinitionRegistry.Register(definition);
            _definitions.Add(definition);
            return definition;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
