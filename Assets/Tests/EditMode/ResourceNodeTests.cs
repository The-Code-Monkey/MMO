using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The networked vein: a <see cref="NetworkBehaviour"/> whose durability and depleted flag
    /// are server-owned <see cref="SyncVar{T}"/>s, whose configuration comes from the
    /// <see cref="ResourceDefinition"/> asset it carries, and whose depleted state repaints the
    /// collider and renderer on every client.
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
            AssertSerializedField("_definition", typeof(ResourceDefinition));
        }

        [Test]
        public void Configuration_ExposesNoPublicSetters()
        {
            foreach (string propertyName in new[]
            {
                nameof(ResourceNode.Type), nameof(ResourceNode.Drop), nameof(ResourceNode.DropAmount),
                nameof(ResourceNode.GatheringTier), nameof(ResourceNode.RequiredTool),
                nameof(ResourceNode.ResetTime), nameof(ResourceNode.GatherIntervalSeconds),
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
        public void Configuration_WithoutADefinition_FallsBackToTheDocumentedDefaults()
        {
            ResourceNode node = AttachNode();

            // A node with no definition is inert, not broken: it answers every question with the
            // documented default instead of throwing in the gather loop.
            Assert.IsNull(node.Definition);
            Assert.AreEqual(ResourceType.None, node.Type);
            Assert.IsNull(node.Drop);
            Assert.AreEqual(ResourceDefinition.DefaultDropAmount, node.DropAmount);
            Assert.AreEqual(0, node.GatheringTier);
            Assert.AreEqual(ToolKind.None, node.RequiredTool);
            Assert.AreEqual(ResourceDefinition.DefaultResetTime, node.ResetTime, 0.0001f);
            Assert.AreEqual(ResourceDefinition.DefaultGatherIntervalSeconds, node.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(ResourceDefinition.DefaultInteractRange, node.InteractRange, 0.0001f);
        }

        [Test]
        public void Configuration_ReadsThroughTheDefinition()
        {
            ResourceNode node = AttachNode();

            ResourceDefinition definition = TestResourceDefinitions.Create(
                TestResourceDefinitions.IronVein,
                dropAmount: 5,
                gatheringTier: 2,
                requiredTool: ToolKind.Pickaxe,
                resetTime: 45f,
                gatherIntervalSeconds: 2f,
                interactRange: 4f);

            node.SetDefinition(definition);

            Assert.AreSame(definition, node.Definition);
            Assert.AreEqual(ResourceType.Ore, node.Type);
            Assert.AreEqual(5, node.DropAmount);
            Assert.AreEqual(2, node.GatheringTier);
            Assert.AreEqual(ToolKind.Pickaxe, node.RequiredTool);
            Assert.AreEqual(45f, node.ResetTime, 0.0001f);
            Assert.AreEqual(2f, node.GatherIntervalSeconds, 0.0001f);
            Assert.AreEqual(4f, node.InteractRange, 0.0001f);
        }

        [Test]
        public void StandOffPosition_IsReachableAndClearOfTheBody()
        {
            ResourceNode node = AttachNode();
            node.transform.position = new Vector3(3f, 0.6f, -2f);
            node.transform.localScale = ResourceNodePrefabFactory.DefaultScale;

            float distance = Vector3.Distance(node.transform.position, node.StandOffPosition);

            Assert.Less(distance, node.InteractRange,
                "A click walks the player here; a spot outside reach is a gather the server refuses.");
            Assert.Greater(distance, ResourceNodePrefabFactory.DefaultDepth / 2f,
                "The stand-off must clear the body's half-depth, or the walk ends inside the vein.");
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
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;

            node.ApplyDepletedState(true);

            Assert.IsFalse(go.GetComponent<Collider>().enabled,
                "A click on an empty vein must never reach the server.");
            Assert.IsFalse(go.GetComponent<MeshRenderer>().enabled,
                "Every client has to see the emptied node the same way, from one replicated flag.");
            Assert.IsFalse(obstacle.enabled,
                "An emptied vein must stop carving too, or the player walks into an invisible wall.");
        }

        [Test]
        public void ApplyDepletedState_WhenAvailable_RestoresTheColliderAndRenderer()
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ResourceNode node = go.AddComponent<ResourceNode>();
            go.AddComponent<NavMeshObstacle>();
            node.ApplyDepletedState(true);

            node.ApplyDepletedState(false);

            Assert.IsTrue(go.GetComponent<Collider>().enabled);
            Assert.IsTrue(go.GetComponent<MeshRenderer>().enabled);
            Assert.IsTrue(go.GetComponent<NavMeshObstacle>().enabled,
                "The carve comes back with the body on respawn.");
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

        private ResourceNode AttachNode()        {
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
