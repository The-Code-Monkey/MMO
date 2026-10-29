using System;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the generated ground maps, then builds the matching URP Lit material and 2 m ground
    /// prefab. The baker is idempotent: a second run updates the texture import settings, material
    /// and prefab instead of creating duplicates. Scene geometry is deliberately untouched.
    /// </summary>
    public static class GroundAssetBaker
    {
        public const string GrassMenuPath = "Tools/MMO/Rebuild Ground Grass Assets";
        public const string CobblestoneMenuPath = "Tools/MMO/Rebuild Ground Cobblestone Assets";
        public const string CobblestoneWornMenuPath = "Tools/MMO/Rebuild Ground Worn Cobblestone Assets";

        public const string TextureFolder = "Assets/Textures/Ground";
        public const string MaterialFolder = "Assets/Materials/Ground";
        public const string PrefabFolder = "Assets/Prefabs/Ground";

        public const string GrassAlbedoPath = TextureFolder + "/Grass_Albedo.png";
        public const string GrassNormalPath = TextureFolder + "/Grass_Normal.png";
        public const string GrassMaskPath = TextureFolder + "/Grass_Mask.png";
        public const string GrassMaterialPath = MaterialFolder + "/Ground_Grass.mat";
        public const string GrassPrefabPath = PrefabFolder + "/Ground_Grass.prefab";

        public const string CobblestoneAlbedoPath = TextureFolder + "/Cobblestone_Albedo.png";
        public const string CobblestoneNormalPath = TextureFolder + "/Cobblestone_Normal.png";
        public const string CobblestoneMaskPath = TextureFolder + "/Cobblestone_Mask.png";
        public const string CobblestoneMaterialPath = MaterialFolder + "/Ground_Cobblestone.mat";
        public const string CobblestonePrefabPath = PrefabFolder + "/Ground_Cobblestone.prefab";

        public const string CobblestoneWornAlbedoPath = TextureFolder + "/Cobblestone_Worn_Albedo.png";
        public const string CobblestoneWornNormalPath = TextureFolder + "/Cobblestone_Worn_Normal.png";
        public const string CobblestoneWornMaskPath = TextureFolder + "/Cobblestone_Worn_Mask.png";
        public const string CobblestoneWornMaterialPath = MaterialFolder + "/Ground_Cobblestone_Worn.mat";
        public const string CobblestoneWornPathPrefabPath = PrefabFolder + "/Path_Cobblestone_Worn.prefab";

        private const string LitShaderName = "Universal Render Pipeline/Lit";

        [MenuItem(GrassMenuPath)]
        public static void RebuildGrass()
        {
            Rebuild(
                "grass",
                GrassAlbedoPath,
                GrassNormalPath,
                GrassMaskPath,
                GrassMaterialPath,
                "Ground_Grass",
                GrassPrefabPath,
                overlayPrefab: false);
        }

        [MenuItem(CobblestoneMenuPath)]
        public static void RebuildCobblestone()
        {
            Rebuild(
                "cobblestone",
                CobblestoneAlbedoPath,
                CobblestoneNormalPath,
                CobblestoneMaskPath,
                CobblestoneMaterialPath,
                "Ground_Cobblestone",
                CobblestonePrefabPath,
                overlayPrefab: false);
        }

        [MenuItem(CobblestoneWornMenuPath)]
        public static void RebuildCobblestoneWorn()
        {
            Rebuild(
                "worn cobblestone path",
                CobblestoneWornAlbedoPath,
                CobblestoneWornNormalPath,
                CobblestoneWornMaskPath,
                CobblestoneWornMaterialPath,
                "Ground_Cobblestone_Worn",
                CobblestoneWornPathPrefabPath,
                overlayPrefab: true);
        }

        private static void Rebuild(
            string surfaceName,
            string albedoPath,
            string normalPath,
            string maskPath,
            string materialPath,
            string materialName,
            string prefabPath,
            bool overlayPrefab)
        {
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            ConfigureTexture(albedoPath, normalMap: false, srgb: true, alpha: false);
            ConfigureTexture(normalPath, normalMap: true, srgb: false, alpha: false);
            ConfigureTexture(maskPath, normalMap: false, srgb: false, alpha: true);

            Material material = EnsureMaterial(materialPath, materialName, albedoPath, normalPath, maskPath);
            EnsurePrefab(prefabPath, materialName, material, overlayPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Rebuilt {surfaceName} ground assets: {materialPath}, {prefabPath}");
        }

        internal static void ConfigureTexture(string path, bool normalMap, bool srgb, bool alpha, int maxSize = 1024)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null)
                throw new InvalidOperationException($"Ground texture missing or not importable at '{path}'.");

            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        internal static Material EnsureMaterial(
            string materialPath,
            string materialName,
            string albedoPath,
            string normalPath,
            string maskPath)
        {
            Shader shader = Shader.Find(LitShaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{LitShaderName}' not found. Run Tools > MMO > Configure URP.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.name = materialName;
            material.SetTexture("_BaseMap", Texture(albedoPath));
            material.SetTexture("_MainTex", Texture(albedoPath));
            material.SetTexture("_BumpMap", Texture(normalPath));
            material.SetTexture("_MetallicGlossMap", Texture(maskPath));
            material.SetTexture("_OcclusionMap", Texture(maskPath));

            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 0.35f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            material.renderQueue = -1;
            material.enableInstancing = true;
            material.SetOverrideTag("RenderType", "Opaque");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsurePrefab(string prefabPath, string prefabName, Material material, bool overlayPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                GameObject temporary = GameObject.CreatePrimitive(
                    overlayPrefab ? PrimitiveType.Plane : PrimitiveType.Cube);
                temporary.name = prefabName;
                temporary.transform.localScale = overlayPrefab
                    ? new Vector3(0.2f, 0.2f, 0.2f)
                    : new Vector3(2f, 0.1f, 2f);
                temporary.GetComponent<MeshRenderer>().sharedMaterial = material;
                GameObjectUtility.SetStaticEditorFlags(temporary, StaticEditorFlags.BatchingStatic);

                if (overlayPrefab)
                {
                    Collider collider = temporary.GetComponent<Collider>();
                    if (collider != null)
                        UnityEngine.Object.DestroyImmediate(collider);
                }

                PrefabUtility.SaveAsPrefabAsset(temporary, prefabPath);
                UnityEngine.Object.DestroyImmediate(temporary);
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            contents.name = prefabName;
            contents.transform.localScale = overlayPrefab
                ? new Vector3(0.2f, 0.2f, 0.2f)
                : new Vector3(2f, 0.1f, 2f);
            contents.GetComponent<MeshRenderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static Texture2D Texture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException($"Ground texture missing at '{path}'.");
            return texture;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            if (slash <= 0)
                throw new InvalidOperationException($"Cannot create root folder '{path}'.");

            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
