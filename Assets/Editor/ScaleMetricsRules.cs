using System.Collections.Generic;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>Scale-contract checks over a scene's name-convention kit/door/stair objects.</summary>
    public static class ScaleMetricsRules
    {
        public static IEnumerable<string> ValidateScene(Scene scene)
        {
            var results = new List<string>();

            foreach (GameObject root in scene.GetRootGameObjects())
                results.AddRange(ValidateGameObject(root, scene.path));

            return results;
        }

        public static IEnumerable<string> ValidateGameObject(GameObject go, string scenePath)
        {
            var results = new List<string>();
            string name = go.name;

            if (name.Contains("Kit", System.StringComparison.OrdinalIgnoreCase))
                results.AddRange(ValidateKitPiece(go, scenePath));

            if (name.Contains("Door", System.StringComparison.OrdinalIgnoreCase))
                results.AddRange(ValidateDoorway(go, scenePath));

            if (name.Contains("Stair", System.StringComparison.OrdinalIgnoreCase))
                results.AddRange(ValidateStair(go, scenePath));

            foreach (Transform child in go.transform)
                results.AddRange(ValidateGameObject(child.gameObject, scenePath));

            return results;
        }

        private static IEnumerable<string> ValidateKitPiece(GameObject go, string scenePath)
        {
            var results = new List<string>();
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers)
            {
                Bounds bounds = r.bounds;
                float width = bounds.size.x;
                float depth = bounds.size.z;

                if (!ScaleMetrics.OnGrid(width, ScaleMetrics.GridSmall) ||
                    !ScaleMetrics.OnGrid(depth, ScaleMetrics.GridSmall))
                {
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
            var colliders = go.GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders)
            {
                float stepHeight = c.bounds.size.y;
                if (stepHeight > ScaleMetrics.MaxStepHeight + 0.001f)
                {
                    results.Add($"[ScaleMetrics] {scenePath}: Stair '{go.name}' has step height {stepHeight:F3}m, exceeds maximum {ScaleMetrics.MaxStepHeight:F3}m.");
                }
            }

            var controller = go.GetComponentInChildren<CharacterController>();
            if (controller != null && controller.stepOffset > ScaleMetrics.MaxStepHeight + 0.001f)
            {
                results.Add($"[ScaleMetrics] {scenePath}: CharacterController on '{go.name}' has stepOffset {controller.stepOffset:F3}m, exceeds maximum {ScaleMetrics.MaxStepHeight:F3}m.");
            }

            return results;
        }
    }
}
