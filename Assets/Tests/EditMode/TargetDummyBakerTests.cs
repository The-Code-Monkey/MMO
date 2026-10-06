using System.Collections.Generic;
using System.IO;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Editor;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The issue's file-level requirements for the enemy: the red capsule is saved as a reusable
    /// prefab in the project, the copy that was built in the scene is gone, and the NetworkManager
    /// is told to instantiate and server-spawn that prefab when the server starts.
    /// </summary>
    public sealed class TargetDummyBakerTests
    {
        private Scene _scene;
        private SpawnablePrefabTestAsset _prefabAsset;

        private readonly List<GameObject> _sceneObjects = new();
        private readonly List<Object> _scratch = new();

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _prefabAsset = new SpawnablePrefabTestAsset();
        }

        [TearDown]
        public void TearDown()
        {
            DestroySceneObjects();
            _prefabAsset?.Cleanup();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private void DestroySceneObjects()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (NetworkManager manager in root.GetComponentsInChildren<NetworkManager>(true))
                {
                    if (manager.SpawnablePrefabs != null)
                        Object.DestroyImmediate(manager.SpawnablePrefabs);
                }

                Object.DestroyImmediate(root);
            }

            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _sceneObjects.Clear();
            _scratch.Clear();
        }

        private NetworkManager NewNetworkManager(string name = "WireTestManager")
        {
            var go = new GameObject(name);
            _sceneObjects.Add(go);
            return go.AddComponent<NetworkManager>();
        }

        private int CountSceneObjectsNamed(string name)
        {
            int count = 0;
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                if (root.name == name)
                    count++;
            }

            return count;
        }

        #region Saving the prefab.
        [Test]
        public void EnsureTargetDummyPrefab_WritesTheAssetToDisk()
        {
            NetworkObject prefab = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.IsNotNull(prefab, "Baking has to produce something for the server to spawn.");
            Assert.IsTrue(File.Exists(TargetDummyBaker.AbsolutePrefabPath),
                $"Expected a prefab asset at '{TargetDummyBaker.PrefabAssetPath}'.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_KeepsThePrefabUnderAssetsPrefabs()
        {
            TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.AreEqual("Assets/Prefabs/TargetDummy.prefab", TargetDummyBaker.PrefabAssetPath);
            Assert.IsTrue(TargetDummyBaker.PrefabAssetPath.StartsWith("Assets/"),
                "Assets outside the project would not ship.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_ProducesAnAssetRatherThanASceneObject()
        {
            NetworkObject prefab = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.IsTrue(EditorUtility.IsPersistent(prefab),
                "A prefab template has to be an asset; a scene copy cannot be reused.");
            Assert.IsTrue(AssetDatabase.Contains(prefab.gameObject),
                "The prefab must be owned by the AssetDatabase.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_DeletesTheCopyItBuiltInTheScene()
        {
            TargetDummyBaker.EnsureTargetDummyPrefab();

            // "Save this capsule as a Prefab, and delete the original from the scene."
            Assert.AreEqual(0, CountSceneObjectsNamed(TargetDummyPrefabFactory.PrefabName),
                "The scene must not keep the capsule the prefab was baked from.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_LeavesNothingElseBehindInTheScene()
        {
            TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.AreEqual(0, _scene.GetRootGameObjects().Length,
                "Baking the prefab must not litter the scene with capsule children.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_ProducesARedCapsule()
        {
            NetworkObject prefab = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.IsNotNull(prefab.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(prefab.GetComponent<MeshFilter>());
            Assert.AreEqual(TargetDummyPrefabFactory.PrefabName, prefab.name);

            Material material = prefab.GetComponent<MeshRenderer>()?.sharedMaterial;
            if (material == null)
            {
                Assert.Ignore("No material could be resolved in this environment.");
                return;
            }

            Color colour = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.HasProperty("_Color")
                    ? material.color
                    : Color.clear;

            Assert.AreEqual(TargetDummyPrefabFactory.RedColor, colour,
                "The enemy must stay red after it becomes an asset.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_ProducesANetworkObject()
        {
            Assert.IsNotNull(TargetDummyBaker.EnsureTargetDummyPrefab(),
                "The prefab has to carry a NetworkObject or FishNet cannot track it.");
        }

        [Test]
        public void EnsureTargetDummyPrefab_ProducesTheProjectEnemyObject()
        {
            NetworkObject prefab = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.IsNotNull(prefab.GetComponent<NPCStats>());
        }

        [Test]
        public void EnsureTargetDummyPrefab_ReusesTheAssetOnLaterCalls()
        {
            NetworkObject first = TargetDummyBaker.EnsureTargetDummyPrefab();
            NetworkObject second = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.AreSame(first, second);
            Assert.AreEqual(0, CountSceneObjectsNamed(TargetDummyPrefabFactory.PrefabName));
        }

        [Test]
        public void EnsureTargetDummyPrefab_KeepsTheAssetOnDiskAcrossCalls()
        {
            TargetDummyBaker.EnsureTargetDummyPrefab();
            string firstGuid = AssetDatabase.AssetPathToGUID(TargetDummyBaker.PrefabAssetPath);

            TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(TargetDummyBaker.PrefabAssetPath),
                "Re-baking must not fork the asset into a second file.");
        }
        #endregion

        #region Loading and deleting.
        [Test]
        public void LoadTargetDummyPrefab_ReturnsNullBeforeAnythingIsBaked()
        {
            Assert.IsNull(TargetDummyBaker.LoadTargetDummyPrefab());
        }

        [Test]
        public void LoadTargetDummyPrefab_ReturnsTheBakedAsset()
        {
            NetworkObject baked = TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.AreSame(baked, TargetDummyBaker.LoadTargetDummyPrefab());
        }

        [Test]
        public void DeleteTargetDummyPrefab_RemovesTheAsset()
        {
            TargetDummyBaker.EnsureTargetDummyPrefab();

            Assert.IsTrue(TargetDummyBaker.DeleteTargetDummyPrefab());

            Assert.IsFalse(File.Exists(TargetDummyBaker.AbsolutePrefabPath));
            Assert.IsNull(TargetDummyBaker.LoadTargetDummyPrefab());
        }

        [Test]
        public void DeleteTargetDummyPrefab_ReportsNothingToDoWhenThereIsNoAsset()
        {
            Assert.IsFalse(TargetDummyBaker.DeleteTargetDummyPrefab(),
                "Deleting a prefab that was never baked is not a failure.");
        }
        #endregion

        #region Telling the NetworkManager to spawn it.
        [Test]
        public void WireTargetDummy_RegistersThePrefabAsASpawnable()
        {
            NetworkManager manager = NewNetworkManager();
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
            manager.SpawnablePrefabs = prefabs;

            NetworkObject targetDummy = TargetDummyBaker.WireTargetDummy(manager);

            Assert.IsNotNull(targetDummy);
            Assert.IsTrue(TargetDummyPrefabFactory.Contains(manager.SpawnablePrefabs, targetDummy),
                "An unregistered prefab has no id, so the client could never instantiate it.");
        }

        [Test]
        public void WireTargetDummy_AddsATargetDummySpawner()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            TargetDummyBaker.WireTargetDummy(manager);

            TargetDummySpawner spawner = manager.gameObject.GetComponent<TargetDummySpawner>();
            Assert.IsNotNull(spawner, "The server needs a component to instantiate and spawn the dummy.");
            Assert.AreSame(manager.gameObject, spawner.gameObject);
        }

        [Test]
        public void WireTargetDummy_PointsTheSpawnerAtThePrefab()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            NetworkObject targetDummy = TargetDummyBaker.WireTargetDummy(manager);

            Assert.AreSame(targetDummy, manager.gameObject.GetComponent<TargetDummySpawner>().Prefab);
        }

        [Test]
        public void WireTargetDummy_WiresTheAssetRatherThanASceneCopy()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            NetworkObject targetDummy = TargetDummyBaker.WireTargetDummy(manager);

            Assert.IsTrue(EditorUtility.IsPersistent(targetDummy),
                "A scene copy would be serialized into the scene instead of referenced as a prefab.");
        }

        [Test]
        public void WireTargetDummy_GivesAConnectionlessManagerACollection()
        {
            NetworkManager manager = NewNetworkManager();

            TargetDummyBaker.WireTargetDummy(manager);

            Assert.IsNotNull(manager.SpawnablePrefabs,
                "NetworkManager.Awake refuses a null SpawnablePrefabs, so wire one up.");
            Assert.AreEqual(NetworkManagerFactory.SpawnablePrefabsName, manager.SpawnablePrefabs.name);
        }

        [Test]
        public void WireTargetDummy_IsIdempotent()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            TargetDummyBaker.WireTargetDummy(manager);
            TargetDummyBaker.WireTargetDummy(manager);

            Assert.AreEqual(1, manager.gameObject.GetComponents<TargetDummySpawner>().Length);
            Assert.AreEqual(1, ((SinglePrefabObjects)manager.SpawnablePrefabs).Prefabs.Count,
                "A duplicate entry would shift prefab ids and desync the two ends.");
        }

        [Test]
        public void WireTargetDummy_IgnoresANullManager()
        {
            Assert.IsNull(TargetDummyBaker.WireTargetDummy(null));
        }
        #endregion

        /// <summary>Gives the manager the same collection shape the factory builds.</summary>
        private void GiveSpawnableCollection(NetworkManager manager)
        {
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
            manager.SpawnablePrefabs = prefabs;
            _scratch.Add(prefabs);
        }
    }
}