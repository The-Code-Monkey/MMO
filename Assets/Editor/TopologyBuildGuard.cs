using System.Linq;
using MMO.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Fails the build when the world topology is missing, structurally wrong, or names a scene that
    /// will not ship. A build from a broken map would put players in a world whose roads do not hold
    /// together. Warnings (a zone with no portals, a tier still in draft) are logged, not fatal.
    /// </summary>
    public sealed class TopologyBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldTopology>(WorldTopology.AssetPath);
            var enabledScenePaths = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            foreach (var warning in TopologyRules.FindWarnings(world))
                Debug.LogWarning($"[TopologyBuildGuard] {warning}");

            var violations = TopologyBuildRules.FindViolations(world, enabledScenePaths);
            if (violations.Count == 0)
                return;

            throw new BuildFailedException(
                "World topology is invalid:\n - " + string.Join("\n - ", violations));
        }
    }
}
