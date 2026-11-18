using System.Collections.Generic;
using MMO.Content;
using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The structural shape of a world map: resolvable ids, portals declared on both sides, one
    /// scene per node, and a safe hub. Warnings cover zones that are legal but suspicious.
    /// </summary>
    public sealed class TopologyRulesTests
    {
        [TearDown]
        public void TearDown() => TestWorldTopologies.Cleanup();

        private static WorldTopology SafeAndYellow(params string[] safeNeighbours)
        {
            return TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: safeNeighbours),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow, neighbours: "safe-zone"));
        }

        [Test]
        public void FindViolations_EmptyTopology_ReportsNoSafeZone()
        {
            var violations = TopologyRules.FindViolations(TestWorldTopologies.Topology());

            CollectionAssert.Contains(violations, TopologyRules.ErrorNoSafeZone);
        }

        [Test]
        public void FindViolations_MissingTopology_IsReported()
        {
            var violations = TopologyRules.FindViolations(null);

            CollectionAssert.Contains(violations, TopologyRules.ErrorMissingTopology);
        }

        [Test]
        public void FindViolations_UnknownNeighbour_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "ghost"));

            var violations = TopologyRules.FindViolations(world);

            Assert.IsTrue(ContainsMentioning(violations, TopologyRules.ErrorUnknownNeighbour, "ghost"));
        }

        [Test]
        public void FindViolations_AsymmetricPortal_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "yellow-1"),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow));

            var violations = TopologyRules.FindViolations(world);

            Assert.IsTrue(ContainsMentioning(violations, TopologyRules.ErrorAsymmetricPortal, "yellow-1"));
        }

        [Test]
        public void FindViolations_SelfPortal_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "safe-zone"));

            var violations = TopologyRules.FindViolations(world);

            CollectionAssert.Contains(violations, $"{TopologyRules.ErrorSelfPortal}: safe-zone");
        }

        [Test]
        public void FindViolations_DuplicateZoneId_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, sceneName: "Assets/Scenes/A.unity"),
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Yellow, sceneName: "Assets/Scenes/B.unity"));

            var violations = TopologyRules.FindViolations(world);

            Assert.IsTrue(ContainsMentioning(violations, TopologyRules.ErrorDuplicateZoneId, "safe-zone"));
        }

        [Test]
        public void FindViolations_MissingZoneId_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe),
                TestWorldTopologies.Node(string.Empty, ZoneRiskTier.Yellow));

            var violations = TopologyRules.FindViolations(world);

            CollectionAssert.Contains(violations, TopologyRules.ErrorMissingZoneId);
        }

        [Test]
        public void FindViolations_DuplicateSceneName_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, sceneName: "Assets/Scenes/One.unity"),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow, sceneName: "Assets/Scenes/One.unity"));

            var violations = TopologyRules.FindViolations(world);

            Assert.IsTrue(ContainsMentioning(violations, TopologyRules.ErrorDuplicateSceneName, "One.unity"));
        }

        [Test]
        public void FindViolations_TwoWayPortal_Passes()
        {
            var world = SafeAndYellow("yellow-1");

            var violations = TopologyRules.FindViolations(world);

            CollectionAssert.IsEmpty(violations);
        }

        [Test]
        public void FindWarnings_ZoneWithNoPortals_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow));

            var warnings = TopologyRules.FindWarnings(world);

            Assert.IsTrue(ContainsMentioning(warnings, TopologyRules.WarningNoPortals, "yellow-1"));
        }

        [Test]
        public void FindWarnings_UnassignedTier_IsReported()
        {
            var world = TestWorldTopologies.Topology(
                TestWorldTopologies.Node("safe-zone", ZoneRiskTier.Safe, neighbours: "yellow-1"),
                TestWorldTopologies.Node("yellow-1", ZoneRiskTier.Yellow, tierAssigned: false,
                    neighbours: "safe-zone"));

            var warnings = TopologyRules.FindWarnings(world);

            Assert.IsTrue(ContainsMentioning(warnings, TopologyRules.WarningTierUnassigned, "yellow-1"));
        }

        [Test]
        public void FindWarnings_ValidMap_HasNoWarnings()
        {
            var world = SafeAndYellow("yellow-1");

            var warnings = TopologyRules.FindWarnings(world);

            CollectionAssert.IsEmpty(warnings);
        }

        private static bool ContainsMentioning(
            IReadOnlyList<string> messages,
            string code,
            string mention)
        {
            foreach (var message in messages)
            {
                if (message.Contains(code) && message.Contains(mention))
                    return true;
            }

            return false;
        }
    }
}
