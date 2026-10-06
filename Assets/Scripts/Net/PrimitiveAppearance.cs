using UnityEngine;
using UnityEngine.Rendering;

namespace MMO.Net
{
    /// <summary>
    /// Shared look-and-feel for the primitives this project builds in code.
    ///
    /// Unity primitives ship with a built-in material that renders magenta under a Scriptable
    /// Render Pipeline, so every factory-built object swaps it for the active pipeline's default.
    /// Both <see cref="PlayerPrefabFactory"/> and <see cref="TargetDummyFactory"/> go through here
    /// so the behaviour (and the tests that pin it down) lives in exactly one place.
    /// </summary>
    internal static class PrimitiveAppearance
    {
        /// <summary>
        /// Replaces the renderer's material with the active render pipeline's default, if it has
        /// one. Without this a primitive in a URP project renders magenta.
        /// </summary>
        /// <remarks>
        /// <c>sharedMaterial</c>, not <c>material</c>: no per-renderer material instance to leak.
        /// </remarks>
        public static void ApplyRenderPipelineMaterial(GameObject go)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Material pipelineDefault = pipeline != null ? pipeline.defaultMaterial : null;
            if (pipelineDefault == null)
                return;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = pipelineDefault;
        }
    }
}