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
                    violations.AddRange(ValidateScene(scene));
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return violations;
        }

        private static IEnumerable<string> ValidateScene(Scene scene)
        {
            var results = new List<string>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                results.AddRange(ValidateGameObject(root, scene.path));
            }

            return results;
        }

        private static IEnumerable<string> ValidateGameObject(GameObject go, string scenePath)
        {
            var results = new List<string>();

            // Check if this looks like a kit piece, doorway, or stair by name convention
            string name = go.name;
            bool isKitPiece = name.Contains("Kit", System.StringComparison.OrdinalIgnoreCase);
            bool isDoorway = name.Contains("Door", System.StringComparison.OrdinalIgnoreCase);
            bool isStair = name.Contains("Stair", System.StringComparison.OrdinalIgnoreCase);

            if (isKitPiece)
            {
                results.AddRange(ValidateKitPiece(go, scenePath));
            }

            if (isDoorway)
            {
                results.AddRange(ValidateDoorway(go, scenePath));
            }

            if (isStair)
            {
                results.AddRange(ValidateStair(go, scenePath));
            }

            // Recurse into children
            foreach (Transform child in go.transform)
            {
                results.AddRange(ValidateGameObject(child.gameObject, scenePath));
            }

            return results;
        }

        private static IEnumerable<string> ValidateKitPiece(GameObject go, string scenePath)
        {
            var results = new List<string>();

            // Check renderer bounds for grid alignment
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                Bounds bounds = r.bounds;
                float width = bounds.size.x;
                float depth = bounds.size.z;

                // Check alignment to small grid (1m)
                if (!ScaleMetrics.OnGrid(width, ScaleMetrics.GridSmall) ||
                    !ScaleMetrics.OnGrid(depth, ScaleMetrics.GridSmall))
                {
                    // Also check large grid (2m) as alternative
                    if (!ScaleMetrics.OnGrid(width, ScaleMetrics.GridLarge) ||
                        !ScaleMetrics.OnGrid(depth, ScaleMetrics.GridLarge))
                    {
                        results.Add($"[ScaleMetrics] {scenePath}: Kit piece '{go.name}' has off-grid footprint ({width:F2}m × {depth:F2}m). Expected multiples of {ScaleMetrics.GridSmall}m or {ScaleMetrics.GridLarge}m.");
                    }
                }
            }

            return results;
        }

        private static IEnumerable<string> ValidateDoorway(GameObject go, string scenePath)
        {
            var results = new List<string>();

            // Look for a collider that represents the doorway opening
            var colliders = go.GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders)
            {
                Bounds bounds = c.bounds;
                float width = bounds.size.x;
                float height = bounds.size.y;

                if (!ScaleMetrics.AdmitsCharacter(width, height))
                {
                    results.Add($"[ScaleMetrics] {scenePath}: Doorway '{go.name}' ({width:F2}m × {height:F2}m) does not admit a standing character. Minimum: {ScaleMetrics.CharacterRadius * 2f:F2}m wide, {ScaleMetrics.CharacterHeight + ScaleMetrics.CharacterClearance:F2}m tall.");
                }
            }

            return results;
        }

        private static IEnumerable<string> ValidateStair(GameObject go, string scenePath)
        {
            var results = new List<string>();

            // Check for step height via collider bounds or explicit step height
            var colliders = go.GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders)
            {
                Bounds bounds = c.bounds;
                float stepHeight = bounds.size.y;

                if (stepHeight > ScaleMetrics.MaxStepHeight + 0.001f)
                {
                    results.Add($"[ScaleMetrics] {scenePath}: Stair '{go.name}' has step height {stepHeight:F3}m, exceeds maximum {ScaleMetrics.MaxStepHeight:F3}m.");
                }
            }

            // Also check CharacterController stepOffset if present
            var controller = go.GetComponentInChildren<CharacterController>();
            if (controller != null && controller.stepOffset > ScaleMetrics.MaxStepHeight + 0.001f)
            {
                results.Add($"[ScaleMetrics] {scenePath}: CharacterController on '{go.name}' has stepOffset {controller.stepOffset:F3}m, exceeds maximum {ScaleMetrics.MaxStepHeight:F3}m.");
            }

            return results;
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
            IReadOnlyList<string> violations = ScaleMetricsValidator.FindViolations(ScaleMetricsValidator.CollectSceneInfo());
            if (violations.Count > 0)
            {
                throw new BuildFailedException(
                    "Scale metrics violations in enabled scenes:\n" + string.Join("\n", violations));
            }
        }
    }
}