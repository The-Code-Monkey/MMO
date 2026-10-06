using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The player object the issue asks for: a capsule FishNet can track and update, plus the
    /// wiring that makes the server hand one to every client that connects.
    ///
    /// Asserted on real components rather than doubles, because every failure this guards against —
    /// a capsule without a NetworkObject, a prefab that never reached the spawnable collection, a
    /// PlayerSpawner pointing at nothing — is completely silent at runtime.
    /// </summary>
    public sealed class PlayerPrefabFactoryTests
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

        private NetworkObject NewPlayerPrefab() => PlayerPrefabFactory.Create();

        private SinglePrefabObjects NewSpawnables()
        {
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "TestSpawnablePrefabs";
            _scratch.Add(prefabs);
            return prefabs;
        }

        /// <summary>
        /// FishNet keeps the spawner's prefab private and offers no getter for it, so the only way
        /// to assert the wiring is to read the field.
        /// </summary>
        private static NetworkObject PrefabOf(PlayerSpawner spawner)
        {
            FieldInfo field = typeof(PlayerSpawner).GetField(
                "_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "FishNet renamed PlayerSpawner's prefab field.");
            return (NetworkObject)field.GetValue(spawner);
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
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<NetworkObject>(),
                "Without a NetworkObject FishNet has nothing to track across the network.");
        }

        [Test]
        public void Create_MakesACapsule()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            var collider = nob.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(collider, "CreatePrimitive(Capsule) must leave a CapsuleCollider behind.");
            Assert.AreEqual(1, collider.direction, "A capsule stands along the Y axis by default.");
        }

        [Test]
        public void Create_GivesTheCapsuleItsDeclaredSize()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            var collider = nob.GetComponent<CapsuleCollider>();
            Assert.AreEqual(PlayerPrefabFactory.DefaultHeight, collider.height, 0.0001f);
            Assert.AreEqual(PlayerPrefabFactory.DefaultRadius, collider.radius, 0.0001f);
        }

        [Test]
        public void Create_ScalesThePrimitiveSoTheWorldSizeIsTheDeclaredSize()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            // The primitive's collider is authored in local units, so the transform scale is what
            // turns its 0.5 radius / 2 height default into the declared dimensions.
            Assert.AreEqual(PlayerPrefabFactory.DefaultScale, nob.transform.localScale);
        }

        [Test]
        public void Create_DefaultsToHumanSizedProportions()
        {
            // Pins the default numbers themselves: a 2m x 1m capsule, and a scale that leaves
            // Unity's own primitive untouched, so the mesh and collider start out in agreement.
            Assert.AreEqual(2f, PlayerPrefabFactory.DefaultHeight, 0.0001f);
            Assert.AreEqual(0.5f, PlayerPrefabFactory.DefaultRadius, 0.0001f);
            Assert.AreEqual(Vector3.one, PlayerPrefabFactory.DefaultScale);
        }

        [Test]
        public void Create_MakesTheCapsuleVisible()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            var filter = nob.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "A capsule nobody can see is not a player object.");
            Assert.IsNotNull(nob.GetComponent<MeshRenderer>(), "A capsule without a renderer is invisible.");
            Assert.IsNotNull(filter.sharedMesh, "The primitive must keep its capsule mesh.");
        }

        [Test]
        public void Create_KeepsTheMeshAndTheColliderTheSameSize()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            // A mesh and collider that disagree are how a player object ends up looking like it is
            // somewhere it is not.
            var collider = nob.GetComponent<CapsuleCollider>();
            Bounds mesh = nob.GetComponent<MeshFilter>().sharedMesh.bounds;
            const float tolerance = 0.05f;

            Assert.AreEqual(collider.radius * 2f, mesh.size.x, tolerance);
            Assert.AreEqual(collider.radius * 2f, mesh.size.z, tolerance);
            Assert.AreEqual(collider.height, mesh.size.y, tolerance);
        }

        [Test]
        public void Create_NamesTheObjectPlayer()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(PlayerPrefabFactory.PrefabName, nob.name);
        }

        [Test]
        public void Create_TagsTheObjectPlayer()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(PlayerPrefabFactory.PlayerTag, nob.gameObject.tag);
        }

        [Test]
        public void Create_AddsTheProjectsNetworkedStats()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<PlayerStats>(),
                "PlayerStats is the project's own networked component and belongs on the player object.");
        }

        [Test]
        public void Create_AddsTheProjectsInventoryContainer()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<PlayerInventory>(),
                "PlayerInventory owns the bag and equipment containers and belongs beside the stats.");
        }

        [Test]
        public void Create_PutsTheStatsNextToTheNetworkObject()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            // A NetworkBehaviour resolves its NetworkObject through its own GameObject; a mismatch
            // is the classic "stats never replicate" bug. FishNet also wires SyncTypes in
            // NetworkBehaviour-index order, so both of the project's behaviours belong here —
            // in the order the factory adds them.
            NetworkBehaviour[] behaviours = nob.GetComponents<NetworkBehaviour>();
            Assert.AreEqual(2, behaviours.Length, "Exactly the project's two behaviours belong here.");
            Assert.IsInstanceOf<PlayerStats>(behaviours[0]);
            Assert.IsInstanceOf<PlayerInventory>(behaviours[1]);
            Assert.AreSame(nob.gameObject, behaviours[0].gameObject,
                "The stats and the NetworkObject must be on the same object.");
            Assert.AreSame(nob.gameObject, behaviours[1].gameObject,
                "The inventory and the NetworkObject must be on the same object.");
        }

        [Test]
        public void Create_LeavesTheObjectAtTheOriginFacingForward()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(Vector3.zero, nob.transform.position);
            Assert.AreEqual(Quaternion.identity, nob.transform.rotation);
        }

        [Test]
        public void Create_LeavesTheObjectActiveSoItCanBeSavedAsAPrefab()
        {
            NetworkObject nob = PlayerPrefabFactory.Create();
            Track(nob.gameObject);

            Assert.IsTrue(nob.gameObject.activeSelf,
                "An inactive root is saved into the prefab greyed out; deactivate at runtime instead.");
        }

        [Test]
        public void Create_BuildsIndependentInstances()
        {
            NetworkObject first = NewPlayerPrefab();
            Track(first.gameObject);
            NetworkObject second = NewPlayerPrefab();
            Track(second.gameObject);

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.gameObject, second.gameObject);
        }

        [Test]
        public void Create_UsesTheActiveRenderPipelineMaterial()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.defaultMaterial == null)
                Assert.Ignore("No active render pipeline with a default material.");

            NetworkObject nob = NewPlayerPrefab();
            Track(nob.gameObject);

            // Without this a capsule in a URP project renders magenta.
            Assert.AreSame(pipeline.defaultMaterial, nob.GetComponent<MeshRenderer>().sharedMaterial);
        }
        #endregion

        #region Spawnable registration.
        [Test]
        public void Register_AddsThePrefabToTheCollection()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            Assert.IsTrue(PlayerPrefabFactory.Register(prefabs, playerPrefab));

            Assert.AreEqual(1, prefabs.Prefabs.Count);
            Assert.AreSame(playerPrefab, prefabs.Prefabs[0],
                "FishNet derives prefab ids from list order, so the player prefab has to be in there.");
        }

        [Test]
        public void Register_LeavesPrefabIdsToTheNetworkManager()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerPrefabFactory.Register(prefabs, playerPrefab);

            // A prefab id means nothing until NetworkManager.Awake sees the final list, and
            // FishNet logs an error if values are initialized outside play mode.
            Assert.AreEqual(NetworkObject.UNSET_PREFABID_VALUE, playerPrefab.PrefabId);
        }

        [Test]
        public void Register_IsIdempotent()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            Assert.IsTrue(PlayerPrefabFactory.Register(prefabs, playerPrefab));
            Assert.IsFalse(PlayerPrefabFactory.Register(prefabs, playerPrefab),
                "A second register would shift every prefab id by one on both ends.");
            Assert.AreEqual(1, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_KeepsEarlierPrefabsInPlace()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject other = NewPlayerPrefab();
            Track(other.gameObject);
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerPrefabFactory.Register(prefabs, other);
            PlayerPrefabFactory.Register(prefabs, playerPrefab);

            Assert.AreEqual(2, prefabs.Prefabs.Count);
            Assert.AreSame(other, prefabs.Prefabs[0]);
            Assert.AreSame(playerPrefab, prefabs.Prefabs[1]);
        }

        [Test]
        public void Register_IgnoresANullPrefab()
        {
            SinglePrefabObjects prefabs = NewSpawnables();

            Assert.IsFalse(PlayerPrefabFactory.Register(prefabs, null));
            Assert.AreEqual(0, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_RejectsANullCollection()
        {
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            Assert.Throws<ArgumentNullException>(() => PlayerPrefabFactory.Register(null, playerPrefab));
        }

        [Test]
        public void Contains_ReportsWhetherThePrefabIsRegistered()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);
            NetworkObject stranger = NewPlayerPrefab();
            Track(stranger.gameObject);

            Assert.IsFalse(PlayerPrefabFactory.Contains(prefabs, playerPrefab));

            PlayerPrefabFactory.Register(prefabs, playerPrefab);

            Assert.IsTrue(PlayerPrefabFactory.Contains(prefabs, playerPrefab));
            Assert.IsFalse(PlayerPrefabFactory.Contains(prefabs, stranger));
        }

        [Test]
        public void Contains_IsFalseForNulls()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);
            PlayerPrefabFactory.Register(prefabs, playerPrefab);

            Assert.IsFalse(PlayerPrefabFactory.Contains(prefabs, null));
            Assert.IsFalse(PlayerPrefabFactory.Contains(null, playerPrefab));
        }

        [Test]
        public void Contains_IsFalseForACollectionThisProjectDoesNotUse()
        {
            var dual = ScriptableObject.CreateInstance<DualPrefabObjects>();
            dual.name = "TestDualSpawnablePrefabs";
            _scratch.Add(dual);

            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            // SinglePrefabObjects is the only list searched; the base class exposes no way to
            // enumerate its contents, so anything else answers "no" rather than guessing.
            Assert.IsFalse(PlayerPrefabFactory.Contains(dual, playerPrefab));
        }
        #endregion

        #region PlayerSpawner wiring.
        [Test]
        public void Attach_PutsTheSpawnerOnTheNetworkManagerObject()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerSpawner spawner = PlayerPrefabFactory.Attach(manager, playerPrefab);

            // PlayerSpawner resolves its manager with GetComponentInParent, so it has to live on
            // the NetworkManager object or one of its children.
            Assert.AreSame(manager.gameObject, spawner.gameObject);
        }

        [Test]
        public void Attach_PointsTheSpawnerAtThePlayerPrefab()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerSpawner spawner = PlayerPrefabFactory.Attach(manager, playerPrefab);

            Assert.AreSame(playerPrefab, PrefabOf(spawner),
                "This is the whole point of the wiring: FishNet has to know which prefab to spawn.");
        }

        [Test]
        public void Attach_AddsOnlyOneSpawner()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerPrefabFactory.Attach(manager, playerPrefab);
            PlayerPrefabFactory.Attach(manager, playerPrefab);

            Assert.AreEqual(1, manager.gameObject.GetComponents<PlayerSpawner>().Length,
                "Two spawners would hand every client two player objects.");
        }

        [Test]
        public void Attach_ReusesAnExistingSpawner()
        {
            NetworkManager manager = NewNetworkManager();
            PlayerSpawner existing = manager.gameObject.AddComponent<PlayerSpawner>();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            Assert.AreSame(existing, PlayerPrefabFactory.Attach(manager, playerPrefab));
        }

        [Test]
        public void Attach_RepointsAnExistingSpawnerAtANewPrefab()
        {
            NetworkManager manager = NewNetworkManager();
            PlayerSpawner spawner = manager.gameObject.AddComponent<PlayerSpawner>();
            NetworkObject stale = NewPlayerPrefab();
            Track(stale.gameObject);
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            PlayerPrefabFactory.Attach(manager, stale);
            PlayerPrefabFactory.Attach(manager, playerPrefab);

            Assert.AreSame(playerPrefab, PrefabOf(spawner));
        }

        [Test]
        public void Attach_KeepsTheCurrentPrefabWhenGivenNone()
        {
            NetworkManager manager = NewNetworkManager();
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);
            PlayerSpawner spawner = PlayerPrefabFactory.Attach(manager, playerPrefab);

            Assert.IsNotNull(PlayerPrefabFactory.Attach(manager, null));
            Assert.AreSame(playerPrefab, PrefabOf(spawner),
                "Rebuilding the manager must not silently blank out the spawner.");
        }

        [Test]
        public void Attach_LeavesAnUntouchedSpawnerUnsetWhenGivenNoPrefab()
        {
            NetworkManager manager = NewNetworkManager();

            PlayerSpawner spawner = PlayerPrefabFactory.Attach(manager, null);

            // Still returns a usable spawner so callers can finish configuring it.
            Assert.IsNotNull(spawner);
            Assert.IsNull(PrefabOf(spawner));
        }

        [Test]
        public void Attach_RejectsANullManager()
        {
            NetworkObject playerPrefab = NewPlayerPrefab();
            Track(playerPrefab.gameObject);

            Assert.Throws<ArgumentNullException>(() => PlayerPrefabFactory.Attach(null, playerPrefab));
        }
        #endregion
    }
}