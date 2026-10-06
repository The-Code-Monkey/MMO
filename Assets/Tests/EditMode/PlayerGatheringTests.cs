using System.Collections.Generic;
using System.Reflection;
using FishNet.Connection;
using FishNet.Object;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The player's half of the gathering loop: the owner-checked intent that may only be sent
    /// by the local owner, the server-side session the tick timer reads, and the fact that a
    /// pure client carries none of it — no SyncVars, no session it can fill, no opinion of its
    /// own about how much ore is left in a vein.
    ///
    /// As with <see cref="PlayerCombatTests"/>, the timing arithmetic lives in static,
    /// NetworkBehaviour-free helpers and the attributes are asserted by reflection — FishNet
    /// injects the actual runtime guard at compile time, so nothing reads them back — while what
    /// a live host does after a click is proven by the PlayMode suite.
    /// </summary>
    public sealed class PlayerGatheringTests
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
        public void Gathering_DerivesFromNetworkBehaviourSoTheRpcExists()
        {
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(PlayerGathering).BaseType,
                "The intent pipeline is a ServerRpc, and only a NetworkBehaviour can own one.");
        }

        [Test]
        public void Gathering_OwnsNoSyncVarsAtAll()
        {
            // Everything a gathering session knows — the node and the next tick — is server-side
            // bookkeeping, never state a client is allowed to hold a copy of. A SyncVar here
            // would be a client with an opinion about its own harvest progress.
            foreach (FieldInfo field in typeof(PlayerGathering).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                Assert.IsFalse(
                    field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(FishNet.Object.Synchronizing.SyncVar<>),
                    $"{field.Name} is a SyncVar; a pure client must carry no gathering state.");
            }
        }

        [Test]
        public void GatheringNode_IsReadableByOthersButNotWritable()
        {
            PropertyInfo property = typeof(PlayerGathering).GetProperty(
                nameof(PlayerGathering.GatheringNode), BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, "UI and tests need to see which node the server is working.");
            Assert.IsNull(property.SetMethod,
                "The session is booked by the server's own decision, never assigned from outside.");
        }

        [Test]
        public void UnspawnedPlayer_ReportsIdleSession()
        {
            PlayerGathering gathering = AttachGathering();

            Assert.IsNull(gathering.GatheringNode,
                "A player with no server session reads as idle, not as an exception.");
        }
        #endregion

        #region The intent pipeline.
        [Test]
        public void RpcRequestGather_IsPrivateAndCarriesTheServerRpcAttribute()
        {
            MethodInfo method = typeof(PlayerGathering).GetMethod(
                "RpcRequestGather", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "The intent must travel through a ServerRpc, not a direct call.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerRpcAttribute>(),
                "RpcRequestGather lost its [ServerRpc] attribute, so a click would stop reaching the server.");
        }

        [Test]
        public void RpcRequestGather_RequiresOwnershipSoAClientCannotGatherForAnotherPlayer()
        {
            ServerRpcAttribute attribute = typeof(PlayerGathering)
                .GetMethod("RpcRequestGather", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetCustomAttribute<ServerRpcAttribute>();

            Assert.IsTrue(attribute.RequireOwnership,
                "An unowned or stolen intent would let one client spend another player's session.");
        }

        [Test]
        public void RpcRequestGather_TakesTheSenderConnectionSoTheServerCanReValidate()
        {
            MethodInfo method = typeof(PlayerGathering).GetMethod(
                "RpcRequestGather", BindingFlags.NonPublic | BindingFlags.Instance);

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(2, parameters.Length,
                "The target plus FishNet's sender connection.");
            Assert.AreEqual(typeof(NetworkObject), parameters[0].ParameterType,
                "The first parameter is the node the client asked for.");
            Assert.AreEqual(typeof(NetworkConnection), parameters[1].ParameterType,
                "The sender is what OwnerMatches re-validates on arrival.");
            Assert.IsTrue(parameters[1].HasDefaultValue,
                "FishNet fills the sender itself; a mandatory connection parameter would not compile.");
        }

        [Test]
        public void RequestGather_IsPublicSoTheNodeClickCanCallIt()
        {
            Assert.IsNotNull(typeof(PlayerGathering).GetMethod(
                    "RequestGather", BindingFlags.Public | BindingFlags.Instance),
                "ResourceNode routes its click here; the method has to stay callable and testable.");
        }

        [Test]
        public void RequestGather_WithNoTarget_DoesNothing()
        {
            PlayerGathering gathering = AttachGathering();

            Assert.DoesNotThrow(() => gathering.RequestGather(null),
                "A click on a node that despawned mid-frame must not take the client down.");
        }

        [Test]
        public void RequestGather_OnAnUninitializedPlayer_DoesNothing()
        {
            // NetworkObject is null until FishNet serializes this behaviour into a spawn, so an
            // intent has no owner to speak for yet — and saying nothing is the safe answer, even
            // with a real, target-shaped NetworkObject in hand.
            GameObject target = Track(new GameObject("TargetNode"));
            target.AddComponent<NetworkObject>();
            PlayerGathering gathering = AttachGathering();

            Assert.DoesNotThrow(() => gathering.RequestGather(target.GetComponent<NetworkObject>()));
        }
        #endregion

        #region The server's decision.
        [Test]
        public void StartAndStopGathering_ArePublicAndCarryTheServerAttribute()
        {
            foreach (string methodName in new[] { nameof(PlayerGathering.StartGathering), nameof(PlayerGathering.StopGathering) })
            {
                MethodInfo method = typeof(PlayerGathering).GetMethod(
                    methodName, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(method, $"{methodName} is the server's own entry point.");
                Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                    $"{methodName} lost its [Server] attribute, so a client could book its own session.");
            }
        }

        [Test]
        public void StartGathering_TakesTheTargetNode()
        {
            MethodInfo method = typeof(PlayerGathering).GetMethod(
                nameof(PlayerGathering.StartGathering), BindingFlags.Public | BindingFlags.Instance);

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(NetworkObject), parameters[0].ParameterType,
                "The intent carries the node's NetworkObject, resolved to a ResourceNode on arrival.");
        }

        [Test]
        public void StartGatheringOnAnUnspawnedTarget_LeavesTheSessionIdle()
        {
            // The server refuses anything that is not a live, spawned node — a target that never
            // spawned must not book a session that the tick loop would then dereference.
            GameObject node = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            NetworkObject target = node.AddComponent<NetworkObject>();
            PlayerGathering gathering = AttachGathering();

            Assert.DoesNotThrow(() => gathering.StartGathering(target));
            Assert.IsNull(gathering.GatheringNode);
        }

        [Test]
        public void StopGatheringOnAnIdlePlayer_IsIdempotent()
        {
            PlayerGathering gathering = AttachGathering();

            Assert.DoesNotThrow(() => gathering.StopGathering());
            Assert.IsNull(gathering.GatheringNode);
        }
        #endregion

        #region Finding the local player.
        [Test]
        public void FindLocal_WithNoManager_ReturnsNullRatherThanThrowing()
        {
            Assert.IsNull(PlayerGathering.FindLocal(null),
                "A click before the manager exists — or on a dedicated server — is a no-op.");
        }
        #endregion

        #region Server-only session state.
        [Test]
        public void SessionState_IsPrivateServerBookkeeping()
        {
            FieldInfo node = typeof(PlayerGathering).GetField("_node", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo nextTick = typeof(PlayerGathering).GetField("_nextTickAt", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(node, "The session needs the node it is working.");
            Assert.IsNotNull(nextTick, "The session needs the server time of its next tick.");
            Assert.AreEqual(typeof(ResourceNode), node.FieldType);
            Assert.AreEqual(typeof(double), nextTick.FieldType,
                "Ticks are booked on Time.timeAsDouble, the same clock as every other server timer.");
        }
        #endregion

        private PlayerGathering AttachGathering()
        {
            GameObject go = Track(new GameObject("PlayerGatheringUnderTest"));
            go.AddComponent<NetworkObject>();
            return go.AddComponent<PlayerGathering>();
        }

        private GameObject Track(GameObject go)
        {
            _sceneObjects.Add(go);
            return go;
        }
    }
}
