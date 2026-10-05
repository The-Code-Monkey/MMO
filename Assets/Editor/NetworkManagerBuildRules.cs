using System.Collections.Generic;
using System.Linq;

namespace MMO.Editor
{
    public readonly struct SceneBuildInfo
    {
        public string Path { get; }
        public bool Enabled { get; }
        public bool ContainsNetworkManager { get; }

        public SceneBuildInfo(string path, bool enabled, bool containsNetworkManager)
        {
            Path = path;
            Enabled = enabled;
            ContainsNetworkManager = containsNetworkManager;
        }
    }

    public static class NetworkManagerBuildRules
    {
        public static IReadOnlyList<string> FindViolations(IEnumerable<SceneBuildInfo> scenes)
        {
            return scenes
                .Where(s => s.Enabled && !s.ContainsNetworkManager)
                .Select(s => s.Path)
                .ToList();
        }
    }
}
