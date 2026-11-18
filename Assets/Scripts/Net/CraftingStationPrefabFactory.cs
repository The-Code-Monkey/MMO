using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Crafting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace MMO.Net
{
    public static class CraftingStationPrefabFactory
    {
        public const string PrefabName = "CraftingStation";

        public const float DefaultWidth = 2f;
        public const float DefaultHeight = 1f;
        public const float DefaultDepth = 2f;

        public static Color BrownColor => new(0.4f, 0.25f, 0.1f, 1f);

        public static Vector3 DefaultScale => new(DefaultWidth, DefaultHeight, DefaultDepth);

        /// <summary>
        /// The default prefab body (the forge) in the station's <em>local</em> space. The root is
        /// scaled to <see cref="DefaultScale"/>, so this is 8 x 6.4 x 8 m in the world.
        /// </summary>
        public static Vector3 BodyLocalSize => CraftingStation.FurnaceBodyLocalSize;

        public static Vector3 BodyLocalCenter => CraftingStation.FurnaceBodyLocalCenter;

        /// <summary>
        /// The carving obstacle's size in the station's <em>local</em> space.
        /// <see cref="NavMeshObstacle.size"/> is scaled by the transform, so it matches the body.
        /// </summary>
        public static Vector3 ObstacleLocalSize => BodyLocalSize;

        public static Vector3 ObstacleLocalCenter => BodyLocalCenter;

        public static Vector3 DefaultSpawnPosition => new(2f, DefaultHeight / 2f, 5f);

        /// <summary>
        /// Builds the station with its fallback materials. This is the runtime/test entry point;
        /// the editor baker calls the material overload with the authored assets instead.
        /// </summary>
        public static NetworkObject Create()
        {
            return Create(null, null);
        }

        /// <summary>
        /// Builds the station body and both visual anchors. A null material falls back to a
        /// flat colour so the factory still works without an editor bake.
        /// </summary>
        public static NetworkObject Create(Material anvilMetal, Material anvilWood)
        {
            GameObject go = new(PrefabName);
            go.transform.localScale = DefaultScale;

            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            CraftingStation station = go.AddComponent<CraftingStation>();

            // The body collider is not a trigger so the world click raycast finds it and players cannot walk
            // through the station. The root has no mesh renderer; the visual parts live under
            // the Visuals child and are toggled by the replicated station type.
            BoxCollider boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.isTrigger = false;
            boxCollider.size = BodyLocalSize;
            boxCollider.center = BodyLocalCenter;

            // Add NavMeshObstacle so the NavMesh is carved around the crafting station,
            // preventing players from walking through its location when clicking elsewhere.
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.size = ObstacleLocalSize;
            obstacle.center = ObstacleLocalCenter;

            BuildVisuals(
                go.transform,
                anvilMetal ?? FallbackMaterial("AnvilMetal", new Color(0.16f, 0.17f, 0.19f)),
                anvilWood ?? FallbackMaterial("AnvilWood", BrownColor),
                out GameObject furnace,
                out GameObject anvil);

            station.SetVisuals(furnace, anvil);

            return networkObject;
        }

        /// <summary>
        /// Builds the visual anchors under <paramref name="root"/>. The anvil is procedural; the
        /// furnace is an empty anchor the imported forge model parents into. The editor baker
        /// reuses this when refreshing the saved prefab so the hierarchy has one source of truth.
        /// </summary>
        public static void BuildVisuals(
            Transform root,
            Material anvilMetal,
            Material anvilWood,
            out GameObject furnace,
            out GameObject anvil)
        {
            GameObject visuals = new("Visuals");
            visuals.transform.SetParent(root, false);

            furnace = new GameObject("Furnace");
            furnace.transform.SetParent(visuals.transform, false);

            anvil = BuildAnvil(visuals.transform, anvilMetal, anvilWood);

            furnace.SetActive(true);
            anvil.SetActive(false);
        }

        public static bool Contains(PrefabObjects prefabs, NetworkObject craftingStation)
        {
            if (prefabs is not SinglePrefabObjects single || craftingStation == null)
                return false;

            return single.Prefabs.Contains(craftingStation);
        }

        public static bool Register(PrefabObjects prefabs, NetworkObject craftingStation, bool initializeAdded = false)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (craftingStation == null || Contains(prefabs, craftingStation))
                return false;

            prefabs.AddObject(craftingStation, checkForDuplicates: true, initializeAdded: initializeAdded);
            return true;
        }

        public static CraftingStationSpawner Attach(NetworkManager manager, NetworkObject craftingStation)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            CraftingStationSpawner spawner = manager.GetComponent<CraftingStationSpawner>();
            if (spawner == null)
                spawner = manager.gameObject.AddComponent<CraftingStationSpawner>();

            if (craftingStation != null)
                spawner.SetDummyPrefab(craftingStation);

            return spawner;
        }

        /// <summary>
        /// Wood-stump anvil: stump and iron band, base, waist, face, heel and a rounded horn.
        /// Coordinates are in the root's local space, whose world scale is 2x1x2.
        /// </summary>
        private static GameObject BuildAnvil(Transform parent, Material metal, Material wood)
        {
            GameObject root = new("Anvil");
            root.transform.SetParent(parent, false);

            Primitive(root.transform, "Stump", PrimitiveType.Cylinder,
                new Vector3(0f, -0.38f, 0f), new Vector3(0.30f, 0.12f, 0.30f), wood);
            Primitive(root.transform, "StumpBand", PrimitiveType.Cylinder,
                new Vector3(0f, -0.265f, 0f), new Vector3(0.31f, 0.02f, 0.31f), metal);
            Primitive(root.transform, "Base", PrimitiveType.Cube,
                new Vector3(0f, -0.20f, 0f), new Vector3(0.30f, 0.12f, 0.26f), metal);
            Primitive(root.transform, "Waist", PrimitiveType.Cube,
                new Vector3(0f, -0.06f, 0f), new Vector3(0.20f, 0.16f, 0.18f), metal);
            Primitive(root.transform, "UnderFace", PrimitiveType.Cube,
                new Vector3(0f, 0.05f, 0f), new Vector3(0.34f, 0.10f, 0.22f), metal);
            Primitive(root.transform, "Face", PrimitiveType.Cube,
                new Vector3(0f, 0.135f, 0f), new Vector3(0.46f, 0.11f, 0.22f), metal);
            Primitive(root.transform, "Heel", PrimitiveType.Cube,
                new Vector3(-0.27f, 0.135f, 0f), new Vector3(0.10f, 0.08f, 0.22f), metal);
            Primitive(root.transform, "Horn", PrimitiveType.Sphere,
                new Vector3(0.28f, 0.135f, 0f), new Vector3(0.18f, 0.12f, 0.14f), metal);

            return root;
        }

        private static GameObject Primitive(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
                DestroyObject(collider);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return go;
        }

        private static Material FallbackMaterial(string name, Color color)
        {
            Shader shader = ResolveShader();
            if (shader == null)
                return null;

            Material material = new(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            material.color = color;
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            return material;
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                return shader;

            shader = Shader.Find("Standard");
            if (shader != null)
                return shader;

            return Shader.Find("Unlit/Color");
        }
    }
}
