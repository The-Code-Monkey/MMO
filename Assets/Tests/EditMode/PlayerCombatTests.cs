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
    /// The combat half of the server-authoritative player: the distance check, the weapon the
    /// server reads, and the intent pipeline that may only be started by the owner.
    ///
    /// As with <see cref="PlayerStatsTests"/>, the arithmetic and the theory of the wiring live in
    /// static, NetworkBehaviour-free helpers so an EditMode test can reach them without a spawned
    /// object. The attributes are asserted by reflection — FishNet injects the actual runtime guard
    /// at compile time, so nothing reads them back — and what a live host actually does with a
    /// click is covered by the PlayMode tests.
    /// </summary>
    public sealed class PlayerCombatTests
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

        #region Distance arithmetic.
        [Test]
        public void IsWithinAttackRange_InsideTheRange_IsInRange()
        {
            Vector3 attacker = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 5f);

            Assert.IsTrue(PlayerCombat.IsWithinAttackRange(attacker, target, 10f));
        }

        [Test]
        public void IsWithinAttackRange_OutsideTheRange_IsNotInRange()
        {
            Vector3 attacker = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 12f);

            Assert.IsFalse(PlayerCombat.IsWithinAttackRange(attacker, target, 10f));
        }

        [Test]
        public void IsWithinAttackRange_ExactlyOnTheBoundary_Misses()
        {
            Vector3 attacker = Vector3.zero;
            Vector3 target = new Vector3(0f, 0f, 10f);

            // The issue says "less than AttackRange"; a target parked exactly on the boundary is
            // out of reach, which is what keeps a floating-point boundary from tipping either way.
            Assert.IsFalse(PlayerCombat.IsWithinAttackRange(attacker, target, 10f));
        }

        [Test]
        public void IsWithinAttackRange_AWeaponWithNoRange_NeverHits()
        {
            Assert.IsFalse(PlayerCombat.IsWithinAttackRange(Vector3.zero, new Vector3(0f, 0f, 1f), 0f));
        }

        [Test]
        public void IsWithinAttackRange_ANegativeRange_NeverHits()
        {
            Assert.IsFalse(PlayerCombat.IsWithinAttackRange(Vector3.zero, Vector3.zero, -1f),
                "Range is a distance, never a signed quantity.");
        }

        [Test]
        public void IsWithinAttackRange_MeasuresTrueDistanceNotAxisAlignedDistance()
        {
            Vector3 attacker = Vector3.zero;
            Vector3 target = new Vector3(3f, 0f, 4f);

            Assert.IsTrue(PlayerCombat.IsWithinAttackRange(attacker, target, 6f),
                "3-4-5 triangle: the straight-line distance is 5, under the 6 metre range.");
            Assert.IsFalse(PlayerCombat.IsWithinAttackRange(attacker, target, 4f));
        }
        #endregion

        #region The weapon the player holds.
        [Test]
        public void EquippedWeapon_DefaultsToUnarmed()
        {
            PlayerCombat combat = AttachCombat();

            Assert.IsNull(combat.EquippedWeapon,
                "The code-built player starts unarmed; only the editor bake (or an equip call) arms it.");
        }

        [Test]
        public void EquippedWeapon_IsSerializedSoTheEditorCanBakeIt()
        {
            // The baker writes the default weapon with a SerializedObject, which only reaches a
            // private [SerializeField]. A public field or an editor-only helper would both be
            // programmer-facing back doors; the serialized field stays the single path.
            FieldInfo field = typeof(PlayerCombat).GetField("_equippedWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "The equipped weapon must be a serialized field.");
            Assert.AreEqual(typeof(WeaponData), field.FieldType);
            Assert.IsNotNull(field.GetCustomAttribute<SerializeField>());
        }

        [Test]
        public void EquippedWeapon_HasNoPublicSetter()
        {
            PropertyInfo property = typeof(PlayerCombat).GetProperty("EquippedWeapon", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, "Clients need to read the equipped weapon for UI.");
            Assert.IsNull(property.SetMethod,
                "An equipped weapon is server-chosen; a public setter would let a client arm itself.");
        }

        [Test]
        public void Equip_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = PublicMethod("Equip", typeof(WeaponData));

            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "Equip lost its [Server] attribute, so any client could hand itself a weapon locally.");
        }

        [Test]
        public void Attack_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = PublicMethod("Attack", typeof(NetworkObject));

            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "Attack lost its [Server] attribute, so any client could decide its own hits.");
        }
        #endregion

        #region The attack request pipeline.
        [Test]
        public void RpcRequestAttack_IsAServerRpcThatRequiresOwnership()
        {
            MethodInfo method = PrivateMethod("RpcRequestAttack");

            ServerRpcAttribute attribute = method.GetCustomAttribute<ServerRpcAttribute>();
            Assert.IsNotNull(attribute,
                "The click intent has to travel as a ServerRpc or the client would be deciding hits.");
            Assert.IsTrue(attribute.RequireOwnership,
                "Only the owning client may ask to attack; FishNet drops ServerRpcs from anyone else.");
        }

        [Test]
        public void RpcRequestAttack_CarriesTheTargetAndTheSender()
        {
            MethodInfo method = PrivateMethod("RpcRequestAttack");

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(2, parameters.Length, "The target and the sender are what the server needs.");

            Assert.AreEqual(typeof(NetworkObject), parameters[0].ParameterType,
                "The target travels as a NetworkObject so the server re-resolves the live instance.");
            Assert.AreEqual(typeof(NetworkConnection), parameters[1].ParameterType,
                "The sender is injected by FishNet so the server can re-check ownership.");
            Assert.IsTrue(parameters[1].IsOptional && parameters[1].HasDefaultValue,
                "The sender must be an optional, default-value parameter — that is how FishNet detects it.");
        }

        [Test]
        public void RequestAttack_WithNoTarget_DoesNothing()
        {
            PlayerCombat combat = AttachCombat();

            Assert.DoesNotThrow(() => combat.RequestAttack(null),
                "A click that resolved no target must be a no-op, never an exception.");
        }

        [Test]
        public void RequestAttack_BeforeTheObjectIsInitialized_DoesNothing()
        {
            GameObject target = Track(new GameObject("UnspawnedTarget"));
            NetworkObject targetNob = target.AddComponent<NetworkObject>();

            PlayerCombat combat = AttachCombat();

            // NetworkObject is null until FishNet initialises this behaviour (at registration or
            // spawn); an uninitialised player has no owner and must not start an RPC.
            Assert.DoesNotThrow(() => combat.RequestAttack(targetNob));
        }

        [Test]
        public void FindLocal_WithNoManager_ReturnsNull()
        {
            Assert.IsNull(PlayerCombat.FindLocal(null));
        }
        #endregion

        private PlayerCombat AttachCombat()
        {
            GameObject go = Track(new GameObject("PlayerCombatUnderTest"));
            go.AddComponent<NetworkObject>();
            return go.AddComponent<PlayerCombat>();
        }

        private static MethodInfo PublicMethod(string name, params System.Type[] parameterTypes)
        {
            MethodInfo method = typeof(PlayerCombat).GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance, null, parameterTypes, null);

            Assert.IsNotNull(method, $"Expected a public {name} for those parameters.");

            return method;
        }

        private static MethodInfo PrivateMethod(string name)
        {
            MethodInfo method = typeof(PlayerCombat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(method, $"Expected a private {name}.");

            return method;
        }
    }
}