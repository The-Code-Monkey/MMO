using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the iron-ore surface maps, builds the full and depleted URP Lit materials and
    /// refreshes the shared <c>ResourceNode</c> prefabs (normal and large). The prefabs are
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

        private const float OreTargetHeight = 1.6f;
        private const float ModelYaw = 180f;
        private const float LargeTargetHeight = 2.4f;
        private const int LargeDropAmount = 15;

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before rebuilding resource node assets.");
                return;
            }

            GroundAssetBaker.EnsureFolder(TextureFolder + "/IronOre");
            GroundAssetBaker.EnsureFolder(TextureFolder + "/StoneOreEmpty");
            GroundAssetBaker.EnsureFolder(TextureFolder + "/IronOreLarge");
            GroundAssetBaker.EnsureFolder(TextureFolder + "/StoneOreLargeEmpty");
            GroundAssetBaker.EnsureFolder(MaterialFolder);
            GroundAssetBaker.EnsureFolder(ModelFolder);
            GroundAssetBaker.EnsureFolder(StoneModelFolder);

            ConfigureTextures(
                OreDiffusePath, OreNormalPath, OreMaskPath, OreEmissivePath);
            ConfigureTextures(
                StoneEmptyDiffusePath, StoneEmptyNormalPath, StoneEmptyMaskPath, null);
            ConfigureTextures(
                OreLargeDiffusePath, OreLargeNormalPath, OreLargeMaskPath, null);
            ConfigureTextures(
                StoneLargeEmptyDiffusePath, StoneLargeEmptyNormalPath, StoneLargeEmptyMaskPath, null);

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

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(prefab != null
                ? $"Rebuilt resource node assets: {ResourceNodeBaker.PrefabAssetPath}, {LargePrefabPath}"
                : "Resource node asset rebuild failed.");
        }

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
            out GameObject empty)
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

            full = BuildModel(root, container.transform, fullModelPath, "IronOreMesh", ore, targetHeight);
            empty = BuildModel(root, container.transform, emptyModelPath, "StoneOreEmptyMesh", oreEmpty, targetHeight);

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
