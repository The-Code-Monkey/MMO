using System.Collections;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Object;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The world bootstrap over a live host: markers become server-spawned network objects when the
    /// server starts, and a joining player is placed at the spawn point chosen for them. Uses the
    /// shared test manager (<see cref="NetworkManagerTestHelper"/>) and a fresh
    /// <see cref="WorldBootstrap"/> per test, torn down after.
    /// </summary>
    public sealed class WorldBootstrapTests
    {
        private NetworkManager _manager;
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
            {
                Object.Destroy(_bootstrap);
                _bootstrap = null;
            }

            var spawner = _manager.GetComponent<PlayerSpawner>();
            if (spawner != null)
                spawner.Spawns = System.Array.Empty<Transform>();

            foreach (GameObject go in _objects)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            _objects.Clear();
            yield return null;
        }

        private GameObject Create(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            _objects.Add(go);
            return go;
        }

        private PlayerSpawnPoint CreateSpawnPoint(Vector3 position, bool isDefault)
        {
            var point = Create("PlayerSpawnPoint", position).AddComponent<PlayerSpawnPoint>();
            point.SetDefault(isDefault);
            return point;
        }

        private WorldBootstrap AddBootstrap()
        {
            _bootstrap = _manager.gameObject.AddComponent<WorldBootstrap>();
            return _bootstrap;
        }

        private static List<NetworkObject> FindSpawned<T>(NetworkManager manager) where T : Component
        {
            var found = new List<NetworkObject>();
            if (manager == null || !manager.IsServerStarted)
                return found;

            foreach (NetworkObject nob in manager.ServerManager.Objects.Spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.GetComponent<T>() != null)
                    found.Add(nob);
            }

            return found;
        }

        private static bool HasSpawnedAt<T>(NetworkManager manager, Vector3 position, float tolerance = 0.5f)
            where T : Component
        {
            foreach (NetworkObject nob in FindSpawned<T>(manager))
            {
                if (Vector3.Distance(nob.transform.position, position) <= tolerance)
                    return true;
            }

            return false;
        }

        [UnityTest]
        public IEnumerator SpawnWorld_TurnsEveryMarkerIntoANetworkObject()
        {
            Vector3 nodeA = new(-6f, 0.6f, -6f);
            Vector3 nodeB = new(6f, 0.6f, -6f);
            Vector3 nodeC = new(0f, 0.6f, -8f);
            Vector3 station = new(4f, 0.5f, 4f);

            foreach (Vector3 position in new[] { nodeA, nodeB, nodeC })
                Create("ResourceNodeMarker", position).AddComponent<ResourceNodeMarker>();
            Create("CraftingStationMarker", station)
                .AddComponent<CraftingStationMarker>().SetStationType(CraftingStationType.Anvil);

            AddBootstrap();

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            for (int i = 0; i < 120 && _bootstrap.SpawnedWorldObjectCount < 4; i++)
                yield return null;

            Assert.AreEqual(4, _bootstrap.SpawnedWorldObjectCount,
                "Three nodes and a station must all be spawned from their markers.");
            Assert.IsTrue(HasSpawnedAt<ResourceNode>(_manager, nodeA));
            Assert.IsTrue(HasSpawnedAt<ResourceNode>(_manager, nodeB));
            Assert.IsTrue(HasSpawnedAt<ResourceNode>(_manager, nodeC));
            Assert.IsTrue(HasSpawnedAt<CraftingStation>(_manager, station));

            NetworkObject spawnedStation = FindSpawned<CraftingStation>(_manager)
                .Find(n => Vector3.Distance(n.transform.position, station) <= 0.5f);
            Assert.IsNotNull(spawnedStation);
            Assert.AreEqual(CraftingStationType.Anvil, spawnedStation.GetComponent<CraftingStation>().StationType,
                "The marker's station type must reach the spawned station.");
        }

        [UnityTest]
        public IEnumerator SpawnWorld_IsIdempotent()
        {
            Create("ResourceNodeMarker", new Vector3(0f, 0.6f, -6f)).AddComponent<ResourceNodeMarker>();

            WorldBootstrap bootstrap = AddBootstrap();
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            for (int i = 0; i < 120 && bootstrap.SpawnedWorldObjectCount < 1; i++)
                yield return null;

            bootstrap.SpawnWorld();

            Assert.AreEqual(1, FindSpawned<ResourceNode>(_manager).Count,
                "A second spawn pass must not duplicate the node.");
        }

        [UnityTest]
        public IEnumerator Join_PlacesPlayerAtTheZonesDefaultSpawnPoint()
        {
            Vector3 defaultPoint = new(12f, 0.1f, -7f);
            CreateSpawnPoint(defaultPoint, isDefault: true);
            CreateSpawnPoint(new Vector3(-20f, 0.1f, -20f), isDefault: false);

            AddBootstrap();

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);
            Assert.AreEqual(defaultPoint, player.transform.position,
                "A new character with no saved position lands on the zone's default spawn point.");
        }

        [UnityTest]
        public IEnumerator PlacePlayerAt_FarSavedPosition_ClampsToTheNearestPoint()
        {
            CreateSpawnPoint(new Vector3(0f, 0.1f, 0f), isDefault: true);
            Vector3 farPoint = new(30f, 0.1f, 0f);
            CreateSpawnPoint(farPoint, isDefault: false);

            WorldBootstrap bootstrap = AddBootstrap();

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            // A saved position from another zone must clamp to a real spawn point, not drop the
            // player through the floor.
            bootstrap.PlacePlayerAt(player, new Vector3(5000f, 0f, 0f));
            yield return null;

            Assert.AreEqual(farPoint, player.transform.position);
        }

        [UnityTest]
        public IEnumerator PlacePlayerAt_NoSavedPosition_UsesTheDefault()
        {
            Vector3 defaultPoint = new(3f, 0.1f, 3f);
            CreateSpawnPoint(defaultPoint, isDefault: true);
            CreateSpawnPoint(new Vector3(-30f, 0.1f, -30f), isDefault: false);

            WorldBootstrap bootstrap = AddBootstrap();

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            bootstrap.PlacePlayerAt(player, null);
            yield return null;

            Assert.AreEqual(defaultPoint, player.transform.position);
        }

        [UnityTest]
        public IEnumerator SpawnWorld_WiresSpawnPointsIntoThePlayerSpawner()
        {
            CreateSpawnPoint(new Vector3(1f, 0.1f, 1f), isDefault: true);
            CreateSpawnPoint(new Vector3(2f, 0.1f, 2f), isDefault: false);

            AddBootstrap();

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return null;

            var spawner = _manager.GetComponent<PlayerSpawner>();
            Assert.IsNotNull(spawner);
            Assert.AreEqual(2, spawner.Spawns.Length,
                "FishNet's own spawner must know the zone's spawn points.");
        }
    }
}
