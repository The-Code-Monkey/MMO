using System.Collections;
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
    /// The end-to-end proof the issue asks for: the dummy is a <em>server-spawned</em> entity. It is
    /// not in any scene, the server instantiates and spawns it the moment it starts listening, and
    /// every client sees it in the exact same state because it only ever exists through that spawn
    /// path.
    ///
    /// Nothing here is mocked — the dummy goes through the real socket, FishNet's spawning path,
    /// and the project's own <see cref="TargetDummySpawner"/>.
    /// </summary>
    public sealed class TargetDummySpawnTests
    {
        private NetworkManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // NetworkManager.Awake runs on the frame the object activates and the spawner subscribes
            // in OnEnable, so give the manager a frame before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            // Stopping a connection is asynchronous; let it finish so the next test starts from a
            // genuinely offline manager.
            yield return null;
        }

        [Test]
        public void Factory_AddsOneTargetDummySpawnerToTheManager()
        {
            TargetDummySpawner[] spawners = _manager.gameObject.GetComponents<TargetDummySpawner>();

            Assert.AreEqual(1, spawners.Length,
                "Exactly one spawner, or the world gets more than one enemy per server.");
            Assert.IsNotNull(spawners[0].Prefab,
                "The spawner is only useful once it knows which prefab to instantiate.");
        }

        [UnityTest]
        public IEnumerator ServerStart_SpawnsTheTargetDummy()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            Assert.AreEqual(1, NetworkManagerTestHelper.FindTargetDummies(_manager).Count,
                "The server must instantiate and spawn exactly one target dummy when it starts.");
        }

        [UnityTest]
        public IEnumerator TargetDummy_IsACapsule()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NetworkObject dummy = NetworkManagerTestHelper.FindTargetDummies(_manager)[0];

            Assert.IsNotNull(dummy.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(dummy.GetComponent<MeshFilter>());
            Assert.IsNotNull(dummy.GetComponent<MeshRenderer>());
        }

        [UnityTest]
        public IEnumerator TargetDummy_CarriesNetworkedStatsAtFullHealth()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NPCStats stats = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();

            Assert.IsNotNull(stats, "The prefab's networked behaviour has to travel with it.");
            Assert.AreEqual(NPCStats.MaxHealth, stats.Health,
                "The server fills health on spawn; zero would mean the reset never ran.");
        }

        [UnityTest]
        public IEnumerator ServerTakingDamage_LowersTheDummiesHealth()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NPCStats stats = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();

            stats.TakeDamage(20);

            Assert.AreEqual(NPCStats.MaxHealth - 20, stats.Health);
        }

        [UnityTest]
        public IEnumerator TargetDummy_IsServerSpawnedAndNotOwnedByAClient()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NetworkObject dummy = NetworkManagerTestHelper.FindTargetDummies(_manager)[0];

            Assert.IsTrue(dummy.IsSpawned);
            Assert.IsTrue(dummy.IsServer);
            Assert.AreEqual(-1, dummy.OwnerId,
                "A world NPC belongs to nobody; a client owning it would be able to drive it.");
            Assert.IsFalse(dummy.IsOwner);
        }

        [UnityTest]
        public IEnumerator TargetDummy_SpawnsAtTheConfiguredPosition()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NetworkObject dummy = NetworkManagerTestHelper.FindTargetDummies(_manager)[0];

            Assert.AreEqual(TargetDummyPrefabFactory.DefaultSpawnPosition, dummy.transform.position,
                "The spawner decides where the enemy stands, not the prefab.");
        }

        [UnityTest]
        public IEnumerator TargetDummy_IsRegisteredAsSpawnableIdOne()
        {
            var prefabs = (SinglePrefabObjects)_manager.SpawnablePrefabs;

            Assert.AreEqual(TargetDummyPrefabFactory.PrefabName, prefabs.Prefabs[1].name);
            // NetworkManager.Awake assigns ids from list order. Without one the pool cannot resolve
            // the prefab and every spawn returns null.
            Assert.AreEqual(1, prefabs.Prefabs[1].PrefabId,
                "The player keeps id 0, so the dummy must be id 1 on every end.");
        }

        [UnityTest]
        public IEnumerator StoppingTheServer_RemovesTheTargetDummy()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            NetworkObject dummy = NetworkManagerTestHelper.FindTargetDummies(_manager)[0];

            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            // Unity's == null is true for a destroyed object, which is how a despawned dummy
            // shows up here.
            Assert.IsTrue(dummy == null,
                "A server-spawned enemy must not outlive the server that spawned it.");
            Assert.AreEqual(0, NetworkManagerTestHelper.FindTargetDummies(_manager).Count);
        }

        [UnityTest]
        public IEnumerator ServerRestart_SpawnsASingleFreshDummy()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);
            Assert.AreEqual(1, NetworkManagerTestHelper.FindTargetDummies(_manager).Count);

            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);

            Assert.AreEqual(1, NetworkManagerTestHelper.FindTargetDummies(_manager).Count,
                "Restarting must not accumulate enemies.");
        }
    }
}