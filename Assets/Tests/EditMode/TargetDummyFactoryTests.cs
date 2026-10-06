using System;
using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The damageable target the issue wants to click on: a cube a player can see and click, with
    /// the networked <see cref="TargetDummy"/> on board and the spawnable registration that lets
    /// the server hand it a prefab id. Mirrors <see cref="PlayerPrefabFactoryTests"/> so a player
    /// and a dummy are held to the same shape.
    /// </summary>
    public sealed class TargetDummyFactoryTests
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

        private NetworkObject NewDummyPrefab() => TargetDummyFactory.Create();

        private SinglePrefabObjects NewSpawnables()
        {
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = "TestSpawnablePrefabs";
            _scratch.Add(prefabs);
            return prefabs;
        }

        #region The object itself.
        [Test]
        public void Create_NamesTheObjectTargetDummy()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(TargetDummyFactory.PrefabName, nob.gameObject.name);
        }

        [Test]
        public void Create_IsACubeWithAColliderAndRenderer()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            // CreatePrimitive(Cube) is what supplies the mesh, renderer and a BoxCollider whose
            // dimensions already match the mesh — the collider is what a player actually clicks.
            Assert.IsNotNull(nob.GetComponent<BoxCollider>(), "Without a collider the cube cannot be clicked.");
            Assert.IsNotNull(nob.GetComponent<MeshFilter>(), "A cube nobody can see is not a target.");
            Assert.IsNotNull(nob.GetComponent<MeshRenderer>(), "A cube without a renderer is invisible.");
        }

        [Test]
        public void Create_ScalesThePrimitiveSoTheWorldSizeIsTheDeclaredSize()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(TargetDummyFactory.DefaultScale, nob.transform.localScale);
        }

        [Test]
        public void Create_DefaultsToHumanSizedProportions()
        {
            // A metre-wide, two-metre-tall cube, roughly the silhouette of the player capsule.
            Assert.AreEqual(1f, TargetDummyFactory.DefaultWidth, 0.0001f);
            Assert.AreEqual(2f, TargetDummyFactory.DefaultHeight, 0.0001f);
        }

        [Test]
        public void Create_CarriesANetworkObject()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<NetworkObject>(),
                "Without a NetworkObject FishNet has nothing to track across the network.");
        }

        [Test]
        public void Create_AddsTheTargetDummyBehaviour()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.IsNotNull(nob.GetComponent<TargetDummy>(),
                "TargetDummy is the project's own networked component and belongs on the dummy.");
        }

        [Test]
        public void Create_PutsTheDummyNextToTheNetworkObject()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            // A NetworkBehaviour resolves its NetworkObject through its own GameObject; a mismatch
            // is the classic "health never replicates" bug.
            NetworkBehaviour[] behaviours = nob.GetComponents<NetworkBehaviour>();
            Assert.AreEqual(1, behaviours.Length, "Only the project's own behaviour belongs here.");
            Assert.AreSame(nob.gameObject, behaviours[0].gameObject,
                "The dummy and the NetworkObject must be on the same object.");
        }

        [Test]
        public void Create_LeavesTheObjectAtTheOrigin()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.AreEqual(Vector3.zero, nob.transform.position);
            Assert.AreEqual(Quaternion.identity, nob.transform.rotation);
        }

        [Test]
        public void Create_LeavesTheObjectActiveSoItCanBeSpawnedOrSaved()
        {
            NetworkObject nob = TargetDummyFactory.Create();
            Track(nob.gameObject);

            Assert.IsTrue(nob.gameObject.activeSelf,
                "An inactive root is saved into the prefab greyed out; deactivate at runtime instead.");
        }

        [Test]
        public void Create_BuildsIndependentInstances()
        {
            NetworkObject first = NewDummyPrefab();
            Track(first.gameObject);
            NetworkObject second = NewDummyPrefab();
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

            NetworkObject nob = NewDummyPrefab();
            Track(nob.gameObject);

            // Without this a cube in a URP project renders magenta.
            Assert.AreSame(pipeline.defaultMaterial, nob.GetComponent<MeshRenderer>().sharedMaterial);
        }
        #endregion

        #region Spawnable registration.
        [Test]
        public void Register_AddsThePrefabToTheCollection()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject dummyPrefab = NewDummyPrefab();
            Track(dummyPrefab.gameObject);

            Assert.IsTrue(TargetDummyFactory.Register(prefabs, dummyPrefab));

            Assert.AreEqual(1, prefabs.Prefabs.Count);
            Assert.AreSame(dummyPrefab, prefabs.Prefabs[0],
                "FishNet derives prefab ids from list order, so the dummy has to be in there.");
        }

        [Test]
        public void Register_LeavesPrefabIdsForTheNetworkManagerInEditMode()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject dummyPrefab = NewDummyPrefab();
            Track(dummyPrefab.gameObject);

            TargetDummyFactory.Register(prefabs, dummyPrefab);

            // In EditMode there is no manager Awake to license immediate id assignment, so the id
            // stays unset and NetworkManager.Awake assigns it later — the same contract as the
            // player prefab. The difference arrives at runtime, and is asserted in the PlayMode
            // combat tests, where the shared manager is already awake when the dummy registers.
            Assert.AreEqual(NetworkObject.UNSET_PREFABID_VALUE, dummyPrefab.PrefabId);
        }

        [Test]
        public void Register_IsIdempotent()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject dummyPrefab = NewDummyPrefab();
            Track(dummyPrefab.gameObject);

            Assert.IsTrue(TargetDummyFactory.Register(prefabs, dummyPrefab));
            Assert.IsFalse(TargetDummyFactory.Register(prefabs, dummyPrefab),
                "A second register would shift every prefab id by one on both ends.");
            Assert.AreEqual(1, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_IgnoresANullPrefab()
        {
            SinglePrefabObjects prefabs = NewSpawnables();

            Assert.IsFalse(TargetDummyFactory.Register(prefabs, null));
            Assert.AreEqual(0, prefabs.Prefabs.Count);
        }

        [Test]
        public void Register_RejectsANullCollection()
        {
            NetworkObject dummyPrefab = NewDummyPrefab();
            Track(dummyPrefab.gameObject);

            Assert.Throws<ArgumentNullException>(() => TargetDummyFactory.Register(null, dummyPrefab));
        }

        [Test]
        public void Contains_ReportsWhetherThePrefabIsRegistered()
        {
            SinglePrefabObjects prefabs = NewSpawnables();
            NetworkObject dummyPrefab = NewDummyPrefab();
            Track(dummyPrefab.gameObject);
            NetworkObject stranger = NewDummyPrefab();
            Track(stranger.gameObject);

            Assert.IsFalse(TargetDummyFactory.Contains(prefabs, dummyPrefab));

            TargetDummyFactory.Register(prefabs, dummyPrefab);

            Assert.IsTrue(TargetDummyFactory.Contains(prefabs, dummyPrefab));
            Assert.IsFalse(TargetDummyFactory.Contains(prefabs, stranger));
        }
        #endregion
    }
}