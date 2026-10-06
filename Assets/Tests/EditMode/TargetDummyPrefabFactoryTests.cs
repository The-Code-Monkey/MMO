using System;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The enemy the issue asks for: a red capsule FishNet can track, carrying the project's
    /// networked health, wired to the NetworkManager as a server-spawned entity.
    ///
    /// Asserted on real components rather than doubles, because every failure this guards against —
    /// a capsule without a NetworkObject, a prefab that never reached the spawnable collection, a
    /// spawner pointing at nothing — is completely silent at runtime.
    /// </summary>
    public sealed class TargetDummyPrefabFactoryTests
    {
        private readonly List<GameObject> _sceneObjects = new();
        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _scratch.Clear();
            _sceneObjects.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _sceneObjects.Add(go);
            return go;
        }

        private NetworkObject NewTargetDummy() => TargetDummyPrefabFactory.Create();

        private SinglePrefabObjects NewSpawnables()
        {
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "TestSpawnablePrefabs";
            _scratch.Add(prefabs);
            return prefabs;
        }

        private NetworkManager NewNetworkManager(string name = "TestNetworkManager")
        {
            GameObject go = Track(new GameObject(name));
            return go.AddComponent<NetworkManager>();
        }

        #region The object itself.
        [Test]
        public void Create_CarriesANetworkObject()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<NetworkObject>(),
                "Without a NetworkObject FishNet has nothing to track across the network.");
        }

        [Test]
        public void Create_MakesACapsule()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            var collider = nob.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(collider, "CreatePrimitive(Capsule) must leave a CapsuleCollider behind.");
            Assert.AreEqual(1, collider.direction, "A capsule stands along the Y axis by default.");
        }

        [Test]
        public void Create_GivesTheCapsuleItsDeclaredSize()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            var collider = nob.GetComponent<CapsuleCollider>();
            Assert.AreEqual(TargetDummyPrefabFactory.DefaultHeight, collider.height, 0.0001f);
            Assert.AreEqual(TargetDummyPrefabFactory.DefaultRadius, collider.radius, 0.0001f);
        }

        [Test]
        public void Create_MakesTheCapsuleVisible()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            var filter = nob.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "A capsule nobody can see is not an enemy.");
            Assert.IsNotNull(nob.GetComponent<MeshRenderer>(), "A capsule without a renderer is invisible.");
            Assert.IsNotNull(filter.sharedMesh, "The primitive must keep its capsule mesh.");
        }

        [Test]
        public void Create_IsRed()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Material material = nob.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (material == null)
            {
                Assert.Ignore("No material could be resolved in this environment.");
                return;
            }

            Color colour = ReadColour(material);
            Assert.AreEqual(TargetDummyPrefabFactory.RedColor, colour,
                "Red is how a target reads as an enemy against a player-shaped capsule.");
        }

        [Test]
        public void Create_AddsTheProjectsNetworkedEnemyStats()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<NPCStats>(),
                "NPCStats is the project's own networked component and belongs on the enemy object.");
        }

        [Test]
        public void Create_PutsTheStatsNextToTheNetworkObject()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            NetworkBehaviour[] behaviours = nob.GetComponents<NetworkBehaviour>();
            Assert.AreEqual(1, behaviours.Length, "Only the project's own behaviour belongs here.");
            Assert.IsInstanceOf<NPCStats>(behaviours[0]);
            Assert.AreSame(nob.gameObject, behaviours[0].gameObject,
                "The stats and the NetworkObject must be on the same object.");
        }

        [Test]
        public void Create_NamesTheObjectTargetDummy()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(TargetDummyPrefabFactory.PrefabName, nob.name);
        }

        [Test]
        public void Create_LeavesTheObjectAtTheOriginFacingForward()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(Vector3.zero, nob.transform.position);
            Assert.AreEqual(Quaternion.identity, nob.transform.rotation);
        }

        [Test]
        public void Create_LeavesTheObjectActiveSoItCanBeSavedAsAPrefab()
        {
            NetworkObject nob = TargetDummyPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsTrue(nob.gameObject.activeSelf,
                "An inactive root is saved into the prefab greyed out; deactivate at runtime instead.");
        }

        [Test]
        public void Create_BuildsIndependentInstances()
        {
            NetworkObject first = NewTargetDummy();
            Track(first.gameObject);
            NetworkObject second = NewTargetDummy();
            Track(second.gameObject);

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.gameObject, second.gameObject);
        }
        #endregion

        #region Spawnable registration.
        [Test]
        public void Register_AddsThePrefabToTheCollection()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            Assert.IsTrue(TargetDummyPrefabFactory.Register(prefabs, targetDummy));

            Assert.AreEqual(1, prefabs.Prefabs.Count);
            Assert.AreSame(targetDummy, prefabs.Prefabs[0],
                "FishNet derives prefab ids from list order, so the dummy has to be in there.");
        }

        [Test]
        public void Register_LeavesPrefabIdsToTheNetworkManager()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            TargetDummyPrefabFactory.Register(prefabs, targetDummy);

            Assert.AreEqual(NetworkObject.UNSET_PREFABID_VALUE, targetDummy.PrefabId);
        }

        [Test]
        public void Register_IsIdempotent()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            Assert.IsTrue(TargetDummyPrefabFactory.Register(prefabs, targetDummy));
            Assert.IsFalse(TargetDummyPrefabFactory.Register(prefabs, targetDummy),
                "A second register would shift every prefab id by one on both ends.");
            Assert.AreEqual(1, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_KeepsThePlayerPrefabInFront()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject player = new GameObject("Player").AddComponent<NetworkObject>();
            Track(player.gameObject);
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            // The player is always registered first so it keeps prefab id 0 on every end.
            PlayerPrefabFactory.Register(prefabs, player);
            TargetDummyPrefabFactory.Register(prefabs, targetDummy);

            Assert.AreEqual(2, prefabs.Prefabs.Count);
            Assert.AreSame(player, prefabs.Prefabs[0]);
            Assert.AreSame(targetDummy, prefabs.Prefabs[1]);
        }

        [Test]
        public void Register_IgnoresANullPrefab()
        {
            SinglePrefabObjects prefabs = NewSpawnables();

            Assert.IsFalse(TargetDummyPrefabFactory.Register(prefabs, null));
            Assert.AreEqual(0, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_RejectsANullCollection()
        {
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            Assert.Throws<ArgumentNullException>(() => TargetDummyPrefabFactory.Register(null, targetDummy));
        }

        [Test]
        public void Contains_ReportsWhetherThePrefabIsRegistered()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);
            NetworkObject stranger = NewTargetDummy();
            Track(stranger.gameObject);

            Assert.IsFalse(TargetDummyPrefabFactory.Contains(prefabs, targetDummy));

            TargetDummyPrefabFactory.Register(prefabs, targetDummy);

            Assert.IsTrue(TargetDummyPrefabFactory.Contains(prefabs, targetDummy));
            Assert.IsFalse(TargetDummyPrefabFactory.Contains(prefabs, stranger));
        }

        [Test]
        public void Contains_IsFalseForNulls()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);
            TargetDummyPrefabFactory.Register(prefabs, targetDummy);

            Assert.IsFalse(TargetDummyPrefabFactory.Contains(prefabs, null));
            Assert.IsFalse(TargetDummyPrefabFactory.Contains(null, targetDummy));
        }
        #endregion

        #region TargetDummySpawner wiring.
        [Test]
        public void Attach_PutsTheSpawnerOnTheNetworkManagerObject()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            TargetDummySpawner spawner = TargetDummyPrefabFactory.Attach(manager, targetDummy);

            Assert.AreSame(manager.gameObject, spawner.gameObject);
        }

        [Test]
        public void Attach_PointsTheSpawnerAtThePrefab()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            TargetDummySpawner spawner = TargetDummyPrefabFactory.Attach(manager, targetDummy);

            Assert.AreSame(targetDummy, spawner.Prefab,
                "This is the whole point of the wiring: the server has to know which prefab to spawn.");
        }

        [Test]
        public void Attach_AddsOnlyOneSpawner()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            TargetDummyPrefabFactory.Attach(manager, targetDummy);
            TargetDummyPrefabFactory.Attach(manager, targetDummy);

            Assert.AreEqual(1, manager.gameObject.GetComponents<TargetDummySpawner>().Length,
                "Two spawners would put two enemies in the world when the server starts.");
        }

        [Test]
        public void Attach_ReusesAnExistingSpawner()
        {
            NetworkManager manager = NewNetworkManager();
            TargetDummySpawner existing = manager.gameObject.AddComponent<TargetDummySpawner>();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            Assert.AreSame(existing, TargetDummyPrefabFactory.Attach(manager, targetDummy));
        }

        [Test]
        public void Attach_KeepsTheCurrentPrefabWhenGivenNone()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);
            TargetDummySpawner spawner = TargetDummyPrefabFactory.Attach(manager, targetDummy);

            Assert.IsNotNull(TargetDummyPrefabFactory.Attach(manager, null));
            Assert.AreSame(targetDummy, spawner.Prefab,
                "Rebuilding the manager must not silently blank out the spawner.");
        }

        [Test]
        public void Attach_RejectsANullManager()
        {
            NetworkObject targetDummy = NewTargetDummy();
            Track(targetDummy.gameObject);

            Assert.Throws<ArgumentNullException>(() => TargetDummyPrefabFactory.Attach(null, targetDummy));
        }
        #endregion

        /// <summary>Reads the material's base colour whichever shader property owns it.</summary>
        private static Color ReadColour(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.color;

            Assert.Ignore("The resolved shader exposes no colour property to assert.");
            return default;
        }
    }
}