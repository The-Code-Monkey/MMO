using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using MMO.Editor;
using MMO.Net;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// Proves the issue's ask literally: a root GameObject named "NetworkManager" carrying
    /// FishNet's NetworkManager, wired to a transport, the join gate, and the player object that
    /// every admitted client is handed.
    ///
    /// FishNet's runtime is not [ExecuteAlways], so NetworkManager.Awake does not run here.
    /// These tests therefore assert what is inspectable without Awake (the saved scene
    /// content); Awake-dependent wiring is proven in the PlayMode factory tests.
    /// </summary>
    public sealed class NetworkManagerSceneBakerTests
    {
        private Scene _scene;
        private SpawnablePrefabTestAsset _prefabAsset;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _prefabAsset = new SpawnablePrefabTestAsset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                DestroyTree(root);

            _prefabAsset?.Cleanup();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void DestroyTree(GameObject root)
        {
            foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
            {
                if (manager.SpawnablePrefabs != null)
                    Object.DestroyImmediate(manager.SpawnablePrefabs);
            }

            Object.DestroyImmediate(root);
        }

        [Test]
        public void CreateInActiveScene_CreatesGameObjectNamed_NetworkManager()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNotNull(GameObject.Find(NetworkManagerFactory.GameObjectName),
                "Expected a GameObject literally named \"NetworkManager\".");
        }

        [Test]
        public void CreateInActiveScene_PutsTheObjectAtTheSceneRoot()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNull(manager.transform.parent);
            Assert.AreEqual(1, _scene.GetRootGameObjects().Length);
        }

        [Test]
        public void CreateInActiveScene_AddsFishNetNetworkManagerComponent()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNotNull(manager);
            Assert.AreEqual(manager.gameObject, GameObject.Find(NetworkManagerFactory.GameObjectName));
        }

        [Test]
        public void CreateInActiveScene_AssignsSpawnablePrefabs()
        {
            // Awake dereferences this; a null here is the classic "NetworkManager does
            // nothing at runtime" bug.
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNotNull(manager.SpawnablePrefabs);
            Assert.IsInstanceOf<PrefabObjects>(manager.SpawnablePrefabs);
            Assert.AreEqual(NetworkManagerFactory.SpawnablePrefabsName, manager.SpawnablePrefabs.name);
        }

        [Test]
        public void CreateInActiveScene_AddsTugboatConfiguredFromSettings()
        {
            var settings = new MMOConnectionSettings(port: 7979, maxClients: 12, handshakeTimeoutSeconds: 5f);

            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(settings);

            var tugboat = manager.gameObject.GetComponent<Tugboat>();
            Assert.IsNotNull(tugboat, "Tugboat is FishNet's default transport and must already be present.");
            Assert.AreEqual(settings.Port, tugboat.GetPort());
            Assert.AreEqual(settings.MaxClients, tugboat.GetMaximumClients());
        }

        [Test]
        public void CreateInActiveScene_AddsTheConnectionGate()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNotNull(manager.gameObject.GetComponent<MMOConnectionGate>(),
                "The gate is only consulted if it exists before NetworkManager.Awake.");
        }

        [Test]
        public void CreateInActiveScene_AddsTheClientHalfOfTheHandshake()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNotNull(manager.gameObject.GetComponent<JoinRequestClient>(),
                "Without this a client can never announce itself and the gate would reap everyone.");
        }

        [Test]
        public void CreateInActiveScene_HandsTheSettingsToTheGate()
        {
            var settings = new MMOConnectionSettings(1234, 3, 1.5f, maintenanceMode: true);

            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(settings);

            var gate = manager.gameObject.GetComponent<MMOConnectionGate>();
            Assert.AreSame(settings, gate.Settings);
            Assert.IsTrue(gate.Settings.MaintenanceMode);
            Assert.AreEqual(3, gate.Settings.MaxClients);
        }

        [Test]
        public void CreateInActiveScene_IsIdempotent()
        {
            NetworkManager first = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            NetworkManager second = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.AreSame(first, second, "A second run must reuse the object.");
            Assert.AreEqual(1, _scene.GetRootGameObjects().Length);
        }

        [Test]
        public void CreateInActiveScene_NullSettings_StillBuildsADefaultObject()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(null);

            var tugboat = manager.gameObject.GetComponent<Tugboat>();
            Assert.AreEqual(MMOConnectionSettings.DefaultPort, tugboat.GetPort());
            Assert.AreEqual(MMOConnectionSettings.DefaultMaxClients, tugboat.GetMaximumClients());
        }

        [Test]
        public void TryFind_FindsTheBakedObject()
        {
            NetworkManager created = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsTrue(NetworkManagerSceneBaker.TryFind(_scene, out NetworkManager found));
            Assert.AreSame(created, found);
        }

        [Test]
        public void TryFind_MissesAnEmptyScene()
        {
            Assert.IsFalse(NetworkManagerSceneBaker.TryFind(_scene, out NetworkManager found));
            Assert.IsNull(found);
        }

        [Test]
        public void TryFind_MissesAnUnrelatedGameObject()
        {
            new GameObject("SomeOtherObject");

            Assert.IsFalse(NetworkManagerSceneBaker.TryFind(_scene, out _));
        }

        [Test]
        public void TryFind_MissesANameOnlyLookalike()
        {
            // Right name, no FishNet NetworkManager: still not a NetworkManager object.
            new GameObject(NetworkManagerFactory.GameObjectName);

            Assert.IsFalse(NetworkManagerSceneBaker.TryFind(_scene, out _));
        }

        [Test]
        public void TryFind_MissesANestedManager()
        {
            var parent = new GameObject("Parent");
            NetworkManager nested = NetworkManagerFactory.Create(new MMOConnectionSettings(), out _);
            nested.transform.SetParent(parent.transform);

            Assert.IsFalse(NetworkManagerSceneBaker.TryFind(_scene, out _),
                "The object has to be a root named NetworkManager so the rules stay discoverable.");
        }

        [Test]
        public void Validate_Fails_ForSceneWithoutNetworkManager()
        {
            Assert.IsFalse(NetworkManagerSceneBaker.Validate(_scene, out string error));
            StringAssert.Contains(NetworkManagerFactory.GameObjectName, error);
            StringAssert.Contains(NetworkManagerSceneBaker.MenuPath, error);
        }

        [Test]
        public void Validate_Fails_ForNameOnlyLookalike()
        {
            new GameObject(NetworkManagerFactory.GameObjectName);

            Assert.IsFalse(NetworkManagerSceneBaker.Validate(_scene, out string error));
            StringAssert.Contains(NetworkManagerFactory.GameObjectName, error);
        }

        [Test]
        public void Validate_Passes_AfterBaking()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsTrue(NetworkManagerSceneBaker.Validate(_scene, out string error), error);
            Assert.IsNull(error);
        }

        [Test]
        public void CreateInActiveScene_BakesThePlayerPrefabIntoTheProject()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            // The scene must reference a reusable template, not carry a capsule of its own.
            Assert.IsNotNull(PlayerPrefabBaker.LoadPlayerPrefab(),
                "The player object has to exist as a prefab asset in the project.");
        }

        [Test]
        public void CreateInActiveScene_RegistersThePlayerPrefabAsASpawnable()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsTrue(PlayerPrefabFactory.Contains(manager.SpawnablePrefabs, PlayerPrefabBaker.LoadPlayerPrefab()),
                "An unregistered prefab has no id, so no client could ever instantiate it.");
        }

        [Test]
        public void CreateInActiveScene_RegistersPlayerThenDummyInOrder()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            var single = manager.SpawnablePrefabs as SinglePrefabObjects;
            Assert.IsNotNull(single);
            Assert.AreEqual(6, single.Prefabs.Count,
                "FishNet derives prefab ids from list order, so duplicates shift ids on one end only. " +
                "The order is player (0), target dummy (1), crafting station (2), loot bag (3), " +
                "resource node (4), bank teller (5).");
            Assert.AreSame(PlayerPrefabBaker.LoadPlayerPrefab(), single.Prefabs[0],
                "The player must keep id 0 so connecting clients spawn the object they expect.");
            Assert.AreSame(TargetDummyBaker.LoadTargetDummyPrefab(), single.Prefabs[1],
                "The target dummy comes second: every end registers the player first, then the dummy.");
            Assert.AreSame(ResourceNodeBaker.LoadResourceNodePrefab(), single.Prefabs[4],
                "The resource node is registered after the loot bag, on every end.");
            Assert.AreSame(LootBagBaker.LoadLootBagPrefab(), single.Prefabs[3],
                "The loot bag keeps id 3 so a full-loot death has a prefab id both ends agree on.");
            Assert.AreSame(BankTellerBaker.LoadBankTellerPrefab(), single.Prefabs[5],
                "The bank teller is last, after every prefab authored before it keeps its id.");
        }

        [Test]
        public void CreateInActiveScene_PointsTheSpawnableAtTheAssetNotASceneCopy()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            var single = (SinglePrefabObjects)manager.SpawnablePrefabs;
            Assert.IsTrue(UnityEditor.EditorUtility.IsPersistent(single.Prefabs[0]),
                "A scene copy would be serialized into the scene instead of referenced as a prefab.");
        }

        [Test]
        public void CreateInActiveScene_AddsAPlayerSpawnerForNewConnections()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            PlayerSpawner spawner = manager.gameObject.GetComponent<PlayerSpawner>();

            // FishNet 4.x has no NetworkManager.PlayerPrefab; this component is what tells it to
            // spawn a player object when a client connects.
            Assert.IsNotNull(spawner);
            Assert.AreSame(manager.gameObject, spawner.gameObject,
                "PlayerSpawner looks for the NetworkManager with GetComponentInParent.");
        }

        [Test]
        public void CreateInActiveScene_PointsThePlayerSpawnerAtThePrefab()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.AreSame(PlayerPrefabBaker.LoadPlayerPrefab(), SpawnedPrefabOf(manager),
                "This is the whole point of the wiring: FishNet has to know which prefab to spawn.");
        }

        [Test]
        public void CreateInActiveScene_BakesTheTargetDummyPrefabIntoTheProject()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            // The dummy must be a reusable template, not a capsule carried by the scene.
            Assert.IsNotNull(TargetDummyBaker.LoadTargetDummyPrefab(),
                "The enemy object has to exist as a prefab asset in the project.");
        }

        [Test]
        public void CreateInActiveScene_RegistersTheTargetDummyAsASpawnable()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsTrue(
                TargetDummyPrefabFactory.Contains(manager.SpawnablePrefabs,
                    TargetDummyBaker.LoadTargetDummyPrefab()),
                "An unregistered prefab has no id, so a client could never instantiate the enemy.");
        }

        [Test]
        public void CreateInActiveScene_DoesNotAddAFixedTargetDummySpawner()
        {
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.IsNull(manager.gameObject.GetComponent<TargetDummySpawner>(),
                "Enemy populations come from EnemyMarker spawn controllers, not a fixed dummy spawner.");
        }

        [Test]
        public void CreateInActiveScene_LeavesNoTargetDummyCapsuleInTheScene()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                Assert.AreNotEqual(TargetDummyPrefabFactory.PrefabName, root.name,
                    "The scene keeps the prefab reference; the capsule itself belongs to the asset.");
            }
        }

        [Test]
        public void CreateInActiveScene_DoesNotDuplicateThePlayerWiring()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());
            NetworkManager manager = NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.AreEqual(1, manager.gameObject.GetComponents<PlayerSpawner>().Length);
            Assert.AreEqual(0, manager.gameObject.GetComponents<TargetDummySpawner>().Length);
            Assert.AreEqual(6, ((SinglePrefabObjects)manager.SpawnablePrefabs).Prefabs.Count);
        }

        [Test]
        public void CreateInActiveScene_LeavesNoPlayerCapsuleInTheScene()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                Assert.AreNotEqual(PlayerPrefabFactory.PrefabName, root.name,
                    "The scene keeps the prefab reference; the capsule itself belongs to the asset.");
            }
        }

        [Test]
        public void CreateInActiveScene_AddsExactlyOneRootObject()
        {
            NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            Assert.AreEqual(1, _scene.GetRootGameObjects().Length);
        }

        /// <summary>FishNet exposes no getter for PlayerSpawner's prefab, so read the field.</summary>
        private static NetworkObject SpawnedPrefabOf(NetworkManager manager)
        {
            var spawner = manager.gameObject.GetComponent<PlayerSpawner>();
            Assert.IsNotNull(spawner, "Expected a PlayerSpawner on the NetworkManager object.");

            var field = typeof(PlayerSpawner).GetField(
                "_playerPrefab",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(field, "FishNet renamed PlayerSpawner's prefab field.");
            return (NetworkObject)field.GetValue(spawner);
        }
    }
}