using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FishNet.Object;
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
    /// Proves the gathering loop, end to end, over a live FishNet connection: a real host spawns
    /// its player and an ore vein, and a click travels the real path — vein click entry point →
    /// <c>PlayerGathering.RequestGather</c> → <c>[ServerRpc]</c> → owner check → availability,
    /// reach and tool checks → one harvest per server tick into the player's replicated bag, until
    /// the vein depletes, drops out of the world and respawns.
    ///
    /// <para>
    /// As with <see cref="PlayerCombatPlayModeTests"/>, there is no attempt to catch a cheating
    /// client here (that needs two processes); the half of the guarantee that <em>is</em>
    /// checkable in-process — RequireOwnership, the <c>[Server]</c> attributes, the reach and tool
    /// gates — is asserted here and by reflection in the EditMode suites.
    /// </para>
    ///
    /// <para>
    /// The node prefab is registered once per test session (a static field) into the shared
    /// manager's shared spawnable collection, exactly like the target dummy does, and each test
    /// spawns short-interval, small-yield veins of its own — configured through a code-built
    /// <see cref="ResourceDefinition"/>, the same asset the shipped prefab carries — so the loop
    /// can be driven a full deplete-respawn cycle quickly.
    /// </para>
    /// </summary>
    public sealed class ResourceNodeGatheringPlayModeTests
    {
        private static NetworkObject _nodePrefab;

        private const float InRangeDistance = 2f;
        private const float OutOfRangeDistance = 50f;
        private const int TestYield = 3;
        private const float TestIntervalSeconds = 0.05f;

        /// <summary>The item the shipped vein awards — the shipped Resources item.</summary>
        private const string DropItemName = "Iron Ore";

        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerGathering _gathering;
        private ResourceNode _node;
        private readonly List<NetworkObject> _spawnedNodes = new();
        private readonly List<UnityEngine.Object> _scratchAssets = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            _player = null;
            _gathering = null;

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

            foreach (NetworkObject spawned in _spawnedNodes)
            {
                if (spawned != null)
                    Object.Destroy(spawned.gameObject);
            }

            _spawnedNodes.Clear();

            // The code-built definitions and tools are real ScriptableObjects; leaving them
            // behind would leak them into the next test and hide a leak from the next test.
            foreach (UnityEngine.Object scratch in _scratchAssets)
            {
                if (scratch != null)
                    Object.Destroy(scratch);
            }

            _scratchAssets.Clear();

            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingTheVeinInRange_HarvestsUntilItDepletes()
        {
            yield return StartHostWithConfiguredNode(InRangeDistance, TestYield, ToolKind.None, 0, 3f);

            // Exactly what OnMouseDown() invokes when a player presses the vein's collider.
            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _node.Remaining == 0, 600);

            Assert.AreEqual(0, _node.Remaining, "Three harvests at one per tick must empty the vein.");
            Assert.IsTrue(_node.Depleted, "An emptied vein has to be visibly depleted.");
            Assert.IsFalse(_node.IsAvailable, "An emptied vein cannot be clicked for more.");

            PlayerInventory inventory = _player.GetComponent<PlayerInventory>();
            Assert.AreEqual(TestYield, CountItemsNamed(inventory, DropItemName),
                "Each successful tick must award one Iron Ore into the player's replicated bag.");
        }

        [UnityTest]
        public IEnumerator ClickingTheVeinOutOfRange_IsRefusedByTheServer()
        {
            yield return StartHostWithConfiguredNode(OutOfRangeDistance, TestYield, ToolKind.None, 0, 3f);

            _node.RequestGatherFromLocalPlayer();

            // Give the RPC plenty of time to travel; the server must refuse it on reach alone.
            yield return WaitUntil(() => _gathering.GatheringNode != null, 60);

            Assert.IsNull(_gathering.GatheringNode, "A session outside the node's reach must never be booked.");
            Assert.AreEqual(TestYield, _node.Remaining, "A refused click must not spend the vein.");

            // Prove the refusal was about reach and not a dropped click: drag the vein into range
            // and the same click starts harvesting. A silently dead pipeline would stay empty-handed.
            _node.transform.position = _player.transform.position + Vector3.forward * InRangeDistance;
            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _node.Remaining < TestYield, 300);

            Assert.AreNotEqual(TestYield, _node.Remaining,
                "The same vein, once in reach, must start harvesting.");
        }

        [UnityTest]
        public IEnumerator ClickingAVeinThatDemandsATool_IsRefusedUntilTheToolIsHeld()
        {
            yield return StartHostWithConfiguredNode(InRangeDistance, TestYield, ToolKind.Pickaxe, 1, 3f);

            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _gathering.GatheringNode != null, 60);

            Assert.IsNull(_gathering.GatheringNode,
                "Clicking a vein the gatherer cannot work must not book a session.");
            Assert.AreEqual(TestYield, _node.Remaining, "A tool-less click must not spend the vein.");

            // Grant the required tool into the bag, and the identical click starts the loop — the
            // tool gate is what changed, nothing else.
            PlayerInventory inventory = _player.GetComponent<PlayerInventory>();
            ItemDefinition tool = CreateTool("PlayModePickaxe", ToolKind.Pickaxe, 1);
            Assert.IsTrue(inventory.TryAddItem(tool), "The bag must have room for the tool being granted.");

            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _node.Remaining < TestYield, 300);

            Assert.AreNotEqual(TestYield, _node.Remaining, "A gatherer holding the tool must be able to work the vein.");
        }

        [UnityTest]
        public IEnumerator ClickingAVeinThatDemandsAHigherTier_IsRefusedUntilTheBetterToolIsHeld()
        {
            yield return StartHostWithConfiguredNode(InRangeDistance, TestYield, ToolKind.Pickaxe, 2, 3f);

            PlayerInventory inventory = _player.GetComponent<PlayerInventory>();

            // A T1 pickaxe is a pickaxe, but it is not good enough for a T2 vein.
            ItemDefinition weakTool = CreateTool("PlayModeTierOnePickaxe", ToolKind.Pickaxe, 1);
            Assert.IsTrue(inventory.TryAddItem(weakTool), "The bag must have room for the tool being granted.");

            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _gathering.GatheringNode != null, 60);

            Assert.IsNull(_gathering.GatheringNode,
                "A T1 pickaxe must never book a session on a T2 vein.");
            Assert.AreEqual(TestYield, _node.Remaining, "A refused click must not spend the vein.");

            // The T2 pickaxe is the only thing that changes.
            ItemDefinition goodTool = CreateTool("PlayModeTierTwoPickaxe", ToolKind.Pickaxe, 2);
            Assert.IsTrue(inventory.TryAddItem(goodTool), "The bag must have room for the tool being granted.");

            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _node.Remaining < TestYield, 300);

            Assert.AreNotEqual(TestYield, _node.Remaining,
                "A T2 pickaxe opens a T2 vein; the tier gate is the only thing that changed.");
        }

        [UnityTest]
        public IEnumerator ADepletedVein_RespawnsEmptyThenFullAfterTheWindow()
        {
            yield return StartHostWithConfiguredNode(InRangeDistance, 1, ToolKind.None, 0, 0.2f);

            _node.RequestGatherFromLocalPlayer();

            yield return WaitUntil(() => _node.Depleted, 600);

            Assert.IsTrue(_node.Depleted, "A one-yield vein empties on its first harvest.");
            Assert.IsFalse(_node.GetComponent<Collider>().enabled,
                "An empty vein must drop its collider so a click never reaches the server.");
            Assert.IsFalse(_node.GetComponent<Renderer>().enabled,
                "An empty vein must be invisible to every client, not just the one that emptied it.");

            yield return WaitUntil(() => _node.IsAvailable, 600);

            Assert.IsTrue(_node.IsAvailable, "The server's respawn timer has to bring the vein back.");
            Assert.AreEqual(1, _node.Remaining, "A respawned vein has a full durability bar again.");
            Assert.IsTrue(_node.GetComponent<Collider>().enabled,
                "A respawned vein must be clickable again on every client.");
            Assert.IsTrue(_node.GetComponent<Renderer>().enabled,
                "A respawned vein must be visible again on every client.");
        }

        [UnityTest]
        public IEnumerator RegisteringTheNodeAtRuntime_AssignsItsPrefabIdImmediately()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return WaitUntil(() => _manager.IsServerStarted, 300);

            // The shared manager has already run Awake, so the runtime register path
            // (initializeAdded: true) has to hand the prefab its id here and now rather than wait
            // for a second Awake — otherwise the client could never resolve the spawn.
            NetworkObject prefab = EnsureNodePrefab();

            Assert.AreNotEqual(NetworkObject.UNSET_PREFABID_VALUE, prefab.PrefabId,
                "A prefab id is required before anything can be spawned from this prefab.");
        }

        private IEnumerator StartHostWithConfiguredNode(
            float distance, int dropAmount, ToolKind requiredTool, int gatheringTier, float respawnSeconds)
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player, "A connecting client must receive a player object.");

            _gathering = _player.GetComponent<PlayerGathering>();
            Assert.IsNotNull(_gathering, "PlayerPrefabFactory must put PlayerGathering on the player.");

            // A host's local client initialises a tick behind the spawn; until then IsOwner reads
            // false and RequestGather would drop the click as not-our-object.
            yield return WaitUntil(() => _player.IsClientInitialized, 300);
            Assert.IsTrue(_player.IsClientInitialized,
                "The host client never finished initialising its player object.");

            _node = SpawnConfiguredNode(
                _player.transform.position + Vector3.forward * distance,
                dropAmount,
                requiredTool,
                gatheringTier,
                respawnSeconds);

            // OnStartNetwork seeds durability on spawn; wait for the vein to read available.
            yield return WaitUntil(() => _node.IsAvailable, 300);
            Assert.IsTrue(_node.IsAvailable, "A spawned vein must start available to click.");
        }

        private ResourceNode SpawnConfiguredNode(
            Vector3 position, int dropAmount, ToolKind requiredTool, int gatheringTier, float respawnSeconds)
        {
            NetworkObject prefab = EnsureNodePrefab();

            NetworkObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
            instance.gameObject.SetActive(true);

            Configure(instance, dropAmount, requiredTool, gatheringTier, respawnSeconds);

            _spawnedNodes.Add(instance);
            _manager.ServerManager.Spawn(instance);
            return instance.GetComponent<ResourceNode>();
        }

        /// <summary>
        /// A test vein is a code-built <see cref="ResourceDefinition"/> — the same asset the
        /// shipped prefab carries, written through the SerializedObject path the Inspector uses.
        /// Configuration is written before spawn so OnStartNetwork seeds from it.
        /// </summary>
        private void Configure(
            NetworkObject instance, int dropAmount, ToolKind requiredTool, int gatheringTier, float respawnSeconds)
        {
            ItemDefinition drop = ItemDefinitionRegistry.Resolve(DropItemName);
            Assert.IsNotNull(drop, $"The shipped '{DropItemName}' item has to resolve for this test to mean anything.");

            ResourceDefinition definition = ScriptableObject.CreateInstance<ResourceDefinition>();
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_resourceType").intValue = (int)ResourceType.Ore;
            inspector.FindProperty("_drop").objectReferenceValue = drop;
            inspector.FindProperty("_dropAmount").intValue = dropAmount;
            inspector.FindProperty("_gatheringTier").intValue = gatheringTier;
            inspector.FindProperty("_requiredTool").intValue = (int)requiredTool;
            inspector.FindProperty("_resetTime").floatValue = respawnSeconds;
            inspector.FindProperty("_gatherIntervalSeconds").floatValue = TestIntervalSeconds;
            inspector.FindProperty("_interactRange").floatValue = InRangeDistance + 2f;
            inspector.ApplyModifiedPropertiesWithoutUndo();
            _scratchAssets.Add(definition);

            ResourceNode node = instance.GetComponent<ResourceNode>();
            var nodeInspector = new SerializedObject(node);
            nodeInspector.FindProperty("_definition").objectReferenceValue = definition;
            nodeInspector.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Builds a tool item — one an item definition can be, with a kind and a tier — and
        /// registers it so the server's tool gate can resolve it by name.
        /// </summary>
        private ItemDefinition CreateTool(string name, ToolKind kind, int tier)
        {
            ItemDefinition definition = ScriptableObject.CreateInstance<ItemDefinition>();
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_toolKind").intValue = (int)kind;
            inspector.FindProperty("_toolTier").intValue = tier;
            inspector.ApplyModifiedPropertiesWithoutUndo();
            _scratchAssets.Add(definition);

            ItemDefinitionRegistry.Register(definition);
            return definition;
        }

        private NetworkObject EnsureNodePrefab()
        {
            // The shared spawnable collection has no removal API, so the node is registered once
            // for the whole test session and every test reuses the same prefab.
            if (_nodePrefab != null)
                return _nodePrefab;

            NetworkObject prefab = ResourceNodePrefabFactory.Create();

            // Out of sight, like the player prefab; FishNet's pool activates the clone it spawns.
            prefab.gameObject.SetActive(false);

            Assert.IsTrue(ResourceNodePrefabFactory.Register(_manager.SpawnablePrefabs, prefab),
                "The node prefab has to be a spawnable for the server to spawn it.");

            _nodePrefab = prefab;
            return _nodePrefab;
        }

        private static int CountItemsNamed(PlayerInventory inventory, string itemName)
        {
            return inventory.Items.Count(instance => instance.ItemName == itemName);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
