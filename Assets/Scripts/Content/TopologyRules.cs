using System;
using System.Collections.Generic;
using System.Linq;
using MMO.Gameplay;

namespace MMO.Content
{
    /// <summary>
    /// The world map's structural shape as pure rules: ids that resolve, portals declared on both
    /// sides, one scene per node, and at least one safe hub. Every message is a const so tests and
    /// the build guard agree on wording. Empty violation list means the map is valid.
    /// </summary>
    public static class TopologyRules
    {
        public const string ErrorMissingTopology = "topology is missing";
        public const string ErrorNoSafeZone = "no Safe zone defined";
        public const string ErrorMissingZoneId = "zone has no id";
        public const string ErrorDuplicateZoneId = "duplicate zone id";
        public const string ErrorDuplicateSceneName = "duplicate scene name";
        public const string ErrorUnknownNeighbour = "unknown neighbour";
        public const string ErrorAsymmetricPortal = "portal is declared on one side only";
        public const string ErrorSelfPortal = "zone lists itself as a neighbour";

        public const string WarningNoPortals = "zone has no portals";
        public const string WarningTierUnassigned = "zone tier is not assigned";

        /// <summary>Every reason the map is not structurally valid. Empty means valid.</summary>
        public static IReadOnlyList<string> FindViolations(WorldTopology world)
        {
            var violations = new List<string>();
            if (world == null)
            {
                violations.Add(ErrorMissingTopology);
                return violations;
            }

            var nodes = world.Nodes.Where(node => node != null).ToList();
            var byId = IndexById(nodes, violations);

            if (!nodes.Any(node => node.Tier == ZoneRiskTier.Safe))
                violations.Add(ErrorNoSafeZone);

            ReportDuplicateScenes(nodes, violations);
            ReportNeighbourProblems(nodes, byId, violations);

            return violations;
        }

        /// <summary>Findings that do not make the map wrong, but are worth knowing.</summary>
        public static IReadOnlyList<string> FindWarnings(WorldTopology world)
        {
            var warnings = new List<string>();
            if (world == null)
                return warnings;

            foreach (var node in world.Nodes.Where(node => node != null))
            {
                if (node.Neighbours.Count == 0)
                    warnings.Add($"{WarningNoPortals}: {Describe(node)}");

                if (!node.TierAssigned)
                    warnings.Add($"{WarningTierUnassigned}: {Describe(node)}");
            }

            return warnings;
        }

        private static Dictionary<string, ZoneNode> IndexById(
            IReadOnlyList<ZoneNode> nodes,
            List<string> violations)
        {
            var byId = new Dictionary<string, ZoneNode>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                if (string.IsNullOrWhiteSpace(node.ZoneId))
                {
                    violations.Add(ErrorMissingZoneId);
                    continue;
                }

                if (!byId.TryAdd(node.ZoneId, node))
                    violations.Add($"{ErrorDuplicateZoneId}: {node.ZoneId}");
            }

            return byId;
        }

        private static void ReportDuplicateScenes(
            IReadOnlyList<ZoneNode> nodes,
            List<string> violations)
        {
            var scenes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in nodes.Where(node => node.HasScene))
            {
                if (!scenes.Add(node.SceneName))
                    violations.Add($"{ErrorDuplicateSceneName}: {node.SceneName}");
            }
        }

        private static void ReportNeighbourProblems(
            IReadOnlyList<ZoneNode> nodes,
            IReadOnlyDictionary<string, ZoneNode> byId,
            List<string> violations)
        {
            foreach (var node in nodes)
            {
                if (string.IsNullOrWhiteSpace(node.ZoneId))
                    continue;

                foreach (var neighbourId in node.Neighbours)
                {
                    if (string.Equals(neighbourId, node.ZoneId, StringComparison.Ordinal))
                    {
                        violations.Add($"{ErrorSelfPortal}: {node.ZoneId}");
                        continue;
                    }

                    if (!byId.TryGetValue(neighbourId, out var neighbour))
                    {
                        violations.Add($"{ErrorUnknownNeighbour}: {node.ZoneId} -> {neighbourId}");
                        continue;
                    }

                    if (!neighbour.Neighbours.Contains(node.ZoneId, StringComparer.Ordinal))
                        violations.Add($"{ErrorAsymmetricPortal}: {node.ZoneId} -> {neighbourId}");
                }
            }
        }

        private static string Describe(ZoneNode node) =>
            string.IsNullOrWhiteSpace(node.ZoneId) ? "<no id>" : node.ZoneId;
    }
}
