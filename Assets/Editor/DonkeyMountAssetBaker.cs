using System.IO;
using MMO.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the donkey FBX, builds its URP material and saves the cosmetic prefab
    /// <see cref="MountPrefabFactory"/> drops over the generic mount body. The model is static:
    /// the supplied FBX contains no skeleton or animation clips.
    /// </summary>
    public static class DonkeyMountAssetBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Donkey Mount Assets";

        public const string ModelPath = "Assets/Models/Mounts/Donkey/Donkey.fbx";
        public const string AlbedoPath = "Assets/Textures/Mounts/Donkey/Donkey_Albedo.jpg";
        public const string MetallicPath = "Assets/Textures/Mounts/Donkey/Donkey_Metallic.jpg";
        public const string RoughnessPath = "Assets/Textures/Mounts/Donkey/Donkey_Roughness.jpg";
        public const string MaskPath = "Assets/Textures/Mounts/Donkey/Donkey_Mask.png";
        public const string MaterialPath = "Assets/Materials/Mounts/Donkey.mat";
        public const string PrefabPath = "Assets/Prefabs/DonkeyMount.prefab";
        public const string MountDataPath = "Assets/Resources/Mounts/Donkey.asset";

        private const float DonkeySize = 1.3f;
        private const float DonkeyGroundDrop = -0.9f;
        private const float DonkeySeatHeight = 0.7f;

        [MenuItem(MenuPath)]
        public static void RebuildMenu() => Rebuild();

        /// <summary>
        /// Builds any missing donkey asset. A later bake reuses the saved prefab rather than
        /// re-importing the FBX and rebuilding the mask on every run.
        /// </summary>
        public static GameObject EnsureAssets()
        {
            return AssetsAreUsable() ? LoadPrefab() : Rebuild();
        }

        public static GameObject Rebuild()
        {
            EnsureFolder(ModelPath);
            EnsureFolder(MaskPath);
            EnsureFolder(MaterialPath);
            EnsureFolder(PrefabPath);

            if (!File.Exists(AbsolutePath(ModelPath)))
            {
                Debug.LogError(
                    $"Missing donkey model at '{ModelPath}'. Copy the FBX into the project before baking the mount.");
                return null;
            }

            ConfigureModel();
            ConfigureTextures();

            if (!BuildMask())
                return null;

            ConfigureTexture(MetallicPath, false, false, 2048);
            ConfigureTexture(RoughnessPath, false, false, 2048);

            Material material = BuildMaterial();
            if (material == null)
                return null;

            GameObject prefab = BuildPrefab(material);
            if (prefab == null)
                return null;

            if (!AssignToMountData(prefab))
                return null;

            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static bool AssetsAreUsable()
        {
            GameObject prefab = LoadPrefab();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            var data = AssetDatabase.LoadAssetAtPath<MountData>(MountDataPath);
            if (prefab == null || material == null || mask == null || data == null)
                return false;

            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            if (renderer == null || renderer.sharedMaterial != material)
                return false;

            var serialized = new SerializedObject(data);
            SerializedProperty model = serialized.FindProperty("_modelPrefab");
            return model != null && model.objectReferenceValue == prefab;
        }

        private static GameObject LoadPrefab()
            => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        private static void ConfigureModel()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"Could not import donkey model '{ModelPath}'.");
                return;
            }

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeGameObjects = false;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        private static void ConfigureTextures()
        {
            ConfigureTexture(AlbedoPath, true, false, 2048);
            ConfigureTexture(MetallicPath, false, true, 2048);
            ConfigureTexture(RoughnessPath, false, true, 2048);
        }

        private static void ConfigureTexture(string path, bool sRGB, bool readable, int maxSize)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Could not import donkey texture '{path}'.");
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// URP Lit packs metallic into R and smoothness into A. The source ships them as separate
        /// greyscale images, so generate the combined mask once instead of adding a custom shader.
        /// </summary>
        private static bool BuildMask()
        {
            var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicPath);
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(RoughnessPath);
            if (metallic == null || roughness == null)
            {
                Debug.LogError("Donkey metallic/roughness maps are missing; cannot build the URP mask.");
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

            return true;
        }

        private static Material BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("URP Lit shader not found; the donkey would render magenta.");
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
            Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);

            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);

            if (mask != null)
                material.EnableKeyword("_METALLICGLOSSMAP");

            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildPrefab(Material material)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"Donkey model asset '{ModelPath}' did not import.");
                return null;
            }

            GameObject prefab = Object.Instantiate(model);
            try
            {
                prefab.name = "DonkeyMount";

                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    int count = Mathf.Max(1, renderer.sharedMaterials.Length);
                    var materials = new Material[count];
                    for (int i = 0; i < count; i++)
                        materials[i] = material;

                    renderer.sharedMaterials = materials;
                }

                foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);

                return PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        private static bool AssignToMountData(GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<MountData>(MountDataPath);
            if (data == null)
            {
                Debug.LogError($"No mount data asset at '{MountDataPath}'.");
                return false;
            }

            var serialized = new SerializedObject(data);
            serialized.FindProperty("_modelPrefab").objectReferenceValue = prefab;
            serialized.FindProperty("_size").floatValue = DonkeySize;
            serialized.FindProperty("_visualOffset").vector3Value = new Vector3(0f, DonkeyGroundDrop, 0f);
            serialized.FindProperty("_riderOffset").vector3Value = new Vector3(0f, DonkeySeatHeight, 0f);
            serialized.FindProperty("_visualYawOffset").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            return true;
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
