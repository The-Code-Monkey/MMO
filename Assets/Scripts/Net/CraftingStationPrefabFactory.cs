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

        public static Vector3 DefaultSpawnPosition => new(2f, DefaultHeight / 2f, 5f);

        public static NetworkObject Create()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<CraftingStation>();

            ApplyBrownMaterial(go);

            // Ensure the BoxCollider is not a trigger so OnMouseDown() fires and players
            // cannot walk through the crafting station.
            if (go.TryGetComponent(out BoxCollider boxCollider))
            {
                boxCollider.isTrigger = false;
            }

            // Add NavMeshObstacle so the NavMesh is carved around the crafting station,
            // preventing players from walking through its location when clicking elsewhere.
            go.AddComponent<NavMeshObstacle>();

            return networkObject;
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

        private static void ApplyBrownMaterial(GameObject go)
        {
            Shader shader = ResolveShader();
            if (shader == null)
                return;

            Material material = new(shader);
            material.name = "CraftingStationBrown";
            material.color = BrownColor;
            material.SetColor("_BaseColor", BrownColor);
            material.SetColor("_Color", BrownColor);

            if (go.TryGetComponent(out Renderer renderer))
            {
                renderer.material = material;
            }
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