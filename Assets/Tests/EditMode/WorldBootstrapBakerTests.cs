using System.Linq;
using FishNet.Managing;
using FishNet.Managing.Object;
using MMO.Editor;
using MMO.Gameplay;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// The zone scenes connected to the server logic. The zone baker authors spawn points and
    /// marker objects; <see cref="WorldBootstrapBaker"/> adds the per-scene
    /// <see cref="WorldBootstrap"/> and registers the node prefab every zone spawns from. The
    /// tutorial zone — the flat ground a brand-new player lands in — is asserted to be exactly
    /// that: a spawn point and a dummy, and nothing else.
    /// </summary>
    public sealed class WorldBootstrapBakerTests
    {
        private EditorBuildSettingsScene[] _originalBuildScenes;
        private bool _safeExisted;
        private bool _wildernessExisted;
        private bool _tutorialExisted;

        [OneTimeSetUp]
        public void BuildZones()
        {
            _originalBuildScenes = EditorBuildSettings.scenes;
            _safeExisted = AssetDatabase.LoadAssetAtPath<SceneAsset>(ZoneSceneBaker.SafeZonePath) != null;
            _wildernessExisted = AssetDatabase.LoadAssetAtPath<SceneAsset>(ZoneSceneBaker.WildernessZonePath) != null;
            _tutorialExisted = AssetDatabase.LoadAssetAtPath<SceneAsset>(ZoneSceneBaker.TutorialZonePath) != null;

            ZoneSceneBaker.BuildBoth();
        }

        [OneTimeTearDown]
        public void RemoveZones()
        {
            EditorBuildSettings.scenes = _originalBuildScenes;

            if (!_safeExisted)
                AssetDatabase.DeleteAsset(ZoneSceneBaker.SafeZonePath);
            if (!_wildernessExisted)
                AssetDatabase.DeleteAsset(ZoneSceneBaker.WildernessZonePath);
            if (!_tutorialExisted)
                AssetDatabase.DeleteAsset(ZoneSceneBaker.TutorialZonePath);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [SetUp]
        public void ResetActiveScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ---- The tutorial zone ---------------------------------------------

        [Test]
        public void TutorialZone_IsInEnabledBuildSettings()
        {
            Assert.IsTrue(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == ZoneSceneBaker.TutorialZonePath));
        }

        [Test]
        public void TutorialZone_HasANetworkManager()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
                Assert.IsTrue(NetworkManagerSceneBaker.Validate(scene, out string error), error));
        }

        [Test]
        public void TutorialZone_IsFlatAndBare()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
            {
                GameObject zoneRoot = FindRoot(scene, ZoneSceneBaker.ZoneRootName);
                Assert.IsNotNull(ChildOf(zoneRoot, ZoneSceneBaker.GroundName), "A flat zone still needs ground.");

                Assert.IsNull(ChildOf(zoneRoot, ZoneSceneBaker.BuildingsName), "The tutorial is not a town.");
                Assert.IsNull(ChildOf(zoneRoot, ZoneSceneBaker.ScatterName), "The tutorial is not a wilderness.");
                Assert.IsNull(ChildOf(zoneRoot, ZoneSceneBaker.HazardsName), "The tutorial is safe.");
            });
        }

        [Test]
        public void TutorialZone_HasASingleSpawnPoint()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
            {
                Transform spawnPoints = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.SpawnPointsName);
                Assert.IsNotNull(spawnPoints);
                Assert.AreEqual(ZoneSceneBaker.TutorialSpawnPointCount, spawnPoints.childCount);

                Assert.IsTrue(ChildOf(spawnPoints.gameObject, "PlayerSpawn_1")
                    .GetComponent<PlayerSpawnPoint>().IsDefault, "A new player with no save needs a default point.");
            });
        }

        [Test]
        public void TutorialZone_HasExactlyOneTargetDummyMarker()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
            {
                Transform markers = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.MarkersName);
                Assert.IsNotNull(markers);

                Assert.AreEqual(1, markers.GetComponentsInChildren<EnemyMarker>().Length,
                    "One dummy to learn the combat loop on.");
                Assert.AreEqual(1, markers.GetComponentsInChildren<ResourceNodeMarker>().Length,
                    "The tutorial ships an iron vein to gather from.");
                Assert.AreEqual(1, markers.GetComponentsInChildren<CraftingStationMarker>().Length,
                    "The tutorial ships a furnace so the ore-to-ingot recipe is reachable.");
            });
        }

        [Test]
        public void TutorialZone_IsSafeTier()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
            {
                Transform config = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.RiskConfigName);
                Assert.IsNotNull(config);
                Assert.AreEqual(ZoneRiskTier.Safe, config.GetComponent<ZoneRiskConfig>().Tier);
            });
        }

        [Test]
        public void TutorialZone_PortalLeadsToTheSafeZone()
        {
            InScene(ZoneSceneBaker.TutorialZonePath, scene =>
            {
                Transform portal = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), "PortalToSafeZone");
                Assert.IsNotNull(portal);

                var serialized = new SerializedObject(portal.GetComponent<ZoneTransferTrigger>());
                Assert.AreEqual(ZoneSceneBaker.SafeZonePort, (ushort)serialized.FindProperty("_targetPort").intValue);
                Assert.AreEqual(ZoneSceneBaker.SafeZoneServerId, serialized.FindProperty("_targetServerId").stringValue);
            });
        }

        // ---- The bootstrap and markers on every zone -----------------------

        [Test]
        public void EveryZone_HasAWorldBootstrapOnItsNetworkManager()
        {
            foreach (string path in new[] { ZoneSceneBaker.SafeZonePath, ZoneSceneBaker.WildernessZonePath, ZoneSceneBaker.TutorialZonePath })
            {
                InScene(path, scene =>
                {
                    Assert.IsTrue(NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager));
                    Assert.IsNotNull(manager.GetComponent<WorldBootstrap>(),
                        $"'{scene.name}' must connect its markers to the server.");
                });
            }
        }

        [Test]
        public void EveryZone_SpawnPointsCarryPlayerSpawnPoints()
        {
            foreach (string path in new[] { ZoneSceneBaker.SafeZonePath, ZoneSceneBaker.WildernessZonePath, ZoneSceneBaker.TutorialZonePath })
            {
                InScene(path, scene =>
                {
                    Transform spawnPoints = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.SpawnPointsName);
                    PlayerSpawnPoint[] points = spawnPoints.GetComponentsInChildren<PlayerSpawnPoint>();
                    Assert.AreEqual(spawnPoints.childCount, points.Length);
                    Assert.AreEqual(1, points.Count(p => p.IsDefault),
                        $"'{scene.name}' declares exactly one default spawn point.");
                });
            }
        }

        [Test]
        public void SafeZone_HasStationAndNodeMarkers()
        {
            InScene(ZoneSceneBaker.SafeZonePath, scene =>
            {
                Transform markers = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.MarkersName);
                Assert.AreEqual(2, markers.GetComponentsInChildren<CraftingStationMarker>().Length, "Furnace and anvil.");
                Assert.AreEqual(3, markers.GetComponentsInChildren<ResourceNodeMarker>().Length);
                Assert.AreEqual(0, markers.GetComponentsInChildren<EnemyMarker>().Length, "A safe town has no enemies.");
            });
        }

        [Test]
        public void Wilderness_HasNodeAndEnemyMarkers()
        {
            InScene(ZoneSceneBaker.WildernessZonePath, scene =>
            {
                Transform markers = ChildOf(FindRoot(scene, ZoneSceneBaker.ZoneRootName), ZoneSceneBaker.MarkersName);
                Assert.AreEqual(4, markers.GetComponentsInChildren<ResourceNodeMarker>().Length);
                Assert.Greater(markers.GetComponentsInChildren<EnemyMarker>().Length, 0, "A wilderness with nothing to fight is a park.");
                Assert.AreEqual(0, markers.GetComponentsInChildren<CraftingStationMarker>().Length);
            });
        }

        [Test]
        public void EveryZone_HasNoFixedDummyOrStationSpawner()
        {
            foreach (string path in new[] { ZoneSceneBaker.SafeZonePath, ZoneSceneBaker.WildernessZonePath, ZoneSceneBaker.TutorialZonePath })
            {
                InScene(path, scene =>
                {
                    Assert.IsTrue(NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager));
                    Assert.IsNull(manager.GetComponent<TargetDummySpawner>(),
                        $"'{scene.name}' spawns its dummies from markers; a fixed spawner would duplicate them.");
                    Assert.IsNull(manager.GetComponent<CraftingStationSpawner>(),
                        $"'{scene.name}' spawns its stations from markers; a fixed spawner would duplicate them.");
                });
            }
        }

        [Test]
        public void EveryZone_RegistersTheResourceNodePrefab()
        {
            foreach (string path in new[] { ZoneSceneBaker.SafeZonePath, ZoneSceneBaker.WildernessZonePath, ZoneSceneBaker.TutorialZonePath })
            {
                InScene(path, scene =>
                {
                    Assert.IsTrue(NetworkManagerSceneBaker.TryFind(scene, out NetworkManager manager));
                    var prefabs = manager.SpawnablePrefabs as SinglePrefabObjects;
                    Assert.IsNotNull(prefabs);
                    Assert.IsTrue(prefabs.Prefabs.Any(p => p != null && p.name == ResourceNodePrefabFactory.PrefabName),
                        $"'{scene.name}' spawns nodes, so both ends must know the node prefab.");
                });
            }
        }

        // ---- Helpers -------------------------------------------------------

        private static void InScene(string path, System.Action<Scene> assert)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                assert(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            Assert.Fail($"Scene '{scene.name}' has no root object named '{name}'.");
            return null;
        }

        private static Transform ChildOf(GameObject root, string name) => root.transform.Find(name);
    }
}
