using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Content;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The master spawn controller over a live host: proximity changes the population, the cap is
    /// respected, and a defeated enemy returns after its respawn timer.
    /// </summary>
    public sealed class SpawnControllerPlayModeTests
    {
        private NetworkManager _manager;
        private NetworkObject _player;
        private WorldBootstrap _bootstrap;
        private readonly List<GameObject> _objects = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            if (_bootstrap != null)
                Object.Destroy(_bootstrap);

            foreach (GameObject go in _objects)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            _objects.Clear();
            _bootstrap = null;
            _player = null;

            yield return null;
        }

        [UnityTest]
        public IEnumerator PopulationScalesWithNearbyPlayers()
        {
            yield return StartHostWithPlayer();

            EnemyMarker marker = CreateMarker(_player.transform.position);
            marker.SetTuning(0, 2, 1, respawnSeconds: 0.3f, wanderRadius: 0f, activationRadius: 0f);

            SpawnController controller = AddController(marker);
            yield return null;
            yield return null;

            Assert.AreEqual(0, controller.AliveCount,
                "No player is inside a zero activation radius.");

            marker.SetTuning(0, 2, 1, respawnSeconds: 0.3f, wanderRadius: 0f, activationRadius: 20f);
            yield return WaitUntil(() => controller.AliveCount == 1, 300);
            Assert.AreEqual(1, controller.AliveCount);

            marker.SetTuning(0, 2, 1, respawnSeconds: 0.3f, wanderRadius: 0f, activationRadius: 0f);
            yield return WaitUntil(() => controller.AliveCount == 0, 300);
            Assert.AreEqual(0, controller.AliveCount, "Leaving the activation radius clears the population.");
        }

        [UnityTest]
        public IEnumerator PopulationNeverExceedsTheAuthoredMaximum()
        {
            yield return StartHostWithPlayer();

            EnemyMarker marker = CreateMarker(_player.transform.position);
            marker.SetTuning(0, 2, 10, respawnSeconds: 0.3f, wanderRadius: 0f, activationRadius: 20f);

            SpawnController controller = AddController(marker);
            yield return WaitUntil(() => controller.AliveCount == 2, 300);

            Assert.AreEqual(2, controller.AliveCount);

            for (int i = 0; i < 30; i++)
                yield return null;

            Assert.LessOrEqual(controller.AliveCount, 2);
        }

        [UnityTest]
        public IEnumerator KilledEnemyRespawnsAfterItsTimer()
        {
            yield return StartHostWithPlayer();

            EnemyMarker marker = CreateMarker(_player.transform.position);
            marker.SetTuning(1, 1, 1, respawnSeconds: 0.3f, wanderRadius: 0f, activationRadius: 20f);

            SpawnController controller = AddController(marker);
            yield return WaitUntil(() => controller.AliveCount == 1, 300);

            NPCStats enemy = controller.AliveEnemies[0];
            Assert.IsNotNull(enemy);

            double killedAt = Time.timeAsDouble;
            enemy.TakeDamage(NPCStats.MaxHealth);
            yield return WaitUntil(() => controller.AliveCount == 0, 120);
            Assert.AreEqual(0, controller.AliveCount);

            yield return WaitUntil(() => controller.AliveCount == 1, 300);
            double elapsed = Time.timeAsDouble - killedAt;

            Assert.AreEqual(1, controller.AliveCount);
            Assert.GreaterOrEqual(elapsed, 0.25d, "The replacement must wait out the respawn timer.");
            Assert.IsTrue(enemy == null, "The defeated corpse is despawned before its replacement appears.");
        }

        [UnityTest]
        public IEnumerator WorldBootstrapConfiguresAControllerForAnEnemyMarker()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);
            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player);

            EnemyMarker marker = CreateMarker(_player.transform.position);
            marker.SetTuning(1, 1, 1, respawnSeconds: 30f, wanderRadius: 0f, activationRadius: 20f);

            _bootstrap = _manager.gameObject.AddComponent<WorldBootstrap>();
            yield return WaitUntil(() =>
            {
                SpawnController controller = marker.GetComponent<SpawnController>();
                return controller != null && controller.AliveCount >= 1;
            }, 300);

            Assert.IsNotNull(marker.GetComponent<SpawnController>(),
                "WorldBootstrap must give each EnemyMarker a spawn controller.");
            Assert.GreaterOrEqual(marker.GetComponent<SpawnController>().AliveCount, 1);
        }

        private IEnumerator StartHostWithPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);
            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player);
        }

        private EnemyMarker CreateMarker(Vector3 position)
        {
            var go = new GameObject("SpawnControllerTestMarker");
            go.transform.position = position;
            _objects.Add(go);
            return go.AddComponent<EnemyMarker>();
        }

        private SpawnController AddController(EnemyMarker marker)
        {
            NetworkObject prefab = FindEnemyPrefab(_manager);
            Assert.IsNotNull(prefab, "The shared manager must have registered the enemy prefab.");

            SpawnController controller = marker.gameObject.AddComponent<SpawnController>();
            controller.Configure(marker, prefab, _manager);
            controller.SetTickInterval(0.05f);
            return controller;
        }

        private static NetworkObject FindEnemyPrefab(NetworkManager manager)
        {
            if (manager == null || manager.SpawnablePrefabs is not SinglePrefabObjects prefabs)
                return null;

            foreach (NetworkObject prefab in prefabs.Prefabs)
            {
                if (prefab != null && prefab.name == TargetDummyPrefabFactory.PrefabName)
                    return prefab;
            }

            return null;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
