using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The networked vein: a <see cref="NetworkBehaviour"/> whose durability and depleted flag
    /// are server-owned <see cref="SyncVar{T}"/>s, whose six configuration fields are serialized
    /// prefab data with no setters, and whose depleted state repaints the collider and renderer
    /// on every client.
    ///
    /// As with <see cref="TargetDummyTests"/>, the arithmetic sits in static, NetworkBehaviour-free
    /// helpers and the attributes are asserted by reflection — FishNet injects the actual runtime
    /// guard at compile time, so nothing reads them back — while what a live host does with a
    /// click is proven by the PlayMode suite.
    /// </summary>
    public sealed class ResourceNodeTests
    {
        private readonly List<GameObject> _sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _sceneObjects.Clear();
        }

        #region Networking shape.
        [Test]
        public void Node_DerivesFromNetworkBehaviourSoStateReplicates()
        {
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(ResourceNode).BaseType,
                "Only a NetworkBehaviour can own SyncTypes and ServerRpcs.");
        }

        [Test]
        public void DurabilityAndDepleted_AreTheOnlySyncVars()
        {
            // Durability and the depleted flag are the node's entire replicated state. A third
            // SyncVar would be state a client could hold that the server does not own.
            var syncVars = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(ResourceNode).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    syncVars.Add(field);
                }
            }

            Assert.AreEqual(2, syncVars.Count,
                "The node owns exactly two SyncVars: the remaining durability and the depleted flag.");
        }

        [Test]
        public void Remaining_DepletedAndIsAvailable_ExposeNoPublicSetters()
        {
            foreach (string propertyName in new[] { nameof(ResourceNode.Remaining), nameof(ResourceNode.Depleted), nameof(ResourceNode.IsAvailable) })
            {
                PropertyInfo property = typeof(ResourceNode).GetProperty(
                    propertyName, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"Clients need to read {propertyName}.");
                Assert.IsNull(property.SetMethod,
                    $"{propertyName} is server-owned; a public setter would let a client refill its own vein.");
            }
        }

        [Test]
        public void UnspawnedNode_ReadsEmptyRatherThanThrowing()
        {
            ResourceNode node = AttachNode();

            // No SyncType writes happen before the network starts, so durability reads zero and
            // the node reports itself unavailable — the safe answer for an un-initialised vein.
            Assert.AreEqual(0, node.Remaining);
            Assert.IsFalse(node.Depleted);
            Assert.IsFalse(node.IsAvailable);
        }
        #endregion

        #region Serialized configuration.
        [Test]
        public void Configuration_IsSerializedSoTheBakerCanWriteIt()
        {
            // The baker and the Inspector write through SerializedObject — the exact path the
            // project uses for every other asset-backed configuration — which only reaches a
            // private [SerializeField]. A public field would be a programmer-facing back door.
            AssertSerializedField("_itemId", typeof(string));
            AssertSerializedField("_requiredTool", typeof(string));
            AssertSerializedField("_maxYield", typeof(int));
            AssertSerializedField("_respawnSeconds", typeof(float));
            AssertSerializedField("_gatherIntervalSeconds", typeof(float));
            AssertSerializedField("_interactRange", typeof(float));
        }

        [Test]
        public void Configuration_ExposesNoPublicSetters()
        {
            foreach (string propertyName in new[]
            {
                nameof(ResourceNode.ItemId), nameof(ResourceNode.RequiredTool), nameof(ResourceNode.MaxYield),
                nameof(ResourceNode.RespawnSeconds), nameof(ResourceNode.GatherIntervalSeconds),
                nameof(ResourceNode.InteractRange),
            })
            {
                PropertyInfo property = typeof(ResourceNode).GetProperty(
                    propertyName, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"The server reads {propertyName} when it validates a gather.");
                Assert.IsNull(property.SetMethod,
                    $"{propertyName} is prefab configuration, not state; balance lives in the Inspector.");
            }
        }

        [Test]
        public void Configuration_DefaultsToTheIronOreVein()
        {
            ResourceNode node = AttachNode();

            Assert.AreEqual(ResourceNode.DefaultItemId, node.ItemId);
            Assert.AreEqual("Iron Ore", node.ItemId,
                "The shipped Resources item is what a default vein awards, so the loop works out of the box.");
            Assert.AreEqual(string.Empty, node.RequiredTool,
                "The default vein is bare-handed; a designer tightens it to e.g. 'Pickaxe'.");
            Assert.AreEqual(ResourceNode.DefaultMaxYield, node.MaxYield);
            Assert.AreEqual(ResourceNode.DefaultRespawnSeconds, node.RespawnSeconds, 0.0001f);
            Assert.AreEqual(ResourceNode.DefaultGatherIntervalSeconds, node.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(ResourceNode.DefaultInteractRange, node.InteractRange, 0.0001f);
        }

        [Test]
        public void MaxYield_FloorsAtAOneHarvestVein()
        {
            ResourceNode node = AttachNode();
            WriteField(node, "_maxYield", 0);

            Assert.AreEqual(1, node.MaxYield,
                "A zero-yield vein could never be depleted, because there is nothing to take.");
        }

        [Test]
        public void RespawnAndIntervalAndReach_FloorAtZero()
        {
            ResourceNode node = AttachNode();
            WriteField(node, "_respawnSeconds", -5f);
            WriteField(node, "_gatherIntervalSeconds", -1f);
            WriteField(node, "_interactRange", -2f);

            Assert.AreEqual(0f, node.RespawnSeconds);
            Assert.AreEqual(0f, node.GatherIntervalSeconds);
            Assert.AreEqual(0f, node.InteractRange);
        }

        [Test]
        public void RequiredTool_ReadsNullAsEmpty()
        {
            ResourceNode node = AttachNode();
            WriteField(node, "_requiredTool", null);

            Assert.AreEqual(string.Empty, node.RequiredTool,
                "A null requirement must not reach string.IsNullOrEmpty callers as an exception.");
        }
        #endregion

        #region Server authority.
        [Test]
        public void TryGather_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(ResourceNode).GetMethod(
                "TryGather", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(PlayerInventory) }, null);

            Assert.IsNotNull(method, "TryGather(PlayerInventory) must be public so the player's tick can reach it.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "TryGather lost its [Server] attribute, so any client could spend its own vein.");
        }

        [Test]
        public void TryGather_ReturnsBoolSoTheTickKnowsWhetherToContinue()
        {
            MethodInfo method = typeof(ResourceNode).GetMethod(
                "TryGather", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(PlayerInventory) }, null);

            Assert.AreEqual(typeof(bool), method.ReturnType,
                "A false must tell PlayerGathering to stop the loop — a void return cannot say 'refused'.");
        }
        #endregion

        #region The depleted state on one client.
        [Test]
        public void ApplyDepletedState_WhenDepleted_DropsTheColliderAndRenderer()
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ResourceNode node = go.AddComponent<ResourceNode>();

            node.ApplyDepletedState(true);

            Assert.IsFalse(go.GetComponent<Collider>().enabled,
                "A click on an empty vein must never reach the server.");
            Assert.IsFalse(go.GetComponent<MeshRenderer>().enabled,
                "Every client has to see the emptied node the same way, from one replicated flag.");
        }

        [Test]
        public void ApplyDepletedState_WhenAvailable_RestoresTheColliderAndRenderer()
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ResourceNode node = go.AddComponent<ResourceNode>();
            node.ApplyDepletedState(true);

            node.ApplyDepletedState(false);

            Assert.IsTrue(go.GetComponent<Collider>().enabled);
            Assert.IsTrue(go.GetComponent<MeshRenderer>().enabled);
        }

        [Test]
        public void ApplyDepletedState_IsIdempotent()
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ResourceNode node = go.AddComponent<ResourceNode>();

            node.ApplyDepletedState(true);
            node.ApplyDepletedState(true);

            Assert.IsFalse(go.GetComponent<Collider>().enabled);
        }

        [Test]
        public void ApplyDepletedState_WithNoColliderOrRenderer_DoesNothing()
        {
            // A node built without a visual — or one mid-destruction — must not take the
            // state callback down with a missing-component exception.
            GameObject go = Track(new GameObject("BareResourceNode"));
            ResourceNode node = go.AddComponent<ResourceNode>();

            Assert.DoesNotThrow(() => node.ApplyDepletedState(true));
        }
        #endregion

        #region The click entry point.
        [Test]
        public void RequestGatherFromLocalPlayer_IsPublicSoTheClickCanCallIt()
        {
            Assert.IsNotNull(typeof(ResourceNode).GetMethod(
                    "RequestGatherFromLocalPlayer", BindingFlags.Public | BindingFlags.Instance),
                "The click handler routes to this method; it has to stay callable and testable.");
        }

        [Test]
        public void RequestGatherFromLocalPlayer_OnAnUninitializedNode_DoesNothing()
        {
            // NetworkObject is null until FishNet initialises this behaviour; without a manager
            // there is no local player to gather from, so a click on an un-spawned node is a no-op.
            ResourceNode node = AttachNode();

            Assert.DoesNotThrow(() => node.RequestGatherFromLocalPlayer());
        }
        #endregion

        private static void AssertSerializedField(string fieldName, System.Type fieldType)
        {
            FieldInfo field = typeof(ResourceNode).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"Expected a private {fieldName} on the node.");
            Assert.AreEqual(fieldType, field.FieldType, $"{fieldName} changed type.");
            Assert.IsNotNull(field.GetCustomAttribute<SerializeField>(),
                $"{fieldName} must be a [SerializeField] so the baker and the Inspector can write it.");
        }

        private static void WriteField(ResourceNode node, string fieldName, object value)
        {
            FieldInfo field = typeof(ResourceNode).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, $"Expected a private {fieldName} on the node.");
            field.SetValue(node, value);
        }

        private ResourceNode AttachNode()
        {
            GameObject go = Track(new GameObject("ResourceNodeUnderTest"));
            go.AddComponent<NetworkObject>();
            return go.AddComponent<ResourceNode>();
        }

        private GameObject Track(GameObject go)
        {
            _sceneObjects.Add(go);
            return go;
        }
    }
}
