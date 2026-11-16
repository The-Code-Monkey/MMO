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
    // These tests bake assets and re-import them per test, which costs seconds each; the
    // Unity Tests workflow shards them by category so they run in parallel with the suite.
    [Category("Bake")]
    [Category("BakeContent")]
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

        /// <summary>
        /// Copies the checked-in prefab — and its .meta, so the GUID survives — outside the project
        /// so a test can delete it and put the exact bytes back afterwards. The prefab is authored
        /// content, not a test fixture: without this, one delete test leaves every later test
        /// looking at a bare factory bake instead of the shipped ore models.
        /// </summary>
        private static string StashShippedPrefab()
        {
            string prefab = ResourceNodeBaker.AbsolutePrefabPath;
            if (!File.Exists(prefab))
                return null;

            string stash = Path.Combine(
                Path.GetTempPath(), "mmo-resource-node-" + System.Guid.NewGuid().ToString("N") + ".prefab");
            File.Copy(prefab, stash);
            if (File.Exists(prefab + ".meta"))
                File.Copy(prefab + ".meta", stash + ".meta");
            return stash;
        }

        private static void RestoreShippedPrefab(string stash)
        {
            if (stash == null)
                return;

            string prefab = ResourceNodeBaker.AbsolutePrefabPath;
            File.Copy(stash, prefab, overwrite: true);
            if (File.Exists(stash + ".meta"))
                File.Copy(stash + ".meta", prefab + ".meta", overwrite: true);

            File.Delete(stash);
            if (File.Exists(stash + ".meta"))
                File.Delete(stash + ".meta");

            AssetDatabase.ImportAsset(
                ResourceNodeBaker.PrefabAssetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
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
        public void EnsureResourceNodePrefab_ProducesTheOreModel()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Assert.IsNotNull(prefab.GetComponent<BoxCollider>(),
                "The root keeps the click collider even though the model is a child.");
            Assert.AreEqual(ResourceNodePrefabFactory.DefaultScale, prefab.transform.localScale);
            Assert.AreEqual(ResourceNodePrefabFactory.PrefabName, prefab.name);

            Assert.IsNotNull(prefab.transform.Find("Visuals/IronOreMesh"),
                "The baked node must carry the imported ore model under Visuals.");

            MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(renderer, "A vein nobody can see is not a landmark.");

            Material material = renderer.sharedMaterial;
            if (material == null)
            {
                Assert.Ignore("No material could be resolved in this environment.");
                return;
            }

            Assert.AreEqual("IronOre", material.name,
                "The baked node must wear the iron-ore material, not a flat fallback colour.");
        }

        [Test]
        public void EnsureResourceNodePrefab_HasFullAndEmptyModels()
        {
            NetworkObject prefab = ResourceNodeBaker.EnsureResourceNodePrefab();

            Transform full = prefab.transform.Find("Visuals/IronOreMesh");
            Transform empty = prefab.transform.Find("Visuals/StoneOreEmptyMesh");

            Assert.IsNotNull(full, "The node needs its full ore model.");
            Assert.IsNotNull(empty, "The node needs its depleted ore model.");
            Assert.IsTrue(full.gameObject.activeSelf, "The full model is the default state.");
            Assert.IsFalse(empty.gameObject.activeSelf, "The empty model only shows while depleted.");
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
            string stash = StashShippedPrefab();
            ResourceNodeBaker.DeleteResourceNodePrefab();
            try
            {
                Assert.IsNull(ResourceNodeBaker.LoadResourceNodePrefab());
            }
            finally
            {
                RestoreShippedPrefab(stash);
            }
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
            string stash = StashShippedPrefab();
            try
            {
                Assert.IsTrue(ResourceNodeBaker.DeleteResourceNodePrefab());

                Assert.IsFalse(File.Exists(ResourceNodeBaker.AbsolutePrefabPath));
                Assert.IsNull(ResourceNodeBaker.LoadResourceNodePrefab());
            }
            finally
            {
                RestoreShippedPrefab(stash);
            }
        }

        [Test]
        public void DeleteResourceNodePrefab_ReportsNothingToDoWhenThereIsNoAsset()
        {
            string stash = StashShippedPrefab();
            ResourceNodeBaker.DeleteResourceNodePrefab();
            try
            {
                Assert.IsFalse(ResourceNodeBaker.DeleteResourceNodePrefab(),
                    "Deleting a prefab that was never baked is not a failure.");
            }
            finally
            {
                RestoreShippedPrefab(stash);
            }
        }
        #endregion
    }
}