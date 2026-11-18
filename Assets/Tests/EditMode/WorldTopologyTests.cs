using System.Linq;
using System.Reflection;
using MMO.Content;
using MMO.Editor;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// The macro map as data: the create-menu entry, the shipped asset's node list, and the two
    /// properties the design depends on — built zones name their scene, and the planned cluster is
    /// not cabled to the built group.
    /// </summary>
    public sealed class WorldTopologyTests
    {
        private static WorldTopology Shipped()
        {
            var topology = AssetDatabase.LoadAssetAtPath<WorldTopology>(WorldTopology.AssetPath);
            Assert.IsNotNull(topology, $"Expected a topology asset at {WorldTopology.AssetPath}.");
            return topology;
        }

        [Test]
        public void WorldTopology_IsOfferedByTheCreateAssetMenu()
        {
            var attribute = typeof(WorldTopology).GetCustomAttribute<CreateAssetMenuAttribute>();

            Assert.IsNotNull(attribute, "WorldTopology must be creatable from the Assets menu.");
            Assert.AreEqual("MMO/World Topology", attribute.menuName);
        }

        [Test]
        public void ShippedAsset_ListsEveryBuiltAndPlannedZone()
        {
            string[] expected =
            {
                "tutorial", "safe-zone", "wilderness", "test-zone",
                "yellow-1", "yellow-2", "red-1", "red-2", "black-1", "black-2", "pass-1",
            };

            var actual = Shipped().Nodes.Select(node => node.ZoneId).ToArray();

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void ShippedAsset_BuiltZonesCarryTheTierTheirScenesCarry()
        {
            var nodes = Shipped().Nodes.ToDictionary(node => node.ZoneId);

            Assert.AreEqual(ZoneRiskTier.Safe, nodes["tutorial"].Tier);
            Assert.AreEqual(ZoneRiskTier.Safe, nodes["safe-zone"].Tier);
            Assert.AreEqual(ZoneRiskTier.Yellow, nodes["wilderness"].Tier);
            Assert.AreEqual(ZoneRiskTier.Safe, nodes["test-zone"].Tier);
        }

        [Test]
        public void ShippedAsset_BuiltZoneTiersMatchTheirScenes()
        {
            foreach (var node in Shipped().Nodes.Where(node => node != null && node.HasScene))
            {
                Assert.AreEqual(node.Tier, RiskTierOf(node.SceneName),
                    $"'{node.ZoneId}' is {node.Tier} on the map but its scene declares another tier.");
            }
        }

        [Test]
        public void ShippedAsset_BuiltZonesNameTheirScene()
        {
            var nodes = Shipped().Nodes.ToDictionary(node => node.ZoneId);

            Assert.AreEqual("Assets/Scenes/Tutorial.unity", nodes["tutorial"].SceneName);
            Assert.AreEqual("Assets/Scenes/SafeZone.unity", nodes["safe-zone"].SceneName);
            Assert.AreEqual("Assets/Scenes/Wilderness.unity", nodes["wilderness"].SceneName);
            Assert.AreEqual("Assets/Scenes/TestZone.unity", nodes["test-zone"].SceneName);
        }

        [Test]
        public void ShippedAsset_PlannedZonesHaveNoScene()
        {
            var planned = Shipped().Nodes.Where(node => !node.HasScene).Select(node => node.ZoneId).ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "yellow-1", "yellow-2", "red-1", "red-2", "black-1", "black-2", "pass-1" },
                planned);
        }

        [Test]
        public void ShippedAsset_PlannedClusterIsNotLinkedToTheBuiltGroup()
        {
            var nodes = Shipped().Nodes.ToDictionary(node => node.ZoneId);
            string[] built = { "tutorial", "safe-zone", "wilderness", "test-zone" };
            string[] planned = { "yellow-1", "yellow-2", "red-1", "red-2", "black-1", "black-2", "pass-1" };

            foreach (var plannedId in planned)
            {
                var neighbours = nodes[plannedId].Neighbours;
                Assert.IsFalse(neighbours.Any(built.Contains),
                    $"{plannedId} is linked to the built group, but the cluster is not cabled yet.");
            }
        }

        [Test]
        public void ShippedAsset_OnlyThePassIsAChokePoint()
        {
            var chokePoints = Shipped().Nodes.Where(node => node.IsChokePoint)
                .Select(node => node.ZoneId).ToArray();

            CollectionAssert.AreEquivalent(new[] { "pass-1" }, chokePoints);
        }

        private static ZoneRiskTier RiskTierOf(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                GameObject root = scene.GetRootGameObjects()
                    .First(rootObject => rootObject.name == ZoneSceneBaker.ZoneRootName);
                Transform config = root.transform.Find(ZoneSceneBaker.RiskConfigName);

                Assert.IsNotNull(config, $"'{scenePath}' declares no zone-root risk tier.");
                return config.GetComponent<ZoneRiskConfig>().Tier;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
