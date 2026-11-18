using System;
using System.Collections.Generic;
using System.Linq;
using MMO.Content;

namespace MMO.Editor
{
    /// <summary>
    /// The build-time half of topology validation: everything <see cref="TopologyRules"/> reports,
    /// plus the check only the editor can make — that a node which claims a scene names one that is
    /// actually in enabled Build Settings.
    /// </summary>
    public static class TopologyBuildRules
    {
        public const string ErrorMissingScene =
            "topology names a scene that is not in enabled Build Settings";

        /// <summary>Every reason the map must not ship. Empty means the build may proceed.</summary>
        public static IReadOnlyList<string> FindViolations(
            WorldTopology world,
            IEnumerable<string> enabledScenePaths)
        {
            var violations = new List<string>(TopologyRules.FindViolations(world));
            if (world == null)
                return violations;

            var enabled = new HashSet<string>(enabledScenePaths ?? Array.Empty<string>(), StringComparer.Ordinal);

            foreach (var node in world.Nodes.Where(node => node != null && node.HasScene))
            {
                if (!enabled.Contains(node.SceneName))
                    violations.Add($"{ErrorMissingScene}: {node.SceneName}");
            }

            return violations;
        }
    }
}
