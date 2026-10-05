using FishNet.Managing;
using FishNet.Managing.Object;
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
    /// FishNet's NetworkManager, wired to a transport and the join gate.
    ///
    /// FishNet's runtime is not [ExecuteAlways], so NetworkManager.Awake does not run here.
    /// These tests therefore assert what is inspectable without Awake (the saved scene
    /// content); Awake-dependent wiring is proven in the PlayMode factory tests.
    /// </summary>
    public sealed class NetworkManagerSceneBakerTests
    {
        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
                DestroyTree(root);

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
    }
}