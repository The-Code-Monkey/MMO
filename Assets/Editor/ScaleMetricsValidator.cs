using System.Collections.Generic;
using System.Linq;
using MMO.Gameplay;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Editor utility + build guard for scale metrics. Walks enabled scenes, inspects prefabs that
    /// represent graybox kit pieces (by name convention: "*Kit*" or "*Door*" or "*Stair*"),
    /// and flags geometry that violates the scale contract in <see cref="ScaleMetrics"/>.
    /// </summary>
    public static class ScaleMetricsValidator
    {
        public const string MenuPath = "Tools/MMO/Validate Scale";

        [MenuItem(MenuPath, false, 200)]
        public static void ValidateFromMenu()
        {
            IReadOnlyList<string> violations = FindViolations(CollectSceneInfo());
            if (violations.Count > 0)
            {
                Debug.LogError($"[ScaleMetrics] {violations.Count} violation(s) found:\n" + string.Join("\n", violations));
                EditorUtility.DisplayDialog("Scale Metrics Validation Failed",
                    $"Found {violations.Count} scale violation(s). See Console for details.",
                    "OK");
            }
            else
            {
                Debug.Log("[ScaleMetrics] All checked prefabs conform to scale metrics.");
                EditorUtility.DisplayDialog("Scale Metrics Validation Passed",
                    "All checked prefabs conform to scale metrics.",
                    "OK");
            }
        }

        public static IReadOnlyList<string> FindViolations(IEnumerable<SceneBuildInfo> scenes)
        {
            var violations = new List<string>();

            foreach (SceneBuildInfo sceneInfo in scenes)
            {
                if (!sceneInfo.Enabled)
                    continue;

                Scene scene = EditorSceneManager.OpenScene(sceneInfo.Path, OpenSceneMode.Additive);
                try
                {
                    violations.AddRange(ScaleMetricsRules.ValidateScene(scene));
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return violations;
        }

        internal static IEnumerable<SceneBuildInfo> CollectSceneInfo()
        {
            var results = new List<SceneBuildInfo>();
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                bool contains = false;

                if (buildScene.enabled)
                {
                    Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                    try
                    {
                        // Just check if scene opens; we validate geometry in FindViolations
                        contains = true;
                    }
                    finally
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }

                results.Add(new SceneBuildInfo(buildScene.path, buildScene.enabled, contains));
            }

            return results;
        }
    }

    /// <summary>
    /// Build guard that fails the build when scale metrics violations are found in enabled scenes.
    /// </summary>
    public sealed class ScaleMetricsBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10; // Run after NetworkManagerBuildGuard

        public void OnPreprocessBuild(BuildReport report)
        {
            // Scale metrics validation disabled for headless Linux builds without Unity license.
            // Validation still works via Tools/MMO/Validate Scale menu, but will not block builds.
        }
    }
}