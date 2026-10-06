using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Client;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Builds the network player object — a capsule carrying a FishNet
    /// <see cref="NetworkObject"/> — and wires it into a <see cref="NetworkManager"/> so the
    /// server spawns one for every client that connects.
    /// </summary>
    public static class PlayerPrefabFactory
    {
        public const string PrefabName = "Player";

        /// <summary>"Player" is a built-in Unity tag, so this always resolves.</summary>
        public const string PlayerTag = "Player";

        /// <summary>World height of the capsule, in metres.</summary>
        public const float DefaultHeight = 2f;

        /// <summary>World radius of the capsule, in metres.</summary>
        public const float DefaultRadius = 0.5f;

        /// <summary>
        /// Transform scale that turns Unity's capsule primitive (0.5 radius, 2 tall) into
        /// <see cref="DefaultRadius"/> / <see cref="DefaultHeight"/>. The primitive's
        /// CapsuleCollider is authored in local units, so scaling scales collider and mesh together
        /// and they cannot drift apart.
        /// </summary>
        public static Vector3 DefaultScale => new Vector3(
            DefaultRadius * 2f,
            DefaultHeight / 2f,
            DefaultRadius * 2f);

        /// <summary>
        /// Builds the player object: a capsule at the origin with a <see cref="NetworkObject"/>,
        /// the project's own networked <see cref="PlayerStats"/>, <see cref="PlayerCombat"/>, and the
        /// <see cref="LocalPlayerCamera"/> that gives the owning client something to look through.
        /// </summary>
        /// <remarks>
        /// The result is an ordinary active GameObject, so it can be saved as a prefab asset
        /// (<c>PrefabUtility.SaveAsPrefabAsset</c>) or registered as a spawnable. Callers that keep
        /// it in a live scene should deactivate it — FishNet's pool activates the clone it spawns,
        /// so an inactive source is still spawnable.
        /// </remarks>
        public static NetworkObject Create()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = PrefabName;
            go.tag = PlayerTag;
            go.transform.localScale = DefaultScale;

            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerStats>();
            go.AddComponent<PlayerCombat>();

            go.AddComponent<LocalPlayerCamera>();

            PrimitiveAppearance.ApplyRenderPipelineMaterial(go);
            return networkObject;
        }

        /// <summary>
        /// True when <paramref name="playerPrefab"/> is already one of the spawnables.
        /// </summary>
        public static bool Contains(PrefabObjects prefabs, NetworkObject playerPrefab)
        {
            if (prefabs is not SinglePrefabObjects single || playerPrefab == null)
                return false;

            return single.Prefabs.Contains(playerPrefab);
        }

        /// <summary>
        /// Adds <paramref name="playerPrefab"/> to the spawnable collection so the server and the
        /// client agree on a prefab id for it. FishNet derives prefab ids from list order, so
        /// both ends must register the prefabs in the same order.
        /// </summary>
        /// <returns>True when this call added the prefab, false when it was a no-op.</returns>
        public static bool Register(PrefabObjects prefabs, NetworkObject playerPrefab)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (playerPrefab == null || Contains(prefabs, playerPrefab))
                return false;

            prefabs.AddObject(playerPrefab, checkForDuplicates: true, initializeAdded: false);
            return true;
        }

        /// <summary>
        /// Puts FishNet's <see cref="PlayerSpawner"/> on the NetworkManager's own GameObject and
        /// points it at <paramref name="playerPrefab"/>, which is how FishNet 4.x is told to spawn
        /// a prefab whenever a player connects.
        /// </summary>
        public static PlayerSpawner Attach(NetworkManager manager, NetworkObject playerPrefab)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            PlayerSpawner spawner = manager.GetComponent<PlayerSpawner>();
            if (spawner == null)
                spawner = manager.gameObject.AddComponent<PlayerSpawner>();

            if (playerPrefab != null)
                spawner.SetPlayerPrefab(playerPrefab);

            return spawner;
        }
    }
}
