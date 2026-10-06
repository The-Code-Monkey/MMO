using System.Collections.Generic;
using System.IO;
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
    /// The issue's file-level requirement for the resource: the ore vein built by
    /// <see cref="ResourceNodePrefabFactory"/> is saved as a reusable prefab in the project and
    /// the copy that was built in the scene is gone. There is deliberately no spawner — a node is
    /// placed by a level or instantiated by a resource spawner, so the baker's only promises are
    /// the template staying a template and re-baking staying idempotent.
    /// </summary>
    public sealed class ResourceNodeBakerTests
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
                Object.DestroyImmediate(root);

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
        public void EnsureResourceNodePrefab_WritesTheAssetToDisk()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsNotNull(prefab, "Baking has to produce something for a level to scatter.");
            Assert.IsTrue(File.Exists(ResourceNodeBaker.AbsolutePrefabPath),
                $"Expected a prefab asset at '{ResourceNodeBaker.PrefabAssetPath}'.");
        }

        [Test]
        public void EnsureResourceNodePrefab_KeepsThePrefabUnderAssetsPrefabs()
        {
            ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.AreEqual("Assets/Prefabs/ResourceNode.prefab", ResourceNodeBaker.PrefabAssetPath);
            Assert.IsTrue(ResourceNodeBaker.PrefabAssetPath.StartsWith("Assets/"),
                "Assets outside the project would not ship.");
        }

        [Test]
        public void EnsureResourceNodePrefab_ProducesAnAssetRatherThanASceneObject()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsTrue(EditorUtility.IsPersistent(prefab),
                "A prefab template has to be an asset; a scene copy cannot be reused.");
            Assert.IsTrue(AssetDatabase.Contains(prefab.gameObject),
                "The prefab must be owned by the AssetDatabase.");
        }

        [Test]
        public void EnsureResourceNodePrefab_DeletesTheCopyItBuiltInTheScene()
        {
            ResourceNodeBaker.EnsureResourceNodePrefab();

            // "Save this node as a Prefab, and delete the original from the scene."
            Assert.AreEqual(0, CountSceneObjectsNamed(ResourceNodePrefabFactory.PrefabName),
                "The scene must not keep the cube the prefab was baked from.");
        }

        [Test]
        public void EnsureResourceNodePrefab_LeavesNothingElseBehindInTheScene()
        {
            ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.AreEqual(0, _scene.GetRootGameObjects().Length,
                "Baking the prefab must not litter the scene with cube children.");
        }

        [Test]
        public void EnsureResourceNodePrefab_ProducesAnOreCube()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsNotNull(prefab.GetComponent<BoxCollider>());
            Assert.IsNotNull(prefab.GetComponent<MeshFilter>());
            Assert.AreEqual(ResourceNodePrefabFactory.DefaultScale, prefab.transform.localScale);
            Assert.AreEqual(ResourceNodePrefabFactory.PrefabName, prefab.name);

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

            Assert.AreEqual(ResourceNodePrefabFactory.OreColor, colour,
                "The vein must stay ore-coloured after it becomes an asset.");
        }

        [Test]
        public void EnsureResourceNodePrefab_ProducesANetworkObject()
        {
            Assert.IsNotNull(ResourceNodeBaker.EnsureResourceNodePrefab(),
                "The prefab has to carry a NetworkObject or FishNet cannot track it.");
        }

        [Test]
        public void EnsureResourceNodePrefab_ProducesTheProjectNodeObject()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsNotNull(prefab.GetComponent<ResourceNode>(),
                "The baked node has to carry the project's own networked ResourceNode.");
        }

        [Test]
        public void EnsureResourceNodePrefab_ReusesTheAssetOnLaterCalls()
        {
            NetworkObject first = ResourceNodeBaker.EnsureResourceNodePrefab();
            NetworkObject second = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.AreSame(first, second);
            Assert.AreEqual(0, CountSceneObjectsNamed(ResourceNodePrefabFactory.PrefabName));
        }

        [Test]
        public void EnsureResourceNodePrefab_KeepsTheAssetOnDiskAcrossCalls()
        {
            ResourceNodeBaker.EnsureResourceNodePrefab();
            string firstGuid = AssetDatabase.AssetPathToGUID(ResourceNodeBaker.PrefabAssetPath);

            ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(ResourceNodeBaker.PrefabAssetPath),
                "Re-baking must not fork the asset into a second file.");
        }
        #endregion

        #region Loading and deleting.
        [Test]
        public void LoadResourceNodePrefab_ReturnsNullBeforeAnythingIsBaked()
        {
            Assert.IsNull(ResourceNodeBaker.LoadResourceNodePrefab());
        }

        [Test]
        public void LoadResourceNodePrefab_ReturnsTheBakedAsset()
        {
            NetworkObject baked = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.AreSame(baked, ResourceNodeBaker.LoadResourceNodePrefab());
        }

        [Test]
        public void DeleteResourceNodePrefab_RemovesTheAsset()
        {
            ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsTrue(ResourceNodeBaker.DeleteResourceNodePrefab());

            Assert.IsFalse(File.Exists(ResourceNodeBaker.AbsolutePrefabPath));
            Assert.IsNull(ResourceNodeBaker.LoadResourceNodePrefab());
        }

        [Test]
        public void DeleteResourceNodePrefab_ReportsNothingToDoWhenThereIsNoAsset()
        {
            Assert.IsFalse(ResourceNodeBaker.DeleteResourceNodePrefab(),
                "Deleting a prefab that was never baked is not a failure.");
        }
        #endregion
    }
}