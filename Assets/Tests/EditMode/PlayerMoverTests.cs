using System.Reflection;
using FishNet.Connection;
using FishNet.Object;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The wiring of the server-authoritative mover: the input path may only be started by the
    /// owner, the position-bearing and integration methods are server-only, and the speed is a
    /// serialized setting. As with <see cref="PlayerCombatTests"/>, the attributes are asserted by
    /// reflection because FishNet injects the runtime guard at compile time; what a live host does
    /// with an impossible delta is covered in <see cref="PlayerMoverPlayModeTests"/>.
    /// </summary>
    public sealed class PlayerMoverTests
    {
        [Test]
        public void MaxSpeed_IsASerializedField()
        {
            FieldInfo field = typeof(PlayerMover).GetField("_maxSpeed", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "The move speed must be a serialized field the prefab can bake.");
            Assert.AreEqual(typeof(float), field.FieldType);
            Assert.IsNotNull(field.GetCustomAttribute<SerializeField>());
        }

        [Test]
        public void MaxSpeed_NegativeConfiguredValue_ReadsAsZero()
        {
            var go = new GameObject("PlayerMoverUnderTest");
            try
            {
                go.AddComponent<NetworkObject>();
                PlayerMover mover = go.AddComponent<PlayerMover>();

                var inspector = new SerializedObject(mover);
                inspector.FindProperty("_maxSpeed").floatValue = -5f;
                inspector.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(0f, mover.MaxSpeed, "Speed is a magnitude; a negative setting must not invert movement.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LastAuthoritativePosition_BeforeAnyServerTick_ReadsTheLiveTransform()
        {
            var go = new GameObject("PlayerMoverUnderTest");
            try
            {
                go.transform.position = new Vector3(3f, 1f, 2f);
                go.AddComponent<NetworkObject>();
                PlayerMover mover = go.AddComponent<PlayerMover>();

                Assert.AreEqual(new Vector3(3f, 1f, 2f), mover.LastAuthoritativePosition,
                    "A just-spawned mover needs a sane baseline before its first server tick.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ApplyInput_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("ApplyInput",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "The server integrates input through a public ApplyInput.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "ApplyInput lost its [Server] attribute, so any client could move locally.");
        }

        [Test]
        public void RpcRequestMoveIntent_IsAServerRpc()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("RpcRequestMoveIntent",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, "The direct-drive intent must reach the server as an RPC.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerRpcAttribute>(),
                "A walking intent is server input; a client that can call it locally moves itself.");
        }

        [Test]
        public void ApplyReportedPosition_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("ApplyReportedPosition",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "Position-bearing messages are judged by ApplyReportedPosition.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "ApplyReportedPosition lost its [Server] attribute, so a client could author positions.");
        }

        [Test]
        public void Teleport_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("Teleport",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "Abilities and respawns need a privileged reposition.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "Teleport is the one way to bypass the validator; it must stay server-only.");
        }

        [Test]
        public void RequestMoveTo_BeforeTheObjectIsInitialized_DoesNothing()
        {
            var go = new GameObject("PlayerMoverUnderTest");
            try
            {
                go.AddComponent<NetworkObject>();
                PlayerMover mover = go.AddComponent<PlayerMover>();

                Assert.DoesNotThrow(() => mover.RequestMoveTo(Vector3.forward),
                    "A not-yet-initialised mover has no owner and must not start an RPC.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetSpeedMultiplier_IsServerOnly()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("SetSpeedMultiplier",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "Mounts scale the mover through SetSpeedMultiplier.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "A client that can set its own speed can outrun every mount.");
        }

        [Test]
        public void SetAuraSpeedMultiplier_IsServerOnly()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("SetAuraSpeedMultiplier",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "Auras scale the mover through their own multiplier.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "A client that can clear its own root can walk through crowd control.");
        }

        [Test]
        public void SetFlight_IsServerOnly()
        {
            MethodInfo method = typeof(PlayerMover).GetMethod("SetFlight",
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "Mounts switch the mover to volumetric movement through SetFlight.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "Flight is a server decision; a client must not grant itself vertical movement.");
        }
    }
}
