using System;
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
    /// The vitals half of the server-authoritative enemy: health as a server-owned SyncVar, plus
    /// the mutators that only the server is allowed to run.
    ///
    /// As with <see cref="PlayerStats"/>, the arithmetic is deliberately kept in static,
    /// NetworkBehaviour-free helpers — that is what makes it reachable from an EditMode test, since
    /// a SyncVar only has a meaningful value once the server has spawned the object. The wiring
    /// that turns those helpers into network state is covered by the PlayMode tests.
    /// </summary>
    public sealed class NPCStatsTests
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

        [Test]
        public void ComputeNewHealth_ReducesHealthByAmount()
        {
            Assert.AreEqual(30, NPCStats.ComputeNewHealth(50, 20));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtZero()
        {
            Assert.AreEqual(0, NPCStats.ComputeNewHealth(30, 999));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtMax()
        {
            Assert.AreEqual(NPCStats.MaxHealth, NPCStats.ComputeNewHealth(NPCStats.MaxHealth, 0));
        }

        [Test]
        public void ComputeNewHealth_NegativeAmountDoesNotHeal()
        {
            Assert.AreEqual(50, NPCStats.ComputeNewHealth(50, -10),
                "A negative damage payload must be dropped, not turned into a heal.");
        }

        [Test]
        public void ComputeNewHealth_ZeroDamageKeepsHealth()
        {
            Assert.AreEqual(27, NPCStats.ComputeNewHealth(27, 0));
        }

        [Test]
        public void ComputeHealedHealth_AddsAmount()
        {
            Assert.AreEqual(45, NPCStats.ComputeHealedHealth(30, 15));
        }

        [Test]
        public void ComputeHealedHealth_ClampsAtMax()
        {
            Assert.AreEqual(NPCStats.MaxHealth, NPCStats.ComputeHealedHealth(40, 500));
        }

        [Test]
        public void ComputeHealedHealth_NegativeAmountIsIgnored()
        {
            Assert.AreEqual(30, NPCStats.ComputeHealedHealth(30, -50));
        }

        [Test]
        public void MaxHealth_IsGreaterThanZero()
        {
            Assert.Greater(NPCStats.MaxHealth, 0, "A health pool of zero makes the stat meaningless.");
        }

        [Test]
        public void IsDeathTransition_OnlyOnTheCrossingIntoZero()
        {
            Assert.IsTrue(NPCStats.IsDeathTransition(10, 0));
            Assert.IsTrue(NPCStats.IsDeathTransition(1, 0));
            Assert.IsFalse(NPCStats.IsDeathTransition(0, 0),
                "A restored corpse must not be treated as a fresh death.");
            Assert.IsFalse(NPCStats.IsDeathTransition(10, 4));
        }

        [Test]
        public void Died_IsAnEventCarryingTheKiller()
        {
            EventInfo died = typeof(NPCStats).GetEvent("Died", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(died, "Loot and quests need one death hook to subscribe to.");
            Assert.AreEqual(typeof(Action<PlayerInventory>), died.EventHandlerType,
                "The killer has to travel with the death; downstream handlers must not guess.");
        }

        [Test]
        public void LastAttacker_IsReadOnly()
        {
            PropertyInfo property = typeof(NPCStats).GetProperty("LastAttacker", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property);
            Assert.AreEqual(typeof(PlayerInventory), property.PropertyType);
            Assert.IsNull(property.GetSetMethod(nonPublic: false),
                "Only ApplyDamage may name the attacker; a public setter would let any caller forge credit.");
        }

        [Test]
        public void ApplyDamage_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(NPCStats).GetMethod(
                "ApplyDamage", BindingFlags.Public | BindingFlags.Instance, null,
                new[] { typeof(int), typeof(PlayerInventory) }, null);

            Assert.IsNotNull(method, "Expected a public ApplyDamage(int amount, PlayerInventory source).");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "ApplyDamage lost its [Server] attribute, so a client could author a kill.");
        }

        [Test]
        public void ResetHealth_IsPublicAndCarriesTheServerAttribute()
        {
            MethodInfo method = typeof(NPCStats).GetMethod("ResetHealth", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(method, "The spawn controller needs a server-only respawn reset.");
            Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                "ResetHealth lost its [Server] attribute, so a client could revive an enemy.");
        }

        [Test]
        public void Health_IsASyncVarSoOnlyTheServerCanWriteIt()
        {
            // The guarantee the issue is really asking for: a client cannot author an enemy's
            // health. That only holds if the value is a SyncVar, because FishNet refuses off-server
            // writes to it and re-sends the server's value to everyone regardless of what a client
            // sent.
            var syncVars = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(NPCStats).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    syncVars.Add(field);
                }
            }

            Assert.AreEqual(1, syncVars.Count,
                "NPCStats should hold exactly one SyncVar: health.");
        }

        [Test]
        public void Health_ExposesNoPublicSetter()
        {
            PropertyInfo property = typeof(NPCStats).GetProperty("Health", BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, "Health should be readable by clients.");
            Assert.IsNull(property.SetMethod,
                "Health must be read-only publicly; a setter would let a client edit an enemy's vitals.");
        }

        [Test]
        public void Mutators_ArePublicAndCarryTheServerAttribute()
        {
            // [Server] is enforced by FishNet's codegen, which injects an IsServer check into the
            // method body at compile time. Nothing reads the attribute back at runtime, so this is
            // the only way to catch someone deleting it in a later refactor.
            foreach (string name in new[] { "TakeDamage", "Heal" })
            {
                MethodInfo method = GetMutator(name);

                Assert.IsTrue(method.IsPublic,
                    $"{name} must be public so server-side systems can reach it.");
                Assert.IsNotNull(method.GetCustomAttribute<ServerAttribute>(),
                    $"{name} lost its [Server] attribute, so any client could call it locally.");
            }
        }

        [Test]
        public void PureHelpers_StayStaticAndUnguarded()
        {
            foreach (string name in new[] { "ComputeNewHealth", "ComputeHealedHealth" })
            {
                MethodInfo method = typeof(NPCStats).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

                Assert.IsNotNull(method, $"{name} should be a public static helper.");
                Assert.IsNull(method.GetCustomAttribute<ServerAttribute>(),
                    $"{name} is pure arithmetic; a server guard here would only make it untestable.");
            }
        }

        [Test]
        public void NetworkCallbacks_AreOverridden()
        {
            // Health starts at zero until the server writes it on spawn, so the reset in
            // OnStartNetwork is what makes a freshly spawned enemy full.
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(NPCStats).BaseType);
            Assert.IsNotNull(typeof(NPCStats).GetMethod("OnStartNetwork", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void UnspawnedStats_ReadZeroRatherThanThrowing()
        {
            var stats = AttachStats();

            Assert.AreEqual(0, stats.Health);
            Assert.IsFalse(stats.IsAlive);
        }

        /// <summary>
        /// Health above zero is what every actor query (and the health bar) keys off.
        /// </summary>
        [Test]
        public void IsAlive_ReflectsTheHealthPool()
        {
            NPCStats stats = AttachStats();

            // The SyncVar is server-write-only; the health pool is driven through the arithmetic so
            // an alive/dead decision cannot drift from the numbers.
            Assert.AreEqual(stats.Health > 0, stats.IsAlive);
        }

        private NPCStats AttachStats()
        {
            var go = new GameObject("NPCStatsUnderTest");
            _sceneObjects.Add(go);

            return go.AddComponent<NPCStats>();
        }

        private static MethodInfo GetMutator(string name)
        {
            MethodInfo method = typeof(NPCStats).GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);

            Assert.IsNotNull(method, $"Expected a public {name}(int amount) server mutator.");

            return method;
        }
    }
}