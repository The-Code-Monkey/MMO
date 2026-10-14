using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// Proves the vitals are genuinely server-owned over a live FishNet connection: a real host
    /// spawns its player object, the server writes the SyncVars, and the values that stick are the
    /// ones the server chose.
    ///
    /// <para>
    /// What this file deliberately does <em>not</em> do is try to catch a cheating client. That needs
    /// a server and a client in two processes, because FishNet permits exactly one
    /// <c>NetworkManager</c> per process and a host is server and client simultaneously, so a client
    /// cannot be made to misbehave from in here. The half of that guarantee which <em>is</em>
    /// checkable in-process — no public setter, and every mutator carrying <c>[Server]</c> — is
    /// asserted by reflection in <c>PlayerStatsTests</c>; FishNet refuses off-server SyncVar writes
    /// by construction.
    /// </para>
    /// </summary>
    public sealed class PlayerStatsPlayModeTests
    {
        private NetworkManager _manager;

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
            NetworkManagerTestHelper.StopIfListening(_manager);

            // Stopping is asynchronous; let it finish so the next test starts genuinely offline.
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_StartsAtFullHealthAndMana()
        {
            yield return WithSpawnedStats(stats =>
            {
                Assert.AreEqual(PlayerStats.MaxHealth, stats.Health,
                    "The server fills health on spawn; zero would mean the reset never ran.");
                Assert.AreEqual(PlayerStats.MaxMana, stats.Mana,
                    "The server fills mana on spawn; zero would mean the reset never ran.");
            });
        }

        [UnityTest]
        public IEnumerator ServerTakingDamage_LowersHealth()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.TakeDamage(30);

                Assert.AreEqual(70, stats.Health);
            });
        }

        [UnityTest]
        public IEnumerator DamageLargerThanHealth_ClampsToZero()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.TakeDamage(9999);

                Assert.AreEqual(0, stats.Health,
                    "A player cannot be driven below zero, however large the hit.");
            });
        }

        [UnityTest]
        public IEnumerator HealingAfterLethalDamage_StopsAtTheCap()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.TakeDamage(9999);
                Assert.AreEqual(0, stats.Health);

                stats.Heal(9999);

                Assert.AreEqual(PlayerStats.MaxHealth, stats.Health);
            });
        }

        [UnityTest]
        public IEnumerator SpendingMoreManaThanAvailable_ClampsToZero()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.SpendMana(PlayerStats.MaxMana + 50);

                Assert.AreEqual(0, stats.Mana,
                    "A player cannot spend mana they do not have, however large the cost.");
            });
        }

        [UnityTest]
        public IEnumerator RestoringMana_StopsAtTheCap()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.SpendMana(40);
                stats.RestoreMana(500);

                Assert.AreEqual(PlayerStats.MaxMana, stats.Mana);
            });
        }

        [UnityTest]
        public IEnumerator HealthAndMana_AreIndependent()
        {
            yield return WithSpawnedStats(stats =>
            {
                stats.TakeDamage(40);
                stats.SpendMana(20);

                Assert.AreEqual(60, stats.Health, "Spending mana must not touch health.");
                Assert.AreEqual(30, stats.Mana, "Taking damage must not touch mana.");
            });
        }

        [UnityTest]
        public IEnumerator DamageRequestedByTheOwner_IsAppliedByTheServer()
        {
            PlayerStats stats = null;

            yield return WithSpawnedStats(s => stats = s);

            // The host owns its own player, so this travels the real RPC path rather than writing
            // the SyncVar directly: RequestDamage -> [ServerRpc] -> owner check -> [Server] TakeDamage.
            // A host still writes to the server through the loopback socket, so the server applies
            // it a frame or two later; poll rather than assume a frame count.
            stats.RequestDamage(25);

            yield return WaitUntil(() => stats.Health == 75, 300);

            Assert.AreEqual(75, stats.Health,
                "A requested hit must be applied by the server, not by the client asking for it.");
        }

        private IEnumerator WithSpawnedStats(Action<PlayerStats> assertions)
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            var stats = player.GetComponent<PlayerStats>();
            Assert.IsNotNull(stats, "The prefab's networked stats have to travel with it.");

            // These tests observe the raw vitals; a lethal hit also wakes the death rules, which
            // resurrect on zero health. Disabling the death component keeps this suite about the
            // clamp itself — the death cycle is covered by PlayerDeathPlayModeTests.
            var death = player.GetComponent<PlayerDeath>();
            if (death != null)
                death.enabled = false;

            assertions(stats);
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
