using System.Collections;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The end-to-end proof the issue asks for: a real client connects, the join gate admits it,
    /// and the server hands it a player object. Nothing here is mocked — it goes over a live socket
    /// through <see cref="MMOConnectionGate"/> and FishNet's own spawning path.
    ///
    /// The gate is what makes this interesting: FishNet spawns a player when a client finishes
    /// loading its start scenes, which only happens after authentication, so a player object is a
    /// reward for being admitted rather than merely for connecting. Turn-around behaviour — who
    /// gets refused — is covered by the gate's own tests.
    /// </summary>
    public sealed class PlayerSpawnOnConnectTests
    {
        private NetworkManager _manager;
        private GameObject _spawnPoint;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // NetworkManager.Awake runs on the frame the object activates, and JoinRequestClient
            // subscribes in Start, so give the manager a frame before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            // The spawner is shared between tests; clear the points a test set so the next one
            // starts from "no spawn points" rather than a destroyed transform.
            var spawner = _manager.GetComponent<PlayerSpawner>();
            if (spawner != null)
                spawner.Spawns = System.Array.Empty<Transform>();

            if (_spawnPoint != null)
                Object.Destroy(_spawnPoint);

            _spawnPoint = null;

            // Stopping a connection is asynchronous; let it finish so the next test starts from a
            // genuinely offline manager.
            yield return null;
        }

        [Test]
        public void Create_WiresThePlayerPrefabIntoTheManager()
        {
            Assert.IsNotNull(_manager.GetComponent<PlayerSpawner>(),
                "FishNet 4.x has no NetworkManager.PlayerPrefab; a PlayerSpawner is what replaces it.");

            var prefabs = _manager.SpawnablePrefabs as SinglePrefabObjects;
            Assert.IsNotNull(prefabs);
            Assert.AreEqual(1, prefabs.Prefabs.Count,
                "Exactly one spawnable: the player object.");
        }

        [Test]
        public void Create_KeepsTheRuntimePrefabOutOfSight()
        {
            NetworkObject prefab = ((SinglePrefabObjects)_manager.SpawnablePrefabs).Prefabs[0];

            Assert.IsFalse(prefab.gameObject.activeSelf,
                "A prefab built at runtime is a real scene object; it must not render in the world.");
        }

        [Test]
        public void Create_GivesThePrefabAPrefabId()
        {
            NetworkObject prefab = ((SinglePrefabObjects)_manager.SpawnablePrefabs).Prefabs[0];

            // NetworkManager.Awake assigns ids from the list order. Without one the pool cannot
            // resolve the prefab and every spawn returns null.
            Assert.AreNotEqual(NetworkObject.UNSET_PREFABID_VALUE, prefab.PrefabId);
            Assert.AreEqual(0, prefab.PrefabId, "It is the only registered prefab, so it is id 0.");
        }

        [UnityTest]
        public IEnumerator ConnectingClient_IsSpawnedAPlayerObject()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_IsACapsule()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            Assert.IsNotNull(player.GetComponent<CapsuleCollider>(),
                "The player object is the capsule, collider included.");
            Assert.IsNotNull(player.GetComponent<MeshFilter>());
            Assert.IsNotNull(player.GetComponent<MeshRenderer>());
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_CarriesTheProjectPlayerStats()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            var stats = player.GetComponent<PlayerStats>();
            Assert.IsNotNull(stats, "The prefab's networked behaviour has to travel with it.");
            Assert.AreEqual(PlayerStats.MaxHealth, stats.Health,
                "The server initialises stats on spawn; a clone would mean the behaviour never started.");
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_IsOwnedByTheClientThatJoined()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            Assert.IsTrue(player.IsOwner, "Only the owning client may move its own player.");
            Assert.AreEqual(_manager.ClientManager.Connection.ClientId, player.OwnerId);
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_IsSpawnedAndRunning()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            Assert.IsTrue(player.IsSpawned);
            Assert.IsTrue(player.IsServer);
            Assert.IsTrue(player.gameObject.activeInHierarchy,
                "FishNet's pool activates what it spawns; an inactive player is an invisible one.");
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_IsTheOnlyOneTheServerOwns()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            int owned = 0;
            foreach (NetworkObject nob in _manager.ServerManager.Spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner)
                    owned++;
            }

            Assert.AreEqual(1, owned,
                "One client, one player object. A second would mean the spawner ran twice.");
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_SpawnsWhereTheSpawnPointSays()
        {
            _spawnPoint = new GameObject("SpawnPoint");
            _spawnPoint.transform.position = new Vector3(12f, 3f, -7f);

            var spawner = _manager.GetComponent<PlayerSpawner>();
            spawner.Spawns = new[] { _spawnPoint.transform };

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            Assert.AreEqual(_spawnPoint.transform.position, player.transform.position,
                "Spawn points are how a world decides where players appear.");
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_AppearsAtTheOriginWithoutSpawnPoints()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            // No spawn points configured, so FishNet falls back to the prefab's own transform.
            Assert.AreEqual(Vector3.zero, player.transform.position);
        }

        [UnityTest]
        public IEnumerator DisconnectedClient_LosesItsPlayerObject()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            // Unity's == null is true for a destroyed object, which is how a despawned player
            // shows up here.
            Assert.IsTrue(player == null,
                "A player object must not outlive the connection that owned it.");
            Assert.IsNull(NetworkManagerTestHelper.FindPlayerObject(_manager));
        }
    }
}