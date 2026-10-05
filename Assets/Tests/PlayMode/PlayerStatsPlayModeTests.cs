using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class PlayerStatsPlayModeTests
    {
        private GameObject _managerGo;
        private NetworkManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _managerGo = new GameObject("PlayerStatsTestManager");
            _manager = _managerGo.AddComponent<NetworkManager>();
            _managerGo.AddComponent<Tugboat>();

            Assert.IsTrue(_manager.ServerManager.StartConnection(), "Server failed to start.");
            yield return null;
            Assert.IsTrue(_manager.ClientManager.StartConnection(), "Client failed to start.");

            float timeout = 5f;
            while (!_manager.IsHostStarted && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_manager.IsHostStarted, "Host did not start within timeout.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null)
            {
                _manager.ServerManager.StopConnection(true);
                _manager.ClientManager.StopConnection();
            }

            yield return null;
            yield return null;

            if (_managerGo != null)
                Object.Destroy(_managerGo);
        }

        [UnityTest]
        public IEnumerator SpawnedStats_HaveFullHealth()
        {
            var stats = SpawnStats(withOwner: true);
            yield return null;

            Assert.AreEqual(100, stats.Health, "Initial health should be 100.");

            DespawnStats(stats);
        }

        [UnityTest]
        public IEnumerator RequestDamage_ReducesHealth()
        {
            var stats = SpawnStats(withOwner: true);
            yield return null;

            stats.RequestDamage(30);

            yield return WaitForHealth(stats, 70);

            Assert.AreEqual(70, stats.Health, "Health should drop to 70.");

            DespawnStats(stats);
        }

        [UnityTest]
        public IEnumerator RequestDamage_ClampsAtZero()
        {
            var stats = SpawnStats(withOwner: true);
            yield return null;

            stats.RequestDamage(150);

            yield return WaitForHealth(stats, 0);

            Assert.AreEqual(0, stats.Health, "Health should clamp to 0.");

            DespawnStats(stats);
        }

        [UnityTest]
        public IEnumerator RequestDamage_NegativeAmountDoesNotHeal()
        {
            var stats = SpawnStats(withOwner: true);
            yield return null;

            stats.RequestDamage(-50);

            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(100, stats.Health, "Negative damage must not heal.");

            DespawnStats(stats);
        }

        [UnityTest]
        public IEnumerator RequestDamage_ZeroAmountIsNoOp()
        {
            var stats = SpawnStats(withOwner: true);
            yield return null;

            stats.RequestDamage(0);

            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(100, stats.Health, "Zero damage must not change health.");

            DespawnStats(stats);
        }

        [UnityTest]
        public IEnumerator RequestDamage_NonOwnerObjectDoesNotDamage()
        {
            var stats = SpawnStats(withOwner: false);
            yield return null;

            stats.RequestDamage(30);

            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(100, stats.Health, "Non-owner should not damage.");

            DespawnStats(stats);
        }

        private PlayerStats SpawnStats(bool withOwner)
        {
            var go = new GameObject("PlayerStats");
            var nob = go.AddComponent<NetworkObject>();
            var stats = go.AddComponent<PlayerStats>();

            _manager.ServerManager.Spawn(nob, withOwner ? _manager.ClientManager.Connection : null);

            return stats;
        }

        private IEnumerator WaitForHealth(PlayerStats stats, int expected)
        {
            float timeout = 3f;
            while (stats.Health != expected && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return new WaitForSeconds(0.05f);
            }
        }

        private void DespawnStats(PlayerStats stats)
        {
            if (stats != null && stats.NetworkObject != null)
                _manager.ServerManager.Despawn(stats.NetworkObject);
        }
    }
}
