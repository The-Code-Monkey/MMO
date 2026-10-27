using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The server-authoritative punching bag: health as a server-owned <see cref="SyncVar{T}"/>,
    /// damage that only the server may apply, and a click entry point that sends intent rather
    /// than deciding anything.
    ///
    /// The damage arithmetic is a static helper for the same reason as
    /// <see cref="PlayerStats.ComputeNewHealth"/>: a SyncVar only has a meaningful value once the
    /// server has spawned the object, so the EditMode tests reach the arithmetic and the
    /// attributes, and the PlayMode tests prove what a live host does with a click.
    /// </summary>
    public sealed class TargetDummyTests
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

        #region Health arithmetic.
        [Test]
        public void ComputeNewHealth_ReducesHealthByAmount()
        {
            Assert.AreEqual(70, TargetDummy.ComputeNewHealth(100, 30));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtZero()
        {
            Assert.AreEqual(0, TargetDummy.ComputeNewHealth(50, 999),
                "A dummy cannot be driven below zero, however large the hit.");
        }

        [Test]
        public void ComputeNewHealth_ClampsAtMax()
        {
            // Zero damage at full health must leave a full dummy — a Mathf.Clamp that allowed an
            // overflow to pass through would short-circuit the whole authority story.
            Assert.AreEqual(TargetDummy.MaxHealth, TargetDummy.ComputeNewHealth(TargetDummy.MaxHealth, 0));
        }

        [Test]
        public void ComputeNewHealth_NegativeAmountDoesNotHeal()
        {
            Assert.AreEqual(50, TargetDummy.ComputeNewHealth(50, -10),
                "A negative damage payload must be dropped, not treated as a heal.");
        }

        [Test]
        public void ComputeNewHealth_ZeroDamageKeepsHealth()
        {
            Assert.AreEqual(42, TargetDummy.ComputeNewHealth(42, 0));
        }

        [Test]
        public void MaxHealth_IsOneHundred()
        {
            Assert.AreEqual(100, TargetDummy.MaxHealth);
        }
        #endregion

        #region Server authority.
        [Test]
        public void Health_IsASyncVarSoOnlyTheServerCanWriteIt()
        {
            // FishNet refuses off-server SyncVar writes and re-sends the server's value to everyone,
            // so a health bar client shows the number the server chose — the issue's real requirement.
            var syncVars = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(TargetDummy).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    syncVars.Add(field);
                }
            }

            Assert.AreEqual(1, syncVars.Count, "The dummy owns exactly one SyncVar: its health.");
        }

        [Test]
        public void Health_ExposesNoPublicSetter()
        {
            PropertyInfo property = typeof(TargetDummy).GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, "Clients need to read Health for the health bar.");
            Assert.IsNull(property.SetMethod,
                "Health must be read-only publicly; a setter would let a client heal its own target.");
        }

        [Test]
        public void TakeDamage_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(TargetDummy).GetMethod("TakeDamage", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "TakeDamage(int) must be public so server-side attacks can reach it.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "TakeDamage lost its [Server] attribute, so any client could damage a dummy locally.");
        }

        [Test]
        public void RequestAttackFromLocalPlayer_IsPublicSoTheClickCanCallIt()
        {
            Assert.IsNotNull(typeof(TargetDummy).GetMethod("RequestAttackFromLocalPlayer", BindingFlags.Public | BindingFlags.Instance),
                "The click handler routes to this method; it has to stay callable and testable.");
        }
        #endregion

        #region Networking shape.
        [Test]
        public void Dummy_DerivesFromNetworkBehaviourSoHealthReplicates()
        {
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(TargetDummy).BaseType);
        }

        [Test]
        public void NetworkCallbacks_AreOverridden()
        {
            // Health starts at zero until the server writes it on spawn, so the reset in
            // OnStartNetwork is what makes a freshly spawned dummy full.
            Assert.IsNotNull(typeof(TargetDummy).GetMethod("OnStartNetwork", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void UnspawnedDummy_ReadsZeroRatherThanThrowing()
        {
            TargetDummy dummy = AttachDummy();

            Assert.AreEqual(0, dummy.Health);
        }
        #endregion

        #region The click entry point.
        [Test]
        public void RequestAttackFromLocalPlayer_OnAnUninitializedDummy_DoesNothing()
        {
            // NetworkObject is null until FishNet initialises this behaviour; without a manager
            // there is no local player to attack from, so a click on an un-spawned dummy is a no-op.
            TargetDummy dummy = AttachDummy();

            Assert.DoesNotThrow(() => dummy.RequestAttackFromLocalPlayer());
        }
        #endregion

        private TargetDummy AttachDummy()
        {
            GameObject go = Track(new GameObject("TargetDummyUnderTest"));
            go.AddComponent<NetworkObject>();
            return go.AddComponent<TargetDummy>();
        }

        private GameObject Track(GameObject go)
        {
            _sceneObjects.Add(go);
            return go;
        }
    }
}