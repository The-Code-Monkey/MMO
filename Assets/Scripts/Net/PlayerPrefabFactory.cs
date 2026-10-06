using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Client;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace MMO.Net
{
    /// <summary>
    /// Builds the network player object — a capsule carrying a FishNet
    /// <see cref="NetworkObject"/> — and wires it into a <see cref="NetworkManager"/> so the
    /// server spawns one for every client that connects.
    ///
    /// FishNet 4.x removed <c>NetworkManager.PlayerPrefab</c>; spawning on connect is the job of
    /// <see cref="PlayerSpawner"/>, which spawns when a client finishes loading its start scenes.
    /// Joining here is gated by <see cref="MMOConnectionGate"/>, so a player object is only ever
    /// spawned for a client the server has already admitted.
    ///
    /// Everything is built in code so a scene, a fresh process and a test all produce the same
    /// object. The editor baker (<c>MMO.Editor.PlayerPrefabBaker</c>) saves what
    /// <see cref="Create"/> returns to disk as a prefab asset.
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
        /// the project's own networked <see cref="PlayerStats"/>, the
        /// <see cref="PlayerInventory"/> that owns this player's bag and equipment containers, and
        /// the <see cref="LocalPlayerCamera"/> that gives the owning client something to look through.
        /// </summary>
        /// <remarks>
        /// The result is an ordinary active GameObject, so it can be saved as a prefab asset
        /// (<c>PrefabUtility.SaveAsPrefabAsset</c>) or registered as a spawnable. Callers that keep
        /// it in a live scene should deactivate it — FishNet's pool activates the clone it spawns,
        /// so an inactive source is still spawnable.
        /// </remarks>
        public static NetworkObject Create()
        {
            // CreatePrimitive is what supplies the capsule mesh, renderer and a CapsuleCollider
            // whose dimensions already match the mesh.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = PrefabName;
            go.tag = PlayerTag;
            go.transform.localScale = DefaultScale;

            // NetworkObject before any NetworkBehaviour: behaviours resolve their owning
            // NetworkObject through it, and this is the order the FishNet editor tooling writes.
            // PlayerInventory follows PlayerStats because it pushes recalculated equipment
            // bonuses into PlayerStats whenever either container changes.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerStats>();
            go.AddComponent<PlayerInventory>();

            // Travels with the prefab so a client that receives its player object has something to
            // look through. It decides whether it may drive the camera by asking IsOwner, which is
            // why it is safe for every client's copy of this prefab to carry it.
            go.AddComponent<LocalPlayerCamera>();

            ApplyRenderPipelineMaterial(go);
            return networkObject;
        }

        /// <summary>
        /// True when <paramref name="playerPrefab"/> is already one of the spawnables.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SinglePrefabObjects"/> is searched: this project uses it exclusively,
        /// and the base <see cref="PrefabObjects"/> exposes no way to enumerate its contents.
        /// </remarks>
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

            // initializeAdded: false leaves prefab id assignment to NetworkManager.Awake, which is
            // the only place that knows the final list order and whether the game is playing.
            prefabs.AddObject(playerPrefab, checkForDuplicates: true, initializeAdded: false);
            return true;
        }

        /// <summary>
        /// Puts FishNet's <see cref="PlayerSpawner"/> on the NetworkManager's own GameObject and
        /// points it at <paramref name="playerPrefab"/>, which is how FishNet 4.x is told to spawn
        /// a prefab whenever a player connects.
        /// </summary>
        /// <remarks>
        /// The spawner must be on the NetworkManager object or a child of it — it resolves the
        /// manager with <c>GetComponentInParent&lt;NetworkManager&gt;()</c>. It is also added only
        /// after <c>NetworkManager.Awake</c> has run, because it subscribes to
        /// <c>SceneManager.OnClientLoadedStartScenes</c> during its own Awake and that manager
        /// sub-object only exists once the NetworkManager has initialised.
        /// </remarks>
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

        /// <summary>
        /// Swaps the primitive's built-in material for the active render pipeline's default.
        /// Without this a capsule in a URP project renders magenta.
        /// </summary>
        private static void ApplyRenderPipelineMaterial(GameObject go)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            Material pipelineDefault = pipeline != null ? pipeline.defaultMaterial : null;
            if (pipelineDefault == null)
                return;

            // sharedMaterial, not material: no per-renderer material instance to leak.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = pipelineDefault;
        }
    }
}