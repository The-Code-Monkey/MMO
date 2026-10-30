using System.IO;
using System.Linq;
using MMO.Client;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the rigged character FBX, builds its URP material and AnimatorController, and saves
    /// the cosmetic visual prefab that <see cref="PlayerPrefabAuthoring"/> nests under the player.
    ///
    /// The model is deliberately kept off the networked player root: the root's component set is
    /// what FishNet numbers its NetworkBehaviours by, so the visual is a separate prefab whose GUID
    /// the baked player prefab references.
    /// </summary>
    public static class PlayerCharacterAssetBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Player Character Assets";

        public const string ModelPath = "Assets/Models/Player/Base.fbx";
        public const string AlbedoPath = "Assets/Textures/Player/Player_Albedo.png";
        public const string NormalPath = "Assets/Textures/Player/Player_Normal.png";
        public const string MetallicPath = "Assets/Textures/Player/Player_Metallic.png";
        public const string RoughnessPath = "Assets/Textures/Player/Player_Roughness.png";
        public const string MaskPath = "Assets/Textures/Player/Player_Mask.png";
        public const string MaterialPath = "Assets/Materials/Player/Player.mat";
        public const string ControllerPath = "Assets/Animations/Player/Player.controller";
        public const string VisualPrefabPath = "Assets/Prefabs/PlayerVisual.prefab";

        public const string VisualPrefabName = "PlayerVisual";
        public const string IdleClipName = "Idle";
        public const string WalkClipName = "Walk";
        public const string RunClipName = "Run";
        public const string DyingClipName = "Dying";
        public const string SpeedParameterName = "Speed";
        public const string DeadParameterName = "Dead";

        public sealed class PlayerCharacterAssets
        {
            public GameObject VisualPrefab;
            public Material Material;
            public RuntimeAnimatorController Controller;
            public AnimationClip Idle;
            public AnimationClip Walk;
            public AnimationClip Run;
            public AnimationClip Dying;
        }

        [MenuItem(MenuPath)]
        public static void RebuildMenu() => Rebuild();

        /// <summary>
        /// Builds any missing player-character asset. A later bake reuses the saved prefab rather
        /// than re-importing a 13 MB FBX on every run.
        /// </summary>
        public static PlayerCharacterAssets EnsureAssets()
        {
            return AssetsAreUsable() ? LoadAssets() : Rebuild();
        }

        /// <summary>
        /// Checks the saved prefab's references, not just the files. Meta files are regenerated on
        /// a fresh checkout, so a prefab can exist while its controller, avatar or material
        /// reference is stale; in that case the assets must be rebuilt before the player is baked.
        /// </summary>
        private static bool AssetsAreUsable()
        {
            var visual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            if (visual == null || controller == null || material == null || mask == null)
                return false;

            Animator animator = visual.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || animator.avatar == null)
                return false;

            Renderer renderer = visual.GetComponentInChildren<Renderer>(true);
            if (renderer == null || renderer.sharedMaterial == null)
                return false;

            return material.GetTexture("_BaseMap") != null &&
                   material.GetTexture("_MetallicGlossMap") != null;
        }

        /// <summary>Rebuilds the import settings, mask, material, controller and visual prefab.</summary>
        public static PlayerCharacterAssets Rebuild()
        {
            EnsureFolder(ModelPath);
            EnsureFolder(MaterialPath);
            EnsureFolder(ControllerPath);
            EnsureFolder(MaskPath);
            EnsureFolder(VisualPrefabPath);

            if (!File.Exists(AbsolutePath(ModelPath)))
            {
                Debug.LogError(
                    $"Missing character model at '{ModelPath}'. Copy the rigged FBX into the project before baking the player.");
                return null;
            }

            ConfigureTextures();
            if (!BuildMask())
                return null;

            ConfigureModel();

            var clips = LoadClips();
            if (clips == null)
                return null;

            Material material = BuildMaterial();
            if (material == null)
                return null;

            RuntimeAnimatorController controller = BuildController(clips);
            if (controller == null)
                return null;

            GameObject visualPrefab = BuildVisualPrefab(material, controller);
            if (visualPrefab == null)
                return null;

            AssetDatabase.SaveAssets();
            return LoadAssets();
        }

        private static PlayerCharacterAssets LoadAssets()
        {
            var clips = LoadClips();
            return new PlayerCharacterAssets
            {
                VisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath),
                Material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath),
                Controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath),
                Idle = clips?.Idle,
                Walk = clips?.Walk,
                Run = clips?.Run,
                Dying = clips?.Dying
            };
        }

        private static void ConfigureTextures()
        {
            ConfigureTexture(AlbedoPath, TextureImporterType.Default, true, false, 2048);
            ConfigureTexture(NormalPath, TextureImporterType.NormalMap, false, false, 2048);
            ConfigureTexture(MetallicPath, TextureImporterType.Default, false, true, 2048);
            ConfigureTexture(RoughnessPath, TextureImporterType.Default, false, true, 2048);
        }

        private static void ConfigureTexture(
            string path,
            TextureImporterType type,
            bool sRGB,
            bool readable,
            int maxSize)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Could not import character texture '{path}'.");
                return;
            }

            importer.textureType = type;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// URP Lit packs metallic into R and smoothness into A of one map. The source ships them as
        /// separate greyscale images, so a small generated mask is cheaper than a custom shader.
        /// </summary>
        private static bool BuildMask()
        {
            var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(RoughnessPath);
            if (metallic == null || roughness == null)
            {
                Debug.LogError("Character metallic/roughness maps are missing; cannot build the URP mask.");
                return false;
            }

            int width = Mathf.Min(metallic.width, roughness.width);
            int height = Mathf.Min(metallic.height, roughness.height);
            Color32[] metallicPixels = metallic.GetPixels32();
            Color32[] roughnessPixels = roughness.GetPixels32();
            var maskPixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                int metallicRow = y * metallic.width;
                int roughnessRow = y * roughness.width;
                int maskRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    byte metal = metallicPixels[metallicRow + x].r;
                    byte smoothness = (byte)(255 - roughnessPixels[roughnessRow + x].r);
                    maskPixels[maskRow + x] = new Color32(metal, 255, 255, smoothness);
                }
            }

            var mask = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                mask.SetPixels32(maskPixels);
                mask.Apply(false, false);
                File.WriteAllBytes(AbsolutePath(MaskPath), mask.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(mask);
            }

            AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceSynchronousImport);
            var maskImporter = AssetImporter.GetAtPath(MaskPath) as TextureImporter;
            if (maskImporter != null)
            {
                maskImporter.textureType = TextureImporterType.Default;
                maskImporter.sRGBTexture = false;
                maskImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                maskImporter.alphaIsTransparency = false;
                maskImporter.mipmapEnabled = true;
                maskImporter.maxTextureSize = 2048;
                maskImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                maskImporter.isReadable = false;
                maskImporter.SaveAndReimport();
            }

            ConfigureTexture(MetallicPath, TextureImporterType.Default, false, false, 2048);
            ConfigureTexture(RoughnessPath, TextureImporterType.Default, false, false, 2048);
            return true;
        }

        private static void ConfigureModel()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Could not import character model '{ModelPath}'.");
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeGameObjects = false;
            importer.isReadable = false;
            importer.SaveAndReimport();

            var clipImporter = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            ModelImporterClipAnimation[] clips = clipImporter?.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                Debug.LogError($"Character model '{ModelPath}' contains no animation takes.");
                return;
            }

            foreach (ModelImporterClipAnimation clip in clips)
            {
                string take = clip.takeName ?? string.Empty;
                if (take.Contains("StandingIdle"))
                    RenameClip(clip, IdleClipName, loop: true);
                else if (take.Contains("Walking"))
                    RenameClip(clip, WalkClipName, loop: true);
                else if (take.Contains("Sprint"))
                    RenameClip(clip, RunClipName, loop: true);
                else if (take.Contains("Dying"))
                    RenameClip(clip, DyingClipName, loop: false);
            }

            clipImporter.clipAnimations = clips;
            clipImporter.SaveAndReimport();
        }

        private static void RenameClip(ModelImporterClipAnimation clip, string name, bool loop)
        {
            clip.name = name;
            clip.loopTime = loop;
            clip.loopPose = loop;
        }

        private static PlayerCharacterAssets LoadClips()
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .ToArray();

            AnimationClip idle = clips.FirstOrDefault(clip => clip.name == IdleClipName);
            AnimationClip walk = clips.FirstOrDefault(clip => clip.name == WalkClipName);
            AnimationClip run = clips.FirstOrDefault(clip => clip.name == RunClipName);
            AnimationClip dying = clips.FirstOrDefault(clip => clip.name == DyingClipName);

            if (idle == null || walk == null || run == null)
            {
                Debug.LogError(
                    $"Expected clips '{IdleClipName}', '{WalkClipName}' and '{RunClipName}' on '{ModelPath}'.");
                return null;
            }

            return new PlayerCharacterAssets { Idle = idle, Walk = walk, Run = run, Dying = dying };
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("URP Lit shader not found; the player model would render magenta.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);

            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);

            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            if (mask != null)
                material.EnableKeyword("_METALLICGLOSSMAP");

            EditorUtility.SetDirty(material);
            return material;
        }

        private static RuntimeAnimatorController BuildController(PlayerCharacterAssets clips)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"Could not create AnimatorController at '{ControllerPath}'.");
                return null;
            }

            controller.AddParameter(SpeedParameterName, AnimatorControllerParameterType.Float);
            controller.AddParameter(DeadParameterName, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            var blendTree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = SpeedParameterName,
                useAutomaticThresholds = false,
                children = new[]
                {
                    new ChildMotion { motion = clips.Idle, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = clips.Walk, threshold = 0.45f, timeScale = 1.15f },
                    new ChildMotion { motion = clips.Run, threshold = 1f, timeScale = 1f }
                }
            };
            AssetDatabase.AddObjectToAsset(blendTree, controller);

            AnimatorState locomotion = stateMachine.AddState("Locomotion", new Vector3(300f, 100f, 0f));
            locomotion.motion = blendTree;
            stateMachine.defaultState = locomotion;

            if (clips.Dying != null)
            {
                AnimatorState dying = stateMachine.AddState(DyingClipName, new Vector3(300f, 240f, 0f));
                dying.motion = clips.Dying;

                AnimatorStateTransition toDying = stateMachine.AddAnyStateTransition(dying);
                toDying.hasExitTime = false;
                toDying.hasFixedDuration = true;
                toDying.duration = 0.1f;
                toDying.AddCondition(AnimatorConditionMode.If, 0f, DeadParameterName);

                AnimatorStateTransition toLocomotion = dying.AddTransition(locomotion);
                toLocomotion.hasExitTime = false;
                toLocomotion.hasFixedDuration = true;
                toLocomotion.duration = 0.2f;
                toLocomotion.AddCondition(AnimatorConditionMode.IfNot, 0f, DeadParameterName);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject BuildVisualPrefab(
            Material material,
            RuntimeAnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"Character model asset '{ModelPath}' did not import.");
                return null;
            }

            GameObject visual = Object.Instantiate(model);
            try
            {
                visual.name = VisualPrefabName;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                Animator animator = visual.GetComponent<Animator>();
                if (animator == null)
                    animator = visual.AddComponent<Animator>();

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
                {
                    int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                    var materials = new Material[count];
                    for (int i = 0; i < count; i++)
                        materials[i] = material;

                    renderer.sharedMaterials = materials;
                    if (renderer is SkinnedMeshRenderer skinned)
                        skinned.updateWhenOffscreen = true;
                }

                if (visual.GetComponent<PlayerCharacterVisual>() == null)
                    visual.AddComponent<PlayerCharacterVisual>();

                return PrefabUtility.SaveAsPrefabAsset(visual, VisualPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(visual);
            }
        }

        private static string AbsolutePath(string assetPath)
            => Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".",
                assetPath);

        private static void EnsureFolder(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent + "/placeholder");

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
