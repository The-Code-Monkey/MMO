using System;
using System.Linq;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Builds the loot bag — a small marker object carrying a FishNet <see cref="NetworkObject"/>
    /// and the project's own networked <see cref="LootBag"/> — and registers it as a spawnable so
    /// a full-loot death can instantiate and server-spawn it.
    ///
    /// Unlike the target dummy, the loot bag has no startup spawner: it only ever enters the world
    /// through <see cref="Gameplay.PlayerDeath"/> reacting to a death. What it does need is a
    /// stable prefab id both ends agree on, which is why it is registered with the other
    /// server-spawned entities in <see cref="NetworkManagerFactory"/>.
    ///
    /// Everything is built in code so a scene, a fresh process and a test all produce the same
    /// object. The editor baker (<c>MMO.Editor.LootBagBaker</c>) saves what <see cref="Create"/>
    /// returns to disk as a prefab asset.
    /// </summary>
    public static class LootBagPrefabFactory
    {
        public const string PrefabName = "LootBag";

        /// <summary>World size of the bag marker, in metres.</summary>
        public const float DefaultSize = 0.6f;

        /// <summary>Colour that reads as "loot" against terrain. Gold is the genre convention.</summary>
        public static readonly Color GoldColor = new(0.95f, 0.78f, 0.18f, 1f);

        /// <summary>How close a looter must stand by default, in metres.</summary>
        public const float DefaultInteractRange = 3f;

        public static Vector3 DefaultScale => Vector3.one * DefaultSize;

        /// <summary>
        /// Builds the loot bag: a small sphere with a <see cref="NetworkObject"/> and the
        /// networked <see cref="LootBag"/>. The caller deactivates a runtime-built source; the
        /// server activates the clone it spawns.
        /// </summary>
        public static NetworkObject Create()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            // NetworkObject before any NetworkBehaviour: behaviours resolve their owning
            // NetworkObject through it.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            LootBag bag = go.AddComponent<LootBag>();
            bag.SetInteractRange(DefaultInteractRange);

            ApplyGoldMaterial(go);
            return networkObject;
        }

        /// <summary>True when <paramref name="lootBag"/> is already one of the spawnables.</summary>
        public static bool Contains(PrefabObjects prefabs, NetworkObject lootBag)
        {
            if (prefabs is not SinglePrefabObjects single || lootBag == null)
                return false;

            return single.Prefabs.Contains(lootBag);
        }

        /// <summary>
        /// Adds <paramref name="lootBag"/> to the spawnable collection so the server and the client
        /// agree on a prefab id for it. FishNet derives prefab ids from list order, so this must be
        /// registered on every end in the same position — after the player, dummy and crafting
        /// station, which is the order <see cref="NetworkManagerFactory"/> uses.
        /// </summary>
        /// <returns>True when this call added the prefab, false when it was a no-op.</returns>
        public static bool Register(PrefabObjects prefabs, NetworkObject lootBag)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (lootBag == null || Contains(prefabs, lootBag))
                return false;

            prefabs.AddObject(lootBag, checkForDuplicates: true, initializeAdded: false);
            return true;
        }

        /// <summary>
        /// Swaps the primitive's built-in material for a gold one using the active render
        /// pipeline's shader, so a URP project does not render the bag magenta.
        /// </summary>
        private static void ApplyGoldMaterial(GameObject go)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return;

            Material material = new(shader);
            material.name = "LootBagGold";
            material.color = GoldColor;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", GoldColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", GoldColor);

            if (go.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = material;
        }
    }
}
