using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Client;
using MMO.Editor;
using MMO.Gameplay;
using MMO.Inventory;
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
    /// The issue's file-level requirements: the capsule is saved as a reusable prefab in the
    /// project, the copy that was built in the scene is gone, and the NetworkManager is told to
    /// spawn that prefab for every player that connects.
    /// </summary>
    // These tests bake assets and re-import them per test, which costs seconds each; the
    // Unity Tests workflow shards them by category so they run in parallel with the suite.
    [Category("Bake")]
    [Category("BakePrefab")]
    public sealed class PlayerPrefabBakerTests
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

        /// <summary>
        /// The scene baker creates a NetworkManager whose SpawnablePrefabs collection is a bare
        /// ScriptableObject; leaving those behind would leak across tests.
        /// </summary>
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

        private static NetworkObject PrefabOf(PlayerSpawner spawner)
        {
            FieldInfo field = typeof(PlayerSpawner).GetField(
                "_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "FishNet renamed PlayerSpawner's prefab field.");
            return (NetworkObject)field.GetValue(spawner);
        }

        /// <summary>Every root object in the scene carrying this name.</summary>
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

        /// <summary>
        /// A readable list of the NetworkBehaviour types on an object, so a mismatch names the
        /// offending component instead of only saying the lists differ.
        /// </summary>
        private static string Describe(IReadOnlyList<NetworkBehaviour> behaviours)
        {
            var builder = new StringBuilder();
            foreach (NetworkBehaviour behaviour in behaviours)
            {
                if (builder.Length > 0)
                    builder.Append(", ");
                builder.Append(behaviour.GetType().Name);
            }

            return builder.ToString();
        }

        #region Saving the prefab.
        [Test]
        public void EnsurePlayerPrefab_WritesTheAssetToDisk()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsNotNull(prefab, "Baking has to produce something for the NetworkManager to spawn.");
            Assert.IsTrue(File.Exists(PlayerPrefabBaker.AbsolutePrefabPath),
                $"Expected a prefab asset at '{PlayerPrefabBaker.PrefabAssetPath}'.");
        }

        [Test]
        public void EnsurePlayerPrefab_KeepsThePrefabUnderAssetsPrefabs()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();

            // The path is a constant so the menu item, the scene baker and the tests can never
            // disagree about where the asset lives.
            Assert.AreEqual("Assets/Prefabs/Player.prefab", PlayerPrefabBaker.PrefabAssetPath);
            Assert.IsTrue(PlayerPrefabBaker.PrefabAssetPath.StartsWith("Assets/"),
                "Assets outside the project would not ship.");
        }

        [Test]
        public void EnsurePlayerPrefab_ProducesAnAssetRatherThanASceneObject()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsTrue(EditorUtility.IsPersistent(prefab),
                "A prefab template has to be an asset; a scene copy cannot be reused.");
            Assert.IsTrue(AssetDatabase.Contains(prefab.gameObject),
                "The prefab must be owned by the AssetDatabase.");
        }

        [Test]
        public void EnsurePlayerPrefab_DeletesTheCopyItBuiltInTheScene()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();

            // "Save this capsule as a Prefab, and delete the original from the scene."
            Assert.AreEqual(0, CountSceneObjectsNamed(PlayerPrefabFactory.PrefabName),
                "The scene must not keep the capsule the prefab was baked from.");
        }

        [Test]
        public void EnsurePlayerPrefab_LeavesNothingElseBehindInTheScene()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.AreEqual(0, _scene.GetRootGameObjects().Length,
                "Baking the prefab must not litter the scene with capsule children.");
        }

        [Test]
        public void EnsurePlayerPrefab_ProducesACharacterBody()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsNotNull(prefab.GetComponent<CharacterController>());
            Assert.IsNull(prefab.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(prefab.GetComponent<MeshFilter>());
            Assert.AreEqual(PlayerPrefabFactory.PrefabName, prefab.name);
        }

        [Test]
        public void EnsurePlayerPrefab_CarriesTheRiggedCharacterVisual()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            Transform visual = prefab.transform.Find(PlayerPrefabAuthoring.VisualRootName);
            Assert.IsNotNull(visual, "The baked player must carry the cosmetic character model.");

            Animator animator = visual.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "The character visual needs an Animator.");
            Assert.IsNotNull(animator.avatar, "A humanoid model without an avatar cannot play its clips.");
            Assert.IsTrue(animator.avatar.isHuman, "The character rig must import as Humanoid.");
            Assert.IsNotNull(animator.runtimeAnimatorController,
                "The visual needs the generated locomotion controller.");

            Assert.IsNotNull(visual.GetComponentInChildren<SkinnedMeshRenderer>(true),
                "The character model must render as a skinned mesh.");

            Assert.AreEqual(1, visual.GetComponentsInChildren<PlayerCharacterVisual>(true).Length,
                "Exactly one visual driver should animate the model.");
        }

        [Test]
        public void EnsurePlayerPrefab_HidesThePrimitiveCapsuleRenderer()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            var capsule = prefab.GetComponent<MeshRenderer>();
            Assert.IsNotNull(capsule, "The primitive mesh is kept for compatibility with the factory.");
            Assert.IsFalse(capsule.enabled, "The capsule must not render through the character model.");
        }

        [Test]
        public void EnsurePlayerPrefab_ProducesANetworkObject()
        {
            Assert.IsNotNull(PlayerPrefabBaker.EnsurePlayerPrefab(),
                "The prefab has to carry a NetworkObject or FishNet cannot track it.");
        }

        [Test]
        public void EnsurePlayerPrefab_ProducesTheProjectPlayerObject()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsNotNull(prefab.GetComponent<PlayerStats>());
            Assert.IsNotNull(prefab.GetComponent<PlayerInventory>(),
                "The baked prefab must carry the inventory container too, not just the stats.");
        }

        [Test]
        public void EnsurePlayerPrefab_EquipsTheShippedDefaultWeapon()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            // The whole reason the baked prefab arms the player is that the code-built player
            // leaves the factory unarmed; only the saved asset carries the ship's weapon.
            var combat = prefab.GetComponent<PlayerCombat>();
            Assert.IsNotNull(combat, "The baked prefab must carry the combat behaviour.");

            Assert.IsNotNull(combat.EquippedWeapon,
                "A freshly baked player must not spawn unarmed.");

            Assert.AreEqual(PlayerPrefabBaker.DefaultWeaponAssetPath,
                AssetDatabase.GetAssetPath(combat.EquippedWeapon),
                "The equipped weapon has to be the shipped Broadsword asset, not a scene copy.");
        }

        [Test]
        public void EnsurePlayerPrefab_LeavesTheFactoryComponentSetIntact()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            // The bake's only job is to arm the player and make its NetworkTransform
            // server-authoritative; it must not add or strip components of its own. Anything
            // else on this prefab shifts the NetworkBehaviour indices every SyncType is wired to.
            NetworkObject built = PlayerPrefabFactory.Create();
            try
            {
                Assert.AreEqual(
                    Describe(built.GetComponents<NetworkBehaviour>()),
                    Describe(prefab.GetComponents<NetworkBehaviour>()));
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        [Test]
        public void EnsurePlayerPrefab_MakesTheNetworkTransformServerAuthoritative()
        {
            NetworkObject prefab = PlayerPrefabBaker.EnsurePlayerPrefab();

            var netTransform = prefab.GetComponent<NetworkTransform>();
            Assert.IsNotNull(netTransform,
                "Without a NetworkTransform the server's movement never reaches the client.");

            SerializedProperty clientAuthoritative = new SerializedObject(netTransform)
                .FindProperty("_clientAuthoritative");
            Assert.IsNotNull(clientAuthoritative,
                "FishNet renamed NetworkTransform's _clientAuthoritative; update PlayerPrefabBaker.");
            Assert.IsFalse(clientAuthoritative.boolValue,
                "The server writes the transform, so the owner (client) must not be authoritative.");
        }

        [Test]
        public void EnsurePlayerPrefab_RefreshesAPrefabThatDriftedFromTheFactory()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();

            // Overwrite the asset with a deliberately incomplete copy: a prefab baked before the
            // factory grew click-to-move would look fine on disk but spawn players that cannot
            // move, so the baker has to notice and rebuild rather than reuse it.
            NetworkObject stale = PlayerPrefabFactory.Create();
            try
            {
                Object.DestroyImmediate(stale.GetComponent<PlayerClickToMove>());
                PrefabUtility.SaveAsPrefabAsset(stale.gameObject, PlayerPrefabBaker.PrefabAssetPath);
            }
            finally
            {
                Object.DestroyImmediate(stale.gameObject);
            }

            NetworkObject refreshed = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsNotNull(refreshed.GetComponent<PlayerClickToMove>(),
                "A prefab that has drifted from the factory must be rebuilt, not reused.");
        }

        [Test]
        public void EnsurePlayerPrefab_ReusesTheAssetOnLaterCalls()
        {
            NetworkObject first = PlayerPrefabBaker.EnsurePlayerPrefab();
            NetworkObject second = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.AreSame(first, second);
            Assert.AreEqual(0, CountSceneObjectsNamed(PlayerPrefabFactory.PrefabName),
                "Re-baking must reuse the same asset and must not leave a scene copy behind.");
        }

        [Test]
        public void EnsurePlayerPrefab_KeepsTheAssetOnDiskAcrossCalls()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();
            string firstGuid = AssetDatabase.AssetPathToGUID(PlayerPrefabBaker.PrefabAssetPath);

            PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(PlayerPrefabBaker.PrefabAssetPath),
                "Re-baking must not fork the asset into a second file.");
        }
        #endregion

        #region Loading and deleting.
        [Test]
        public void LoadPlayerPrefab_ReturnsNullBeforeAnythingIsBaked()
        {
            Assert.IsNull(PlayerPrefabBaker.LoadPlayerPrefab());
        }

        [Test]
        public void LoadPlayerPrefab_ReturnsTheBakedAsset()
        {
            NetworkObject baked = PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.AreSame(baked, PlayerPrefabBaker.LoadPlayerPrefab());
        }

        [Test]
        public void DeletePlayerPrefab_RemovesTheAsset()
        {
            PlayerPrefabBaker.EnsurePlayerPrefab();

            Assert.IsTrue(PlayerPrefabBaker.DeletePlayerPrefab());

            Assert.IsFalse(File.Exists(PlayerPrefabBaker.AbsolutePrefabPath));
            Assert.IsNull(PlayerPrefabBaker.LoadPlayerPrefab());
        }

        [Test]
        public void DeletePlayerPrefab_ReportsNothingToDoWhenThereIsNoAsset()
        {
            Assert.IsFalse(PlayerPrefabBaker.DeletePlayerPrefab(),
                "Deleting a prefab that was never baked is not a failure.");
        }
        #endregion

        #region Telling the NetworkManager to spawn it.
        [Test]
        public void WirePlayerPrefab_RegistersThePrefabAsASpawnable()
        {
            NetworkManager manager = NewNetworkManager();
            var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
            manager.SpawnablePrefabs = prefabs;

            NetworkObject playerPrefab = PlayerPrefabBaker.WirePlayerPrefab(manager);

            Assert.IsNotNull(playerPrefab);
            Assert.IsTrue(PlayerPrefabFactory.Contains(manager.SpawnablePrefabs, playerPrefab),
                "An unregistered prefab has no id, so the client could never instantiate it.");
        }

        [Test]
        public void WirePlayerPrefab_AddsAPlayerSpawner()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            PlayerPrefabBaker.WirePlayerPrefab(manager);

            PlayerSpawner spawner = manager.gameObject.GetComponent<PlayerSpawner>();
            Assert.IsNotNull(spawner, "FishNet 4.x spawns players through a PlayerSpawner component.");
            Assert.AreSame(manager.gameObject, spawner.gameObject,
                "PlayerSpawner looks for the NetworkManager with GetComponentInParent.");
        }

        [Test]
        public void WirePlayerPrefab_PointsTheSpawnerAtThePrefab()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            NetworkObject playerPrefab = PlayerPrefabBaker.WirePlayerPrefab(manager);

            Assert.AreSame(playerPrefab,
                PrefabOf(manager.gameObject.GetComponent<PlayerSpawner>()));
        }

        [Test]
        public void WirePlayerPrefab_WiresTheAssetRatherThanASceneCopy()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            NetworkObject playerPrefab = PlayerPrefabBaker.WirePlayerPrefab(manager);

            Assert.IsTrue(EditorUtility.IsPersistent(playerPrefab),
                "A scene copy would be serialized into the scene instead of referenced as a prefab.");
        }

        [Test]
        public void WirePlayerPrefab_GivesAConnectionlessManagerACollection()
        {
            // A NetworkManager that was never handed a collection cannot spawn anything.
            NetworkManager manager = NewNetworkManager();

            PlayerPrefabBaker.WirePlayerPrefab(manager);

            Assert.IsNotNull(manager.SpawnablePrefabs,
                "NetworkManager.Awake refuses a null SpawnablePrefabs, so wire one up.");
            Assert.AreEqual(NetworkManagerFactory.SpawnablePrefabsName, manager.SpawnablePrefabs.name);
        }

        [Test]
        public void WirePlayerPrefab_IsIdempotent()
        {
            NetworkManager manager = NewNetworkManager();
            GiveSpawnableCollection(manager);

            PlayerPrefabBaker.WirePlayerPrefab(manager);
            PlayerPrefabBaker.WirePlayerPrefab(manager);

            Assert.AreEqual(1, manager.gameObject.GetComponents<PlayerSpawner>().Length);
            Assert.AreEqual(1, ((SinglePrefabObjects)manager.SpawnablePrefabs).Prefabs.Count,
                "A duplicate entry would shift prefab ids and desync the two ends.");
        }

        [Test]
        public void WirePlayerPrefab_IgnoresANullManager()
        {
            Assert.IsNull(PlayerPrefabBaker.WirePlayerPrefab(null));
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