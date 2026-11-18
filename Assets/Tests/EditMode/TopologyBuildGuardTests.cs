using System.Linq;
using MMO.Content;
using MMO.Editor;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;

namespace MMO.Tests
{
    /// <summary>
    /// What the build guard refuses to ship: a missing topology, a map with errors, a node whose
    /// scene is not in enabled Build Settings, and the planned nodes that must be skipped.
    /// </summary>
    public sealed class TopologyBuildGuardTests
    {
        [TearDown]
        public void TearDown() => TestWorldTopologies.Cleanup();

        [Test]
        public void FindViolations_MissingTopology_IsReported()
        {
            var violations = TopologyBuildRules.FindViolations(null, new string[0]);

            CollectionAssert.Contains(violations, TopologyRules.ErrorMissingTopology);
        }

        [Test]
        public void FindViolations_ErrorBearingTopology_FailsTheBuildInput()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "ghost"));

            var violations = TopologyBuildRules.FindViolations(world, new string[0]);

            Assert.IsNotEmpty(violations);
        }

        [Test]
        public void FindViolations_BuiltNodeMissingFromBuildSettings_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe,
                    sceneName: "Assets/Scenes/SafeZone.unity"));

            var violations = TopologyBuildRules.FindViolations(world, new string[0]);

            Assert.IsTrue(violations.Any(v => v.Contains(TopologyBuildRules.ErrorMissingScene) &&
                                              v.Contains("SafeZone.unity")));
        }

        [Test]
        public void FindViolations_PlannedNode_IsSkipped()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "yellow-1"),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow, neighbours: "safe-zone"));

            var violations = TopologyBuildRules.FindViolations(world, new string[0]);

            CollectionAssert.IsEmpty(violations);
        }

        [Test]
        public void FindViolations_ShippedAssetWithItsScenesEnabled_Passes()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldTopology>(WorldTopology.AssetPath);
            var enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();

            var violations = TopologyBuildRules.FindViolations(world, enabled);

            CollectionAssert.IsEmpty(violations);
        }
    }
}
