using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Proves the inventory and equipment containers work over a live FishNet connection:
    /// the server spawns the player's two SyncLists, a real right-click travels
    /// <c>view -> [ServerRpc] -> owner check -> [Server] mutation</c>, and every gate the issue
    /// lists — inventory space, designated slot, stat (level) requirements, recalculation — is
    /// answered on the server before anything replicates back.
    ///
    /// <para>
    /// Host-mode limits, same as the vitals suite: a cheating *client* cannot be simulated in
    /// process (one NetworkManager per process), so the client half of the guarantee — that the
    /// lists are server-write-only and the RPCs owner-check their sender — is asserted by
    /// FishNet's codegen and by the reflection tests in EditMode.
    /// </para>
    /// </summary>
    public sealed class PlayerInventoryPlayModeTests
    {
        private NetworkManager _manager;
        private PlayerInventory _inventory;
        private PlayerStats _stats;
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
        public IEnumerator SpawnedPlayer_StartsWithAnEmptyBagAndFiveVacantSlots()
        {
            yield return SpawnPlayer();
            yield return WaitUntil(() => _inventory.Equipment.Count == EquipmentSlots.Count, 300);

            Assert.AreEqual(0, _inventory.Items.Count, "A fresh player carries nothing.");
            for (int i = 0; i < EquipmentSlots.Count; i++)
            {
                Assert.IsTrue(_inventory.GetEquipped((EquipmentSlot)i).IsEmpty,
                    $"Slot {(EquipmentSlot)i} must start vacant on the server's copy.");
            }

            Assert.AreEqual(PlayerStats.DefaultLevel, _stats.Level,
                "The server seeds the level on spawn; zero would fail every level-1 item check.");
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.EffectiveMaxHealth);
            Assert.AreEqual(0, _stats.MaxHealthBonus, "No gear means no bonus.");
        }

        [UnityTest]
        public IEnumerator ServerAddItem_WritesTheInstanceIntoTheBag()
        {
            yield return SpawnPlayer();

            bool added = _inventory.TryAddItem(ResolveShipped("Iron Helm"));

            Assert.IsTrue(added, "An empty bag has room for one 1x1 item.");
            Assert.AreEqual(1, _inventory.Items.Count);

            ItemInstance placed = _inventory.GetInventoryItem(0);
            Assert.AreEqual("Iron Helm", placed.ItemName, "The instance carries the definition key.");
            Assert.IsFalse(_inventory.GetItemAt(placed.GridX, placed.GridY).IsEmpty,
                "The instance must be reachable through the cell it claims.");
        }

        [UnityTest]
        public IEnumerator FullBag_RefusesTheNextItem()
        {
            yield return SpawnPlayer();
            ItemDefinition filler = CreateDefinition("Ballast");

            for (int i = 0; i < PlayerInventory.GridWidth * PlayerInventory.GridHeight; i++)
                Assert.IsTrue(_inventory.TryAddItem(filler), $"Cell {i} should accept the filler.");

            Assert.IsFalse(_inventory.TryAddItem(filler),
                "Item forty-nine must be refused once every one of the forty-eight cells is full.");
            Assert.AreEqual(48, _inventory.Items.Count, "A refusal adds nothing.");
        }

        [UnityTest]
        public IEnumerator RightClickEquips_ThroughTheRealRpcPath()
        {
            yield return SpawnPlayer();
            Assert.IsTrue(_inventory.TryAddItem(ResolveShipped("Iron Helm")));

            ItemInstance placed = _inventory.GetInventoryItem(0);
            InventorySlotView view = CreateInventorySlotView(placed.GridX, placed.GridY);

            // The issue's gesture end to end: uGUI right-click -> view -> RequestEquip ->
            // [ServerRpc] -> owner check -> [Server] TryEquip -> SyncList replication back.
            view.OnPointerClick(Click(PointerEventData.InputButton.Right));

            yield return WaitUntil(() => !_inventory.GetEquipped(EquipmentSlot.Head).IsEmpty, 300);

            Assert.AreEqual("Iron Helm", _inventory.GetEquipped(EquipmentSlot.Head).ItemName,
                "The server moved the item into the slot its definition designates.");
            Assert.AreEqual(0, _inventory.Items.Count, "The item left the bag when it became worn.");
            Assert.AreEqual(10, _stats.MaxHealthBonus,
                "The server recalculated the bonus from what is actually worn.");
            Assert.AreEqual(PlayerStats.MaxHealth + 10, _stats.EffectiveMaxHealth);
        }

        [UnityTest]
        public IEnumerator LeftClick_ChangesNothing()
        {
            yield return SpawnPlayer();
            Assert.IsTrue(_inventory.TryAddItem(ResolveShipped("Iron Helm")));

            InventorySlotView view = CreateInventorySlotView(0, 0);
            view.OnPointerClick(Click(PointerEventData.InputButton.Left));

            // Give a wrongly-sent RPC every chance to arrive before judging.
            for (int i = 0; i < 10; i++)
                yield return null;

            Assert.AreEqual(1, _inventory.Items.Count, "Only the right button equips.");
            Assert.IsTrue(_inventory.GetEquipped(EquipmentSlot.Head).IsEmpty);
        }

        [UnityTest]
        public IEnumerator LevelGate_RefusesThenAcceptsAsThePlayerGrows()
        {
            yield return SpawnPlayer();
            ItemDefinition staff = CreateDefinition("Vault Staff", requiredLevel: 99);
            Assert.IsTrue(_inventory.TryAddItem(staff));
            ItemInstance placed = _inventory.GetInventoryItem(0);

            Assert.IsFalse(_inventory.TryEquip(placed.GridX, placed.GridY),
                "A level 1 player cannot equip an item that requires level 99.");
            Assert.AreEqual(1, _inventory.Items.Count, "A refused equip moves nothing.");
            Assert.IsTrue(_inventory.GetEquipped(EquipmentSlot.MainHand).IsEmpty);

            _stats.SetLevel(99);

            Assert.IsTrue(_inventory.TryEquip(placed.GridX, placed.GridY),
                "At the required level the same request now succeeds.");
            Assert.AreEqual("Vault Staff", _inventory.GetEquipped(EquipmentSlot.MainHand).ItemName);
            Assert.AreEqual(0, _inventory.Items.Count);
        }

        [UnityTest]
        public IEnumerator SlotSwap_DisplacesTheWornItemBackIntoTheBag()
        {
            yield return SpawnPlayer();
            ItemDefinition alpha = CreateDefinition("Helm Alpha", equipSlot: EquipmentSlot.Head);
            ItemDefinition beta = CreateDefinition("Helm Beta", equipSlot: EquipmentSlot.Head);
            Assert.IsTrue(_inventory.TryAddItem(alpha));
            Assert.IsTrue(_inventory.TryAddItem(beta));
            Assert.IsTrue(_inventory.TryEquip(0, 0));
            Assert.AreEqual("Helm Alpha", _inventory.GetEquipped(EquipmentSlot.Head).ItemName);

            // Equipping into an occupied slot: Beta takes the slot, Alpha goes home to the bag.
            Assert.IsTrue(_inventory.TryEquip(1, 0));

            Assert.AreEqual("Helm Beta", _inventory.GetEquipped(EquipmentSlot.Head).ItemName);
            Assert.AreEqual(1, _inventory.Items.Count,
                "One item in, one item out — a swap must not duplicate or delete anything.");
            Assert.AreEqual("Helm Alpha", _inventory.GetInventoryItem(0).ItemName,
                "The displaced item returns to the bag instead of vanishing.");
            Assert.AreEqual("Helm Alpha", _inventory.GetItemAt(0, 0).ItemName,
                "Freed cells are refilled from the origin, so Alpha lands where Beta stood.");
        }

        [UnityTest]
        public IEnumerator SlotSwap_IsRefusedWhenTheDisplacedItemWouldNotFit()
        {
            yield return SpawnPlayer();
            ItemDefinition greatHelm = CreateDefinition("Great Helm", height: 2, equipSlot: EquipmentSlot.Head);
            ItemDefinition smallHelm = CreateDefinition("Small Helm", equipSlot: EquipmentSlot.Head);
            ItemDefinition filler = CreateDefinition("Ballast");

            Assert.IsTrue(_inventory.TryAddItem(greatHelm));
            Assert.IsTrue(_inventory.TryEquip(0, 0));
            Assert.IsTrue(_inventory.TryAddItem(smallHelm));
            for (int i = 0; i < 47; i++)
                Assert.IsTrue(_inventory.TryAddItem(filler), $"Filler {i} should fit.");

            Assert.AreEqual(48, _inventory.Items.Count, "The bag must be completely full for this gate.");
            ItemInstance small = _inventory.GetItemAt(0, 0);
            Assert.AreEqual("Small Helm", small.ItemName);

            // The only free cell after removing Small Helm is one cell — Great Helm needs two.
            Assert.IsFalse(_inventory.TryEquip(small.GridX, small.GridY),
                "The swap must be refused when the displaced item has nowhere to go.");
            Assert.AreEqual("Great Helm", _inventory.GetEquipped(EquipmentSlot.Head).ItemName,
                "A refusal leaves the slot untouched.");
            Assert.AreEqual(48, _inventory.Items.Count, "A refusal leaves the bag untouched.");
            Assert.AreEqual("Small Helm", _inventory.GetItemAt(small.GridX, small.GridY).ItemName);
        }

        [UnityTest]
        public IEnumerator RightClickUnequip_RestoresTheBagAndClampsVitalsToTheNewCap()
        {
            yield return SpawnPlayer();
            Assert.IsTrue(_inventory.TryAddItem(ResolveShipped("Iron Helm")));
            Assert.IsTrue(_inventory.TryEquip(0, 0));
            Assert.AreEqual(PlayerStats.MaxHealth + 10, _stats.EffectiveMaxHealth);

            _stats.Heal(10);
            Assert.AreEqual(PlayerStats.MaxHealth + 10, _stats.Health,
                "The raised cap holds the extra health.");

            EquipmentSlotView view = CreateEquipmentSlotView(EquipmentSlot.Head);
            view.OnPointerClick(Click(PointerEventData.InputButton.Right));

            yield return WaitUntil(() => _inventory.GetEquipped(EquipmentSlot.Head).IsEmpty, 300);

            Assert.AreEqual(1, _inventory.Items.Count, "The helm came home to the bag.");
            Assert.AreEqual("Iron Helm", _inventory.GetInventoryItem(0).ItemName);
            Assert.AreEqual(0, _stats.MaxHealthBonus, "Removing the only bonus item zeroes the bonus.");
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.EffectiveMaxHealth);
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.Health,
                "Health floating above the new cap is clamped down, not left invalid.");
        }

        [UnityTest]
        public IEnumerator EquipmentBonuses_RecalculateFromEverythingWorn()
        {
            yield return SpawnPlayer();
            Assert.IsTrue(_inventory.TryAddItem(ResolveShipped("Iron Helm")));
            ItemDefinition band = CreateDefinition("Mana Band", equipSlot: EquipmentSlot.OffHand, maxManaBonus: 20);
            Assert.IsTrue(_inventory.TryAddItem(band));

            Assert.IsTrue(_inventory.TryEquip(0, 0));
            Assert.IsTrue(_inventory.TryEquip(1, 0));
            Assert.AreEqual(10, _stats.MaxHealthBonus);
            Assert.AreEqual(20, _stats.MaxManaBonus);
            Assert.AreEqual(PlayerStats.MaxMana + 20, _stats.EffectiveMaxMana);

            Assert.IsTrue(_inventory.TryUnequip(EquipmentSlot.Head));

            Assert.AreEqual(0, _stats.MaxHealthBonus,
                "Recalculation rebuilds from what is worn; it never accumulates stale bonuses.");
            Assert.AreEqual(20, _stats.MaxManaBonus, "The band's bonus survives its neighbour's departure.");
            Assert.AreEqual(PlayerStats.MaxHealth, _stats.EffectiveMaxHealth);
            Assert.AreEqual(PlayerStats.MaxMana + 20, _stats.EffectiveMaxMana);
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
        }

        private InventorySlotView CreateInventorySlotView(int gridX, int gridY)
        {
            var go = new GameObject("InventorySlotViewUnderTest");
            _scratchObjects.Add(go);

            InventorySlotView view = go.AddComponent<InventorySlotView>();
            view.SetInventory(_inventory);
            view.SetCell(gridX, gridY);
            return view;
        }

        private EquipmentSlotView CreateEquipmentSlotView(EquipmentSlot slot)
        {
            var go = new GameObject("EquipmentSlotViewUnderTest");
            _scratchObjects.Add(go);

            EquipmentSlotView view = go.AddComponent<EquipmentSlotView>();
            view.SetInventory(_inventory);
            view.SetSlot(slot);
            return view;
        }

        private ItemDefinition CreateDefinition(
            string name,
            int width = 1,
            int height = 1,
            EquipmentSlot equipSlot = EquipmentSlot.MainHand,
            int requiredLevel = 0,
            int maxHealthBonus = 0,
            int maxManaBonus = 0)
        {
            // SerializedObject is the Inspector's own write path, so the fixture is proven
            // against Unity's serialization rather than a field poke.
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_equipSlot").intValue = (int)equipSlot;
            inspector.FindProperty("_requiredLevel").intValue = requiredLevel;
            inspector.FindProperty("_gridWidth").intValue = width;
            inspector.FindProperty("_gridHeight").intValue = height;
            inspector.FindProperty("_maxHealthBonus").intValue = maxHealthBonus;
            inspector.FindProperty("_maxManaBonus").intValue = maxManaBonus;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            ItemDefinitionRegistry.Register(definition);
            _definitions.Add(definition);
            return definition;
        }

        private static ItemDefinition ResolveShipped(string itemName)
        {
            ItemDefinition definition = ItemDefinitionRegistry.Resolve(itemName);
            Assert.IsNotNull(definition,
                $"Shipped item '{itemName}' must resolve through the registry or the Resources fallback.");
            return definition;
        }

        private static PointerEventData Click(PointerEventData.InputButton button)
        {
            return new PointerEventData(null) { button = button };
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
