using System.Collections.Generic;
using System.IO;
using System.Linq;
using MMO.Content;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Keeps the prose map and the data map in step. The asset is authoritative for connectivity, so
    /// a zone that exists only in data is a documentation bug — and one that is easy to miss by eye.
    /// </summary>
    public sealed class WorldMapDocTests
    {
        private const string ChapterPath = "docs/19-world-map-and-zone-topology.md";

        [TearDown]
        public void TearDown() => TestWorldTopologies.Cleanup();

        [Test]
        public void ShippedAsset_EveryZoneIsMentionedInTheChapter()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldTopology>(WorldTopology.AssetPath);
            Assert.IsNotNull(world, $"Expected a topology asset at {WorldTopology.AssetPath}.");

            var undocumented = FindUndocumented(world, ReadChapter());

            CollectionAssert.IsEmpty(undocumented,
                "Every zone in the topology must be described in the world-map chapter.");
        }

        [Test]
        public void FindUndocumented_ZoneAddedWithoutAnEntry_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", MMO.Gameplay.ZoneRiskTier.Safe, neighbours: "ghost-zone"),
                TestWorldTopologies.Node("ghost-zone", MMO.Gameplay.ZoneRiskTier.Yellow,
                    neighbours: "safe-zone"));

            var undocumented = FindUndocumented(world, ReadChapter());

            CollectionAssert.Contains(undocumented, "ghost-zone");
        }

        private static IReadOnlyList<string> FindUndocumented(WorldTopology world, string chapter)
        {
            return world.Nodes
                .Where(node => node != null && !chapter.Contains(node.ZoneId))
                .Select(node => node.ZoneId)
                .ToList();
        }

        private static string ReadChapter()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var path = Path.Combine(projectRoot ?? string.Empty, ChapterPath);
            Assert.IsTrue(File.Exists(path), $"Expected the world-map chapter at {ChapterPath}.");

            return File.ReadAllText(path);
        }
    }
}
