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
    /// The vitals half of the server-authoritative player: health and mana as server-owned SyncVars,
    /// plus the mutators that only the server is allowed to run.
    ///
    /// The arithmetic is deliberately kept in static, NetworkBehaviour-free helpers. That is what
    /// makes it reachable from an EditMode test: a SyncVar only has a meaningful value once the
    /// server has spawned the object, so a NetworkManager would otherwise be the price of checking
    /// that 30 damage leaves 70 health. The wiring that turns those helpers into network state is
    /// covered by the PlayMode tests.
    /// </summary>
    public sealed class PlayerStatsTests
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
            Assert.AreEqual(70, PlayerStats.ComputeNewHealth(100, 30));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtZero()
        {
            Assert.AreEqual(0, PlayerStats.ComputeNewHealth(50, 999));
        }

        [Test]
        public void ComputeNewHealth_ClampsAtMax()
        {
            Assert.AreEqual(PlayerStats.MaxHealth, PlayerStats.ComputeNewHealth(PlayerStats.MaxHealth, 0));
        }

        [Test]
        public void ComputeNewHealth_NegativeAmountDoesNotHeal()
        {
            Assert.AreEqual(50, PlayerStats.ComputeNewHealth(50, -10),
                "A negative damage payload must be dropped, not turned into a heal.");
        }

        [Test]
        public void ComputeNewHealth_ZeroDamageKeepsHealth()
        {
            Assert.AreEqual(42, PlayerStats.ComputeNewHealth(42, 0));
        }

        [Test]
        public void MaxHealth_IsOneHundred()
        {
            Assert.AreEqual(100, PlayerStats.MaxHealth);
        }

        [Test]
        public void ComputeHealedHealth_AddsAmount()
        {
            Assert.AreEqual(80, PlayerStats.ComputeHealedHealth(50, 30));
        }

        [Test]
        public void ComputeHealedHealth_ClampsAtMax()
        {
            Assert.AreEqual(PlayerStats.MaxHealth, PlayerStats.ComputeHealedHealth(90, 500),
                "Healing must not push a player past their maximum.");
        }

        [Test]
        public void ComputeHealedHealth_NegativeAmountIsIgnored()
        {
            Assert.AreEqual(50, PlayerStats.ComputeHealedHealth(50, -30));
        }

        [Test]
        public void ComputeNewMana_ReducesManaByAmount()
        {
            Assert.AreEqual(20, PlayerStats.ComputeNewMana(50, 30));
        }

        [Test]
        public void ComputeNewMana_ClampsAtZero()
        {
            Assert.AreEqual(0, PlayerStats.ComputeNewMana(10, 999),
                "A player cannot spend mana they do not have.");
        }

        [Test]
        public void ComputeNewMana_ClampsAtMax()
        {
            Assert.AreEqual(PlayerStats.MaxMana, PlayerStats.ComputeNewMana(PlayerStats.MaxMana, 0));
        }

        [Test]
        public void ComputeNewMana_NegativeAmountDoesNotRestore()
        {
            Assert.AreEqual(20, PlayerStats.ComputeNewMana(20, -10),
                "A negative cost is a malformed payload, not a free cast.");
        }

        [Test]
        public void ComputeNewMana_ZeroCostKeepsMana()
        {
            Assert.AreEqual(7, PlayerStats.ComputeNewMana(7, 0));
        }

        [Test]
        public void ComputeRestoredMana_AddsAmount()
        {
            Assert.AreEqual(35, PlayerStats.ComputeRestoredMana(20, 15));
        }

        [Test]
        public void ComputeRestoredMana_ClampsAtMax()
        {
            Assert.AreEqual(PlayerStats.MaxMana, PlayerStats.ComputeRestoredMana(45, 100));
        }

        [Test]
        public void ComputeRestoredMana_NegativeAmountIsIgnored()
        {
            Assert.AreEqual(20, PlayerStats.ComputeRestoredMana(20, -15));
        }

        [Test]
        public void MaxMana_IsGreaterThanZero()
        {
            Assert.Greater(PlayerStats.MaxMana, 0, "A mana pool of zero makes the stat meaningless.");
        }

        [Test]
        public void Vitals_AreSyncVarsSoOnlyTheServerCanWriteThem()
        {
            // The guarantee the issue is really asking for: a client cannot author its own vitals.
            // That only holds if the values are SyncVars, because FishNet refuses off-server writes to
            // them and re-sends the server's value to everyone regardless of what a client sent.
            var syncVars = new List<FieldInfo>();

            foreach (FieldInfo field in typeof(PlayerStats).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                {
                    syncVars.Add(field);
                }
            }

            Assert.AreEqual(2, syncVars.Count,
                "PlayerStats should hold exactly two SyncVars: health and mana.");
        }

        [Test]
        public void Vitals_ExposeNoPublicSetter()
        {
            // A public setter is the easy way to lose server authority, so it is asserted absent.
            foreach (string stat in new[] { "Health", "Mana" })
            {
                PropertyInfo property = typeof(PlayerStats).GetProperty(stat, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{stat} should be readable by clients.");
                Assert.IsNull(property.SetMethod,
                    $"{stat} must be read-only publicly; a setter would let a client edit its own vitals.");
            }
        }

        [Test]
        public void Mutators_ArePublicAndCarryTheServerAttribute()
        {
            // [Server] is enforced by FishNet's codegen, which injects an IsServer check into the
            // method body at compile time. Nothing reads the attribute back at runtime, so this is
            // the only way to catch someone deleting it in a later refactor.
            foreach (string name in new[] { "TakeDamage", "Heal", "SpendMana", "RestoreMana" })
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
            foreach (string name in new[]
                     {
                         "ComputeNewHealth", "ComputeHealedHealth", "ComputeNewMana", "ComputeRestoredMana",
                     })
            {
                MethodInfo method = typeof(PlayerStats).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

                Assert.IsNotNull(method, $"{name} should be a public static helper.");
                Assert.IsNull(method.GetCustomAttribute<ServerAttribute>(),
                    $"{name} is pure arithmetic; a server guard here would only make it untestable.");
            }
        }

        [Test]
        public void NetworkCallbacks_AreOverridden()
        {
            // Health and mana start at zero until the server writes them on spawn, so the reset in
            // OnStartNetwork is what makes a freshly spawned player full.
            Assert.AreEqual(typeof(NetworkBehaviour), typeof(PlayerStats).BaseType);
            Assert.IsNotNull(typeof(PlayerStats).GetMethod("OnStartNetwork", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void UnspawnedStats_ReadZeroRatherThanThrowing()
        {
            PlayerStats stats = AttachStats();

            Assert.AreEqual(0, stats.Health);
            Assert.AreEqual(0, stats.Mana);
        }

        private PlayerStats AttachStats()
        {
            var go = new GameObject("PlayerStatsUnderTest");
            _sceneObjects.Add(go);

            return go.AddComponent<PlayerStats>();
        }

        private static MethodInfo GetMutator(string name)
        {
            MethodInfo method = typeof(PlayerStats).GetMethod(name,
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);

            Assert.IsNotNull(method, $"Expected a public {name}(int amount) server mutator.");

            return method;
        }
    }
}
