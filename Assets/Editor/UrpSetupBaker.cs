using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MMO.Editor
{
    /// <summary>
    /// Configures the project for the Universal Render Pipeline: it bakes a URP asset (with a
    /// universal renderer) and assigns it in two places — <see cref="GraphicsSettings.defaultRenderPipeline"/>
    /// and every quality level's <c>Render Pipeline Asset</c> — so neither has to be set by hand.
    ///
    /// <para>
    /// The project's <c>ProjectSettings</c> were historically not committed, so a fresh clone
    /// regenerates Built-in Render Pipeline defaults even though the URP package is installed and the
    /// runtime prefers the <c>Universal Render Pipeline/Lit</c> shader. Running this once restores the
    /// intended pipeline; the shaders are baked via <see cref="ResourceReloader"/> so the generated
    /// asset is complete rather than a shell the Inspector would have to repair.
    /// </para>
    /// </summary>
    public static class UrpSetupBaker
    {
        public const string MenuPath = "Tools/MMO/Configure URP";

        /// <summary>Folder the generated pipeline assets live in, relative to the project root.</summary>
        public const string SettingsFolder = "Assets/Settings";

        /// <summary>Where the saved <see cref="UniversalRenderPipelineAsset"/> ends up.</summary>
        public const string PipelineAssetPath =
            SettingsFolder + "/MMO_Universal_Render_Pipeline_Asset.asset";

        /// <summary>Where the saved <see cref="UniversalRendererData"/> ends up.</summary>
        public const string RendererAssetPath =
            SettingsFolder + "/MMO_Universal_Renderer.asset";

        [MenuItem(MenuPath)]
        public static void ConfigureUrpMenu()
        {
            UniversalRenderPipelineAsset pipeline = EnsureUrp();
            if (pipeline == null)
                return;

            Selection.activeObject = pipeline;
            EditorGUIUtility.PingObject(pipeline);
        }

        /// <summary>
        /// Returns the project's URP asset, creating and assigning it on the first call. Idempotent:
        /// a second call re-assigns the asset already on disk rather than rebuilding it.
        /// </summary>
        public static UniversalRenderPipelineAsset EnsureUrp()
        {
            UniversalRenderPipelineAsset existing =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null)
            {
                AssignPipeline(existing);
                return existing;
            }

            EnsureFolder();

            // A universal renderer is what actually draws the scene; the pipeline asset only points
            // at it. Reload the renderer's shader references so it is usable, not an empty shell.
            UniversalRendererData renderer =
                AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererAssetPath);
                ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            }

            // Create() wires the renderer into the pipeline and reloads the pipeline's own shaders.
            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);

            AssignPipeline(pipeline);
            return pipeline;
        }

        /// <summary>
        /// Points both the default render pipeline and every quality level at <paramref name="pipeline"/>.
        /// Quality levels default to null ("inherit from Graphics"), but writing them explicitly keeps
        /// the Quality settings readable and avoids a level silently falling back to Built-in.
        /// </summary>
        private static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;

            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }

            QualitySettings.SetQualityLevel(current, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Creates the folder the assets are saved into. <see cref="AssetDatabase.CreateFolder"/>
        /// rather than <c>Directory.CreateDirectory</c> so Unity generates the matching .meta.
        /// </summary>
        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(SettingsFolder))
                return;

            string parent = Path.GetDirectoryName(SettingsFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(SettingsFolder);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", leaf);
            else
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
