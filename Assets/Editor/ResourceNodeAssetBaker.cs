using System;
using System.IO;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the iron, copper, tin, cobalt and veridium surface maps, builds their full and depleted
    /// URP Lit materials and refreshes every <c>ResourceNode</c> prefab (small and large). The prefabs are
    /// rebuilt in place so their GUIDs and every scene reference survive a rebake.
    /// </summary>
    public static class ResourceNodeAssetBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Resource Node Assets";

        public const string TextureFolder = "Assets/Textures/Resources";
        public const string MaterialFolder = "Assets/Materials/Resources";
        public const string ModelFolder = "Assets/Models/Resources/IronOre";
        public const string StoneModelFolder = "Assets/Models/Resources/StoneOre";

        public const string OreModelPath = ModelFolder + "/IronOre.fbx";
        public const string StoneEmptyModelPath = StoneModelFolder + "/StoneOreEmpty.fbx";
        public const string OreLargeModelPath = ModelFolder + "/IronOreLarge.fbx";
        public const string StoneLargeEmptyModelPath = StoneModelFolder + "/StoneOreLargeEmpty.fbx";
        public const string OreDiffusePath = TextureFolder + "/IronOre/IronOre_Diffuse.png";
        public const string OreNormalPath = TextureFolder + "/IronOre/IronOre_Normal.png";
        public const string OreMaskPath = TextureFolder + "/IronOre/IronOre_Mask.png";
        public const string OreEmissivePath = TextureFolder + "/IronOre/IronOre_Emissive.png";
        public const string OreMaterialPath = MaterialFolder + "/IronOre.mat";

        public const string StoneEmptyDiffusePath = TextureFolder + "/StoneOreEmpty/StoneOreEmpty_Diffuse.png";
        public const string StoneEmptyNormalPath = TextureFolder + "/StoneOreEmpty/StoneOreEmpty_Normal.png";
        public const string StoneEmptyMaskPath = TextureFolder + "/StoneOreEmpty/StoneOreEmpty_Mask.png";
        public const string StoneEmptyMaterialPath = MaterialFolder + "/StoneOreEmpty.mat";

        public const string OreLargeDiffusePath = TextureFolder + "/IronOreLarge/IronOreLarge_Diffuse.png";
        public const string OreLargeNormalPath = TextureFolder + "/IronOreLarge/IronOreLarge_Normal.png";
        public const string OreLargeMaskPath = TextureFolder + "/IronOreLarge/IronOreLarge_Mask.png";
        public const string OreLargeMaterialPath = MaterialFolder + "/IronOreLarge.mat";

        public const string StoneLargeEmptyDiffusePath = TextureFolder + "/StoneOreLargeEmpty/StoneOreLargeEmpty_Diffuse.png";
        public const string StoneLargeEmptyNormalPath = TextureFolder + "/StoneOreLargeEmpty/StoneOreLargeEmpty_Normal.png";
        public const string StoneLargeEmptyMaskPath = TextureFolder + "/StoneOreLargeEmpty/StoneOreLargeEmpty_Mask.png";
        public const string StoneLargeEmptyMaterialPath = MaterialFolder + "/StoneOreLargeEmpty.mat";

        public const string LargeDefinitionPath = "Assets/Resources/ResourceDefinitions/Iron_Ore_Large.asset";
        public const string LargePrefabPath = "Assets/Prefabs/ResourceNode_Large.prefab";

        public const string CopperModelFolder = "Assets/Models/Resources/CopperOre";
        public const string TinModelFolder = "Assets/Models/Resources/TinOre";
        public const string CobaltModelFolder = "Assets/Models/Resources/CobaltOre";
        public const string VeridiumModelFolder = "Assets/Models/Resources/VeridiumOre";

        public const string CopperPrefabPath = "Assets/Prefabs/ResourceNode_Copper.prefab";
        public const string CopperLargePrefabPath = "Assets/Prefabs/ResourceNode_Copper_Large.prefab";
        public const string TinPrefabPath = "Assets/Prefabs/ResourceNode_Tin.prefab";
        public const string TinLargePrefabPath = "Assets/Prefabs/ResourceNode_Tin_Large.prefab";
        public const string CobaltPrefabPath = "Assets/Prefabs/ResourceNode_Cobalt.prefab";
        public const string VeridiumPrefabPath = "Assets/Prefabs/ResourceNode_Veridium.prefab";

        public const string CopperDefinitionPath = "Assets/Resources/ResourceDefinitions/Copper_Ore.asset";
        public const string CopperLargeDefinitionPath = "Assets/Resources/ResourceDefinitions/Copper_Ore_Large.asset";
        public const string TinDefinitionPath = "Assets/Resources/ResourceDefinitions/Tin_Ore.asset";
        public const string TinLargeDefinitionPath = "Assets/Resources/ResourceDefinitions/Tin_Ore_Large.asset";
        public const string CobaltDefinitionPath = "Assets/Resources/ResourceDefinitions/Cobalt_Ore.asset";
        public const string VeridiumDefinitionPath = "Assets/Resources/ResourceDefinitions/Veridium_Ore.asset";

        public const string CopperItemPath = "Assets/Resources/Items/Copper_Ore.asset";
        public const string TinItemPath = "Assets/Resources/Items/Tin_Ore.asset";
        public const string CobaltItemPath = "Assets/Resources/Items/Cobalt_Ore.asset";
        public const string VeridiumItemPath = "Assets/Resources/Items/Veridium_Ore.asset";

        /// <summary>
        /// Every node prefab in the order every NetworkManager must register them. FishNet derives
        /// prefab ids from list order, so the menu and zone scenes have to agree exactly.
        /// </summary>
        public static readonly string[] AllPrefabPaths =
        {
            ResourceNodeBaker.PrefabAssetPath,
            LargePrefabPath,
            CopperPrefabPath,
            CopperLargePrefabPath,
            TinPrefabPath,
            TinLargePrefabPath,
            CobaltPrefabPath,
            VeridiumPrefabPath,
        };

        private const float OreTargetHeight = 1.6f;
        private const float ModelYaw = 180f;
        private const float LargeTargetHeight = 2.4f;
        private const int LargeDropAmount = 15;
        private const int SmallDropAmount = 5;

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before rebuilding resource node assets.");
                return;
            }

            foreach (string folder in new[]
                     {
                         TextureFolder + "/IronOre",
                         TextureFolder + "/StoneOreEmpty",
                         TextureFolder + "/IronOreLarge",
                         TextureFolder + "/StoneOreLargeEmpty",
                         TextureFolder + "/CopperOre",
                         TextureFolder + "/CopperOreLarge",
                         TextureFolder + "/TinOre",
                         TextureFolder + "/TinOreLarge",
                         TextureFolder + "/CobaltOre",
                         TextureFolder + "/CobaltOreEmpty",
                         TextureFolder + "/VeridiumOre",
                         TextureFolder + "/VeridiumOreEmpty",
                         MaterialFolder,
                         ModelFolder,
                         StoneModelFolder,
                         CopperModelFolder,
                         TinModelFolder,
                         CobaltModelFolder,
                         VeridiumModelFolder,
                     })
            {
                GroundAssetBaker.EnsureFolder(folder);
            }

            ConfigureTextures(
                OreDiffusePath, OreNormalPath, OreMaskPath, OreEmissivePath);
            ConfigureTextures(
                StoneEmptyDiffusePath, StoneEmptyNormalPath, StoneEmptyMaskPath, null);
            ConfigureTextures(
                OreLargeDiffusePath, OreLargeNormalPath, OreLargeMaskPath, null);
            ConfigureTextures(
                StoneLargeEmptyDiffusePath, StoneLargeEmptyNormalPath, StoneLargeEmptyMaskPath, null);

            foreach (NodeVariant variant in NewVariants)
            {
                EnsureMaskFromPbr(variant.PbrPath, variant.MaskPath);
                ConfigureTextures(variant.DiffusePath, variant.NormalPath, variant.MaskPath, variant.EmissivePath);
                ConfigureModel(variant.ModelPath);

                if (variant.HasOwnEmpty)
                {
                    EnsureMaskFromPbr(variant.EmptyPbrPath, variant.EmptyMaskPath);
                    ConfigureTextures(
                        variant.EmptyDiffusePath, variant.EmptyNormalPath, variant.EmptyMaskPath,
                        variant.EmptyEmissivePath);
                    ConfigureModel(variant.EmptyModelPath);
                }
            }

            ConfigureModel(OreModelPath);
            ConfigureModel(StoneEmptyModelPath);
            ConfigureModel(OreLargeModelPath);
            ConfigureModel(StoneLargeEmptyModelPath);

            Material ore = EnsureOreMaterial();
            Material oreEmpty = EnsureEmptyMaterial();
            Material oreLarge = EnsureLargeMaterial();
            Material oreLargeEmpty = EnsureLargeEmptyMaterial();

            AssetDatabase.SaveAssets();

            NetworkObject prefab = RebuildPrefab(ore, oreEmpty);
            ResourceDefinition largeDefinition = EnsureLargeDefinition();
            RebuildLargePrefab(oreLarge, oreLargeEmpty, largeDefinition);

            foreach (NodeVariant variant in NewVariants)
                RebuildVariant(variant, oreEmpty, oreLargeEmpty);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(prefab != null
                ? $"Rebuilt resource node assets: {ResourceNodeBaker.PrefabAssetPath}, {LargePrefabPath}, " +
                  $"{CopperPrefabPath}, {CopperLargePrefabPath}, {TinPrefabPath}, {TinLargePrefabPath}, " +
                  $"{CobaltPrefabPath}, {VeridiumPrefabPath}"
                : "Resource node asset rebuild failed.");
        }

        private sealed class NodeVariant
        {
            public readonly string Name;
            public readonly string ModelPath;
            public readonly string DiffusePath;
            public readonly string NormalPath;
            public readonly string PbrPath;
            public readonly string MaskPath;
            public readonly string EmissivePath;
            public readonly string MaterialPath;
            public readonly string DefinitionPath;
            public readonly string PrefabPath;
            public readonly string ItemPath;
            public readonly string ItemName;
            public readonly string FullMeshName;
            public readonly int DropAmount;
            public readonly bool Large;

            public readonly string EmptyModelPath;
            public readonly string EmptyMaterialPath;
            public readonly string EmptyTextureName;
            public readonly string EmptyDiffusePath;
            public readonly string EmptyNormalPath;
            public readonly string EmptyPbrPath;
            public readonly string EmptyMaskPath;
            public readonly string EmptyEmissivePath;
            public readonly string EmptyMeshName;

            public NodeVariant(
                string name,
                string modelFolder,
                string textureFolder,
                string materialFolder,
                string definitionFolder,
                string prefabFolder,
                string itemPath,
                string itemName,
                string fullMeshName,
                int dropAmount,
                bool large,
                bool hasEmissive,
                string emptyModelPath = null,
                string emptyTextureFolder = null,
                string emptyTextureName = null,
                bool emptyHasEmissive = false)
            {
                Name = name;
                ModelPath = modelFolder + "/" + name + ".fbx";
                DiffusePath = textureFolder + "/" + name + "_Diffuse.png";
                NormalPath = textureFolder + "/" + name + "_Normal.png";
                PbrPath = textureFolder + "/" + name + "_PBR.png";
                MaskPath = textureFolder + "/" + name + "_Mask.png";
                EmissivePath = hasEmissive ? textureFolder + "/" + name + "_Emissive.png" : null;
                MaterialPath = materialFolder + "/" + name + ".mat";
                string definitionLeaf = large
                    ? name.Replace("OreLarge", "_Ore_Large")
                    : name.Replace("Ore", "_Ore");
                DefinitionPath = definitionFolder + "/" + definitionLeaf + ".asset";
                string prefabLeaf = large
                    ? name.Replace("OreLarge", "") + "_Large"
                    : name.Replace("Ore", "");
                PrefabPath = prefabFolder + "/ResourceNode_" + prefabLeaf + ".prefab";
                ItemPath = itemPath;
                ItemName = itemName;
                FullMeshName = fullMeshName;
                DropAmount = dropAmount;
                Large = large;

                EmptyTextureName = emptyTextureName;
                bool ownEmpty = !string.IsNullOrEmpty(emptyTextureFolder) && !string.IsNullOrEmpty(emptyTextureName);
                HasOwnEmpty = ownEmpty;
                EmptyModelPath = emptyModelPath ?? (large ? StoneLargeEmptyModelPath : StoneEmptyModelPath);
                EmptyMaterialPath = ownEmpty
                    ? materialFolder + "/" + emptyTextureName + ".mat"
                    : large ? StoneLargeEmptyMaterialPath : StoneEmptyMaterialPath;
                EmptyDiffusePath = ownEmpty ? emptyTextureFolder + "/" + emptyTextureName + "_Diffuse.png" : null;
                EmptyNormalPath = ownEmpty ? emptyTextureFolder + "/" + emptyTextureName + "_Normal.png" : null;
                EmptyPbrPath = ownEmpty ? emptyTextureFolder + "/" + emptyTextureName + "_PBR.png" : null;
                EmptyMaskPath = ownEmpty ? emptyTextureFolder + "/" + emptyTextureName + "_Mask.png" : null;
                EmptyEmissivePath = ownEmpty && emptyHasEmissive
                    ? emptyTextureFolder + "/" + emptyTextureName + "_Emissive.png"
                    : null;
                EmptyMeshName = ownEmpty
                    ? emptyTextureName + "Mesh"
                    : large ? "StoneOreLargeEmptyMesh" : "StoneOreEmptyMesh";
            }

            public bool HasOwnEmpty { get; }
            public float TargetHeight => Large ? LargeTargetHeight : OreTargetHeight;
        }

        private const string ItemFolder = "Assets/Resources/Items";
        private const string DefinitionFolder = "Assets/Resources/ResourceDefinitions";
        private const string PrefabFolder = "Assets/Prefabs";

        private static readonly NodeVariant[] NewVariants =
        {
            new NodeVariant(
                "CopperOre", CopperModelFolder, TextureFolder + "/CopperOre", MaterialFolder,
                DefinitionFolder, PrefabFolder, CopperItemPath, "Copper Ore", "CopperOreMesh",
                SmallDropAmount, large: false, hasEmissive: true),
            new NodeVariant(
                "CopperOreLarge", CopperModelFolder, TextureFolder + "/CopperOreLarge", MaterialFolder,
                DefinitionFolder, PrefabFolder, CopperItemPath, "Copper Ore", "CopperOreMesh",
                LargeDropAmount, large: true, hasEmissive: false),
            new NodeVariant(
                "TinOre", TinModelFolder, TextureFolder + "/TinOre", MaterialFolder,
                DefinitionFolder, PrefabFolder, TinItemPath, "Tin Ore", "TinOreMesh",
                SmallDropAmount, large: false, hasEmissive: false),
            new NodeVariant(
                "TinOreLarge", TinModelFolder, TextureFolder + "/TinOreLarge", MaterialFolder,
                DefinitionFolder, PrefabFolder, TinItemPath, "Tin Ore", "TinOreMesh",
                LargeDropAmount, large: true, hasEmissive: false),
            new NodeVariant(
                "CobaltOre", CobaltModelFolder, TextureFolder + "/CobaltOre", MaterialFolder,
                DefinitionFolder, PrefabFolder, CobaltItemPath, "Cobalt Ore", "CobaltOreMesh",
                SmallDropAmount, large: false, hasEmissive: false,
                emptyModelPath: CobaltModelFolder + "/CobaltOreEmpty.fbx",
                emptyTextureFolder: TextureFolder + "/CobaltOreEmpty",
                emptyTextureName: "CobaltOreEmpty",
                emptyHasEmissive: false),
            new NodeVariant(
                "VeridiumOre", VeridiumModelFolder, TextureFolder + "/VeridiumOre", MaterialFolder,
                DefinitionFolder, PrefabFolder, VeridiumItemPath, "Veridium Ore", "VeridiumOreMesh",
                SmallDropAmount, large: false, hasEmissive: true,
                emptyModelPath: VeridiumModelFolder + "/VeridiumOreEmpty.fbx",
                emptyTextureFolder: TextureFolder + "/VeridiumOreEmpty",
                emptyTextureName: "VeridiumOreEmpty",
                emptyHasEmissive: true),
        };

        /// <summary>
        /// Registers every node prefab in <see cref="AllPrefabPaths"/> order. The same order on
        /// every end is what keeps FishNet prefab ids stable across the menu and each zone.
        /// </summary>
        public static int RegisterAllPrefabs(FishNet.Managing.Object.PrefabObjects prefabs)
        {
            if (prefabs == null)
                return 0;

            int registered = 0;
            foreach (string path in AllPrefabPaths)
            {
                NetworkObject prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
                if (prefab != null && ResourceNodePrefabFactory.Register(prefabs, prefab))
                    registered++;
            }

            return registered;
        }

        private static void RebuildVariant(NodeVariant variant, Material empty, Material largeEmpty)
        {
            ItemDefinition item = EnsureItem(variant.ItemPath, variant.ItemName);
            ResourceDefinition definition = EnsureDefinition(variant, item);
            Material material = EnsureVariantMaterial(variant);
            Material emptyMaterial = variant.HasOwnEmpty
                ? EnsureEmptyVariantMaterial(variant)
                : variant.Large ? largeEmpty : empty;

            NetworkObject built = ResourceNodePrefabFactory.Create(definition);
            built.name = Path.GetFileNameWithoutExtension(variant.PrefabPath);
            try
            {
                RemoveComponent<MeshFilter>(built.gameObject);
                RemoveComponent<MeshRenderer>(built.gameObject);

                ApplyVisuals(
                    built.transform, material, emptyMaterial,
                    variant.ModelPath, variant.EmptyModelPath, variant.TargetHeight,
                    out GameObject full, out GameObject emptyVisual,
                    variant.FullMeshName, variant.EmptyMeshName);
                built.GetComponent<ResourceNode>()?.SetVisuals(full, emptyVisual);

                if (variant.Large && built.TryGetComponent(out BoxCollider body))
                {
                    body.isTrigger = false;
                    body.size = new Vector3(1.4f, 2f, 1.1f);
                    body.center = new Vector3(0f, 0.5f, 0f);
                }

                if (variant.Large && built.TryGetComponent(out NavMeshObstacle obstacle))
                {
                    obstacle.carving = true;
                    obstacle.size = new Vector3(1.4f, 2f, 1.1f);
                    obstacle.center = new Vector3(0f, 0.5f, 0f);
                }

                StripChildColliders(built.gameObject);
                PrefabUtility.SaveAsPrefabAsset(built.gameObject, variant.PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        private static ItemDefinition EnsureItem(string path, string itemName)
        {
            GroundAssetBaker.EnsureFolder(ItemFolder);
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }

            var serialized = new SerializedObject(item);
            serialized.FindProperty("_itemName").stringValue = itemName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            item.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(item);
            return item;
        }

        private static ResourceDefinition EnsureDefinition(NodeVariant variant, ItemDefinition item)
        {
            GroundAssetBaker.EnsureFolder(DefinitionFolder);
            var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(variant.DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ResourceDefinition>();
                AssetDatabase.CreateAsset(definition, variant.DefinitionPath);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_resourceType").intValue = (int)ResourceType.Ore;
            serialized.FindProperty("_drop").objectReferenceValue = item;
            serialized.FindProperty("_dropAmount").intValue = variant.DropAmount;
            serialized.FindProperty("_gatheringTier").intValue = 0;
            serialized.FindProperty("_requiredTool").intValue = 0;
            serialized.FindProperty("_resetTime").floatValue = 30f;
            serialized.FindProperty("_gatherIntervalSeconds").floatValue = 1.2f;
            serialized.FindProperty("_interactRange").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            definition.name = Path.GetFileNameWithoutExtension(variant.DefinitionPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Material EnsureVariantMaterial(NodeVariant variant)
        {
            Material material = GroundAssetBaker.EnsureMaterial(
                variant.MaterialPath,
                variant.Name,
                variant.DiffusePath,
                variant.NormalPath,
                variant.MaskPath);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);

            if (!string.IsNullOrEmpty(variant.EmissivePath))
            {
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(variant.EmissivePath));
                material.SetColor("_EmissionColor", new Color(1.5f, 1.4f, 1.3f, 1f));
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureEmptyVariantMaterial(NodeVariant variant)
        {
            Material material = GroundAssetBaker.EnsureMaterial(
                variant.EmptyMaterialPath,
                variant.EmptyTextureName,
                variant.EmptyDiffusePath,
                variant.EmptyNormalPath,
                variant.EmptyMaskPath);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);

            if (!string.IsNullOrEmpty(variant.EmptyEmissivePath))
            {
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(variant.EmptyEmissivePath));
                material.SetColor("_EmissionColor", new Color(1.5f, 1.4f, 1.3f, 1f));
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Packs the supplied PBR map into the URP Lit metallic/gloss layout the rest of the
        /// project uses: R = metallic, G = 255, B = 0, A = smoothness.
        /// </summary>
        private static void EnsureMaskFromPbr(string pbrPath, string maskPath)
        {
            string pbrAbsolute = AbsolutePath(pbrPath);
            if (!File.Exists(pbrAbsolute))
            {
                Debug.LogWarning($"PBR map missing at '{pbrPath}'; the node material will have no mask.");
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, File.ReadAllBytes(pbrAbsolute)))
                    throw new InvalidOperationException($"Could not decode PBR map '{pbrPath}'.");

                Color32[] pixels = source.GetPixels32();
                var packed = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte metallic = pixels[i].b;
                    byte smoothness = (byte)(255 - pixels[i].g);
                    packed[i] = new Color32(metallic, 255, 0, smoothness);
                }

                var mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                try
                {
                    mask.SetPixels32(packed);
                    mask.Apply(false, false);
                    File.WriteAllBytes(AbsolutePath(maskPath), mask.EncodeToPNG());
                }
                finally
                {
                    Object.DestroyImmediate(mask);
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }

            AssetDatabase.ImportAsset(maskPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string AbsolutePath(string assetPath)
            => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", assetPath);

        private static void ConfigureTextures(
            string diffusePath,
            string normalPath,
            string maskPath,
            string emissivePath)
        {
            GroundAssetBaker.ConfigureTexture(diffusePath, normalMap: false, srgb: true, alpha: false, maxSize: 2048);
            GroundAssetBaker.ConfigureTexture(normalPath, normalMap: true, srgb: false, alpha: false, maxSize: 2048);
            GroundAssetBaker.ConfigureTexture(maskPath, normalMap: false, srgb: false, alpha: true, maxSize: 2048);

            if (!string.IsNullOrEmpty(emissivePath))
                GroundAssetBaker.ConfigureTexture(emissivePath, normalMap: false, srgb: true, alpha: false, maxSize: 2048);
        }

        private static Material EnsureOreMaterial()
        {
            Material ore = GroundAssetBaker.EnsureMaterial(
                OreMaterialPath,
                "IronOre",
                OreDiffusePath,
                OreNormalPath,
                OreMaskPath);
            ore.SetFloat("_Smoothness", 1f);
            ore.SetFloat("_SmoothnessTextureChannel", 0f);
            ore.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(OreEmissivePath));
            ore.SetColor("_EmissionColor", new Color(1.5f, 1.4f, 1.3f, 1f));
            ore.EnableKeyword("_EMISSION");
            ore.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            ore.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(ore);
            return ore;
        }

        private static Material EnsureEmptyMaterial()
        {
            Material oreEmpty = GroundAssetBaker.EnsureMaterial(
                StoneEmptyMaterialPath,
                "StoneOreEmpty",
                StoneEmptyDiffusePath,
                StoneEmptyNormalPath,
                StoneEmptyMaskPath);
            oreEmpty.SetFloat("_Smoothness", 1f);
            oreEmpty.SetFloat("_SmoothnessTextureChannel", 0f);
            oreEmpty.DisableKeyword("_EMISSION");
            oreEmpty.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            oreEmpty.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(oreEmpty);
            return oreEmpty;
        }

        private static Material EnsureLargeMaterial()
        {
            Material oreLarge = GroundAssetBaker.EnsureMaterial(
                OreLargeMaterialPath,
                "IronOreLarge",
                OreLargeDiffusePath,
                OreLargeNormalPath,
                OreLargeMaskPath);
            oreLarge.SetFloat("_Smoothness", 1f);
            oreLarge.SetFloat("_SmoothnessTextureChannel", 0f);
            oreLarge.DisableKeyword("_EMISSION");
            oreLarge.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            oreLarge.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(oreLarge);
            return oreLarge;
        }

        private static Material EnsureLargeEmptyMaterial()
        {
            Material oreLargeEmpty = GroundAssetBaker.EnsureMaterial(
                StoneLargeEmptyMaterialPath,
                "StoneOreLargeEmpty",
                StoneLargeEmptyDiffusePath,
                StoneLargeEmptyNormalPath,
                StoneLargeEmptyMaskPath);
            oreLargeEmpty.SetFloat("_Smoothness", 1f);
            oreLargeEmpty.SetFloat("_SmoothnessTextureChannel", 0f);
            oreLargeEmpty.DisableKeyword("_EMISSION");
            oreLargeEmpty.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            oreLargeEmpty.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(oreLargeEmpty);
            return oreLargeEmpty;
        }

        private static ResourceDefinition EnsureLargeDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(LargeDefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ResourceDefinition>();
                AssetDatabase.CreateAsset(definition, LargeDefinitionPath);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_resourceType").intValue = (int)ResourceType.Ore;
            serialized.FindProperty("_drop").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ItemDefinition>("Assets/Resources/Items/Iron_Ore.asset");
            serialized.FindProperty("_dropAmount").intValue = LargeDropAmount;
            serialized.FindProperty("_gatheringTier").intValue = 0;
            serialized.FindProperty("_requiredTool").intValue = 0;
            serialized.FindProperty("_resetTime").floatValue = 30f;
            serialized.FindProperty("_gatherIntervalSeconds").floatValue = 1.2f;
            serialized.FindProperty("_interactRange").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static NetworkObject RebuildPrefab(Material ore, Material oreEmpty)
        {
            string path = ResourceNodeBaker.PrefabAssetPath;
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                NetworkObject built = ResourceNodePrefabFactory.Create();
                try
                {
                    RemoveComponent<MeshFilter>(built.gameObject);
                    RemoveComponent<MeshRenderer>(built.gameObject);
                    ApplyVisuals(
                        built.transform, ore, oreEmpty, OreModelPath, StoneEmptyModelPath, OreTargetHeight,
                        out GameObject full, out GameObject empty);
                    built.GetComponent<ResourceNode>()?.SetVisuals(full, empty);
                    StripChildColliders(built.gameObject);
                    GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, path);
                    return asset != null ? asset.GetComponent<NetworkObject>() : null;
                }
                finally
                {
                    Object.DestroyImmediate(built.gameObject);
                }
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // The first generation was a single cube; drop its renderer and any stale model.
                RemoveComponent<MeshFilter>(contents);
                RemoveComponent<MeshRenderer>(contents);

                Transform stale = contents.transform.Find("Visuals");
                if (stale != null)
                    Object.DestroyImmediate(stale.gameObject);

                if (contents.TryGetComponent(out BoxCollider body))
                {
                    body.isTrigger = false;
                    body.center = Vector3.zero;
                    body.size = Vector3.one;
                }

                if (contents.TryGetComponent(out NavMeshObstacle obstacle))
                {
                    obstacle.carving = true;
                    obstacle.center = Vector3.zero;
                    obstacle.size = Vector3.one;
                }

                ApplyVisuals(
                    contents.transform, ore, oreEmpty, OreModelPath, StoneEmptyModelPath, OreTargetHeight,
                    out GameObject full, out GameObject empty);
                contents.GetComponent<ResourceNode>()?.SetVisuals(full, empty);
                StripChildColliders(contents);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
                return saved != null ? saved.GetComponent<NetworkObject>() : null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void RebuildLargePrefab(
            Material oreLarge,
            Material oreLargeEmpty,
            ResourceDefinition definition)
        {
            NetworkObject built = ResourceNodePrefabFactory.Create(definition);
            built.name = "ResourceNode_Large";
            try
            {
                RemoveComponent<MeshFilter>(built.gameObject);
                RemoveComponent<MeshRenderer>(built.gameObject);
                ApplyVisuals(
                    built.transform, oreLarge, oreLargeEmpty, OreLargeModelPath, StoneLargeEmptyModelPath,
                    LargeTargetHeight, out GameObject full, out GameObject empty);
                built.GetComponent<ResourceNode>()?.SetVisuals(full, empty);

                if (built.TryGetComponent(out BoxCollider body))
                {
                    body.isTrigger = false;
                    body.size = new Vector3(1.4f, 2f, 1.1f);
                    body.center = new Vector3(0f, 0.5f, 0f);
                }

                if (built.TryGetComponent(out NavMeshObstacle obstacle))
                {
                    obstacle.carving = true;
                    obstacle.size = new Vector3(1.4f, 2f, 1.1f);
                    obstacle.center = new Vector3(0f, 0.5f, 0f);
                }

                StripChildColliders(built.gameObject);
                PrefabUtility.SaveAsPrefabAsset(built.gameObject, LargePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        private static void ConfigureModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }

            if (importer == null)
            {
                Debug.LogWarning($"Ore model missing or not importable at '{path}'.");
                return;
            }

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.addCollider = false;
            importer.SaveAndReimport();
        }

        private static void ApplyVisuals(
            Transform root,
            Material ore,
            Material oreEmpty,
            string fullModelPath,
            string emptyModelPath,
            float targetHeight,
            out GameObject full,
            out GameObject empty,
            string fullMeshName = "IronOreMesh",
            string emptyMeshName = "StoneOreEmptyMesh")
        {
            // The node root is scaled to 2 x 1.2 x 2. This container cancels that scale so the
            // imported models keep their proportions while still fitting the footprint.
            var container = new GameObject("Visuals");
            container.transform.SetParent(root, false);
            Vector3 rootScale = root.lossyScale;
            container.transform.localScale = new Vector3(
                1f / Mathf.Max(rootScale.x, 0.0001f),
                1f / Mathf.Max(rootScale.y, 0.0001f),
                1f / Mathf.Max(rootScale.z, 0.0001f));

            full = BuildModel(root, container.transform, fullModelPath, fullMeshName, ore, targetHeight);
            empty = BuildModel(root, container.transform, emptyModelPath, emptyMeshName, oreEmpty, targetHeight);

            full.SetActive(true);
            empty.SetActive(false);
        }

        private static GameObject BuildModel(
            Transform root,
            Transform parent,
            string modelPath,
            string modelName,
            Material material,
            float targetHeight)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
            {
                Debug.LogWarning($"Ore model missing at '{modelPath}'.");
                var placeholder = new GameObject(modelName);
                placeholder.transform.SetParent(parent, false);
                return placeholder;
            }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            PrefabUtility.UnpackPrefabInstance(
                model,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            model.name = modelName;
            model.transform.localPosition = Vector3.zero;
            // Compose the yaw with the FBX axis-conversion rotation instead of replacing it.
            model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f) * model.transform.localRotation;

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            Bounds bounds = RendererBounds(model);
            if (bounds.size.y > 0.0001f)
                model.transform.localScale *= targetHeight / bounds.size.y;

            bounds = RendererBounds(model);
            float floorY = root.position.y - 0.5f * root.lossyScale.y;
            model.transform.position += Vector3.up * (floorY - bounds.min.y);

            bounds = RendererBounds(model);
            model.transform.position -= new Vector3(
                bounds.center.x - root.position.x,
                0f,
                bounds.center.z - root.position.z);

            return model;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void StripChildColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.gameObject != root)
                    Object.DestroyImmediate(collider);
            }
        }

        private static void RemoveComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
                Object.DestroyImmediate(component);
        }
    }
}
