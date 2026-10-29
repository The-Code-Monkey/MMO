using FishNet.Object;
using MMO.Crafting;
using MMO.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the crafting-station surface maps, builds their URP Lit materials and refreshes the
    /// shared <c>CraftingStation</c> prefab with the furnace and anvil models. The prefab is
    /// rebuilt in place so its GUID and every scene reference survive a rebake.
    /// </summary>
    public static class CraftingStationAssetBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Crafting Station Assets";

        public const string TextureFolder = "Assets/Textures/Crafting";
        public const string MaterialFolder = "Assets/Materials/Crafting";

        public const string AnvilAlbedoPath = TextureFolder + "/Anvil_Albedo.png";
        public const string AnvilNormalPath = TextureFolder + "/Anvil_Normal.png";
        public const string AnvilMaskPath = TextureFolder + "/Anvil_Mask.png";
        public const string AnvilMaterialPath = MaterialFolder + "/Anvil_Metal.mat";

        public const string FurnaceAlbedoPath = TextureFolder + "/Furnace_Albedo.png";
        public const string FurnaceNormalPath = TextureFolder + "/Furnace_Normal.png";
        public const string FurnaceMaskPath = TextureFolder + "/Furnace_Mask.png";
        public const string FurnaceMaterialPath = MaterialFolder + "/Furnace_Stone.mat";

        public const string WoodMaterialPath = MaterialFolder + "/Wood_Stump.mat";
        public const string FireMaterialPath = MaterialFolder + "/Furnace_Fire.mat";

        public const string ForgeModelPath = "Assets/Models/Crafting/Forge/Forge.fbx";
        public const string ForgeDiffusePath = TextureFolder + "/Forge/Forge_Diffuse.png";
        public const string ForgeNormalPath = TextureFolder + "/Forge/Forge_Normal.png";
        public const string ForgeMaskPath = TextureFolder + "/Forge/Forge_Mask.png";
        public const string ForgeEmissivePath = TextureFolder + "/Forge/Forge_Emissive.png";
        public const string ForgeMaterialPath = MaterialFolder + "/Forge.mat";

        private const float ForgeYaw = 0f;

        private const string LitShaderName = "Universal Render Pipeline/Lit";

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before rebuilding crafting station assets.");
                return;
            }

            GroundAssetBaker.EnsureFolder(TextureFolder);
            GroundAssetBaker.EnsureFolder(MaterialFolder);
            GroundAssetBaker.EnsureFolder(CraftingStationBaker.PrefabFolder);
            GroundAssetBaker.EnsureFolder(TextureFolder + "/Forge");
            GroundAssetBaker.EnsureFolder("Assets/Models/Crafting/Forge");

            GroundAssetBaker.ConfigureTexture(AnvilAlbedoPath, normalMap: false, srgb: true, alpha: false);
            GroundAssetBaker.ConfigureTexture(AnvilNormalPath, normalMap: true, srgb: false, alpha: false);
            GroundAssetBaker.ConfigureTexture(AnvilMaskPath, normalMap: false, srgb: false, alpha: true);

            GroundAssetBaker.ConfigureTexture(FurnaceAlbedoPath, normalMap: false, srgb: true, alpha: false);
            GroundAssetBaker.ConfigureTexture(FurnaceNormalPath, normalMap: true, srgb: false, alpha: false);
            GroundAssetBaker.ConfigureTexture(FurnaceMaskPath, normalMap: false, srgb: false, alpha: true);

            GroundAssetBaker.ConfigureTexture(ForgeDiffusePath, normalMap: false, srgb: true, alpha: false, maxSize: 2048);
            GroundAssetBaker.ConfigureTexture(ForgeNormalPath, normalMap: true, srgb: false, alpha: false, maxSize: 2048);
            GroundAssetBaker.ConfigureTexture(ForgeMaskPath, normalMap: false, srgb: false, alpha: true, maxSize: 2048);
            GroundAssetBaker.ConfigureTexture(ForgeEmissivePath, normalMap: false, srgb: true, alpha: false, maxSize: 2048);
            ConfigureForgeModel();

            Material anvil = GroundAssetBaker.EnsureMaterial(
                AnvilMaterialPath,
                "Anvil_Metal",
                AnvilAlbedoPath,
                AnvilNormalPath,
                AnvilMaskPath);
            anvil.SetFloat("_Smoothness", 0.60f);
            anvil.SetFloat("_SmoothnessTextureChannel", 0f);
            anvil.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(anvil);

            Material furnace = GroundAssetBaker.EnsureMaterial(
                FurnaceMaterialPath,
                "Furnace_Stone",
                FurnaceAlbedoPath,
                FurnaceNormalPath,
                FurnaceMaskPath);
            furnace.SetFloat("_Smoothness", 0.50f);
            furnace.SetFloat("_SmoothnessTextureChannel", 0f);
            furnace.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(furnace);

            Material wood = EnsureSolidMaterial(
                WoodMaterialPath,
                "Wood_Stump",
                new Color(0.30f, 0.20f, 0.11f),
                smoothness: 0.12f);
            Material fire = EnsureEmissiveMaterial(
                FireMaterialPath,
                "Furnace_Fire",
                new Color(0.55f, 0.20f, 0.05f),
                new Color(1f, 0.38f, 0.09f) * 1.0f);

            Material forge = GroundAssetBaker.EnsureMaterial(
                ForgeMaterialPath,
                "Forge",
                ForgeDiffusePath,
                ForgeNormalPath,
                ForgeMaskPath);
            forge.SetFloat("_Smoothness", 1f);
            forge.SetFloat("_SmoothnessTextureChannel", 0f);
            forge.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ForgeEmissivePath));
            forge.SetColor("_EmissionColor", new Color(2f, 1.9f, 1.7f, 1f));
            forge.EnableKeyword("_EMISSION");
            forge.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            forge.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(forge);

            AssetDatabase.SaveAssets();

            NetworkObject prefab = RebuildPrefab(anvil, wood, forge);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(prefab != null
                ? $"Rebuilt crafting station assets: {CraftingStationBaker.PrefabAssetPath}"
                : "Crafting station asset rebuild failed.");
        }

        private static NetworkObject RebuildPrefab(
            Material anvilMetal,
            Material anvilWood,
            Material forgeMaterial)
        {
            string path = CraftingStationBaker.PrefabAssetPath;
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing == null)
            {
                NetworkObject built = CraftingStationPrefabFactory.Create(anvilMetal, anvilWood);
                try
                {
                    ApplyForgeModel(built.transform.Find("Visuals/Furnace"), forgeMaterial);
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

                BoxCollider body = contents.GetComponent<BoxCollider>();
                if (body != null)
                {
                    body.isTrigger = false;
                    body.center = CraftingStationPrefabFactory.BodyLocalCenter;
                    body.size = CraftingStationPrefabFactory.BodyLocalSize;
                }

                NavMeshObstacle obstacle = contents.GetComponent<NavMeshObstacle>();
                if (obstacle != null)
                {
                    obstacle.carving = true;
                    obstacle.center = CraftingStationPrefabFactory.ObstacleLocalCenter;
                    obstacle.size = CraftingStationPrefabFactory.ObstacleLocalSize;
                }

                Transform stale = contents.transform.Find("Visuals");
                if (stale != null)
                    Object.DestroyImmediate(stale.gameObject);

                CraftingStationPrefabFactory.BuildVisuals(
                    contents.transform,
                    anvilMetal,
                    anvilWood,
                    out GameObject furnace,
                    out GameObject anvil);

                ApplyForgeModel(furnace.transform, forgeMaterial);

                // Primitives arrive with their own colliders. Only the root body collider may
                // survive, otherwise the station carries a dozen extra blocking volumes.
                StripChildColliders(contents);

                CraftingStation station = contents.GetComponent<CraftingStation>();
                if (station != null)
                    station.SetVisuals(furnace, anvil);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
                return saved != null ? saved.GetComponent<NetworkObject>() : null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void ConfigureForgeModel()
        {
            var importer = AssetImporter.GetAtPath(ForgeModelPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(ForgeModelPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(ForgeModelPath) as ModelImporter;
            }

            if (importer == null)
            {
                Debug.LogWarning($"Forge model missing or not importable at '{ForgeModelPath}'.");
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

        private static void ApplyForgeModel(Transform anchor, Material material)
        {
            if (anchor == null)
                return;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ForgeModelPath);
            if (source == null)
            {
                Debug.LogWarning($"Forge model missing at '{ForgeModelPath}'; the Furnace anchor stays empty.");
                return;
            }

            // The station root is scaled to 2x1x2. This container cancels that scale so the
            // imported model keeps its own proportions while still fitting the footprint.
            var container = new GameObject("ForgeModel");
            container.transform.SetParent(anchor, false);
            Vector3 anchorScale = anchor.lossyScale;
            container.transform.localScale = new Vector3(
                1f / Mathf.Max(anchorScale.x, 0.0001f),
                1f / Mathf.Max(anchorScale.y, 0.0001f),
                1f / Mathf.Max(anchorScale.z, 0.0001f));

            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, container.transform);
            PrefabUtility.UnpackPrefabInstance(
                model,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            model.name = "ForgeMesh";
            model.transform.localPosition = Vector3.zero;
            // The FBX import carries an axis-conversion rotation; compose the yaw with it so the
            // forge stays upright instead of being tipped over.
            model.transform.localRotation = Quaternion.Euler(0f, ForgeYaw, 0f) * model.transform.localRotation;

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            const float targetHeight = 6.40f;
            Bounds bounds = RendererBounds(model);
            if (bounds.size.y > 0.0001f)
                model.transform.localScale *= targetHeight / bounds.size.y;

            bounds = RendererBounds(model);
            float floorY = anchor.position.y - 0.5f * anchor.lossyScale.y;
            model.transform.position += Vector3.up * (floorY - bounds.min.y);

            bounds = RendererBounds(model);
            model.transform.position -= new Vector3(
                bounds.center.x - anchor.position.x,
                0f,
                bounds.center.z - anchor.position.z);
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

        private static void RemoveComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
                Object.DestroyImmediate(component);
        }

        private static void StripChildColliders(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.gameObject != root)
                    Object.DestroyImmediate(collider);
            }
        }

        private static Material EnsureSolidMaterial(
            string path,
            string materialName,
            Color color,
            float smoothness)
        {
            Material material = EnsureBaseMaterial(path, materialName);
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_OCCLUSIONMAP");
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_EMISSION");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureEmissiveMaterial(
            string path,
            string materialName,
            Color baseColor,
            Color emissionColor)
        {
            Material material = EnsureBaseMaterial(path, materialName);
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_Color", baseColor);
            material.SetColor("_EmissionColor", emissionColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_OCCLUSIONMAP");
            material.DisableKeyword("_NORMALMAP");
            material.EnableKeyword("_EMISSION");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureBaseMaterial(string path, string materialName)
        {
            Shader shader = Shader.Find(LitShaderName);
            if (shader == null)
                throw new System.InvalidOperationException(
                    $"Shader '{LitShaderName}' not found. Run Tools > MMO > Configure URP.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.name = materialName;
            material.renderQueue = -1;
            material.SetOverrideTag("RenderType", "Opaque");
            return material;
        }
    }
}
