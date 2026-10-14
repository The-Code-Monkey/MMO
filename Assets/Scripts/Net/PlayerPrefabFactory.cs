using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Auction;
using MMO.Client;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Builds the network player object — a capsule carrying a FishNet <see cref="NetworkObject"/>
    /// and the project's own <see cref="PlayerStats"/> and <see cref="PlayerCombat"/> — and wires
    /// it into a <see cref="NetworkManager"/> so the server spawns one for every client that
    /// connects.
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
        /// the project's own networked <see cref="PlayerStats"/>, <see cref="PlayerCombat"/>, its
        /// <see cref="PlayerInventory"/> that owns this player's bag and equipment containers, its
        /// <see cref="PlayerAbilities"/> that carries this player's ability slots, its
        /// <see cref="PlayerGathering"/> that runs the server-side gathering loop this player
        /// started, and the <see cref="LocalPlayerCamera"/> that gives the owning client
        /// something to look through.
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
            // PlayerCombat spends the vitals it sits next to. PlayerInventory follows PlayerStats
            // because it pushes recalculated equipment bonuses into PlayerStats whenever either
            // container changes, and PlayerAbilities comes after the inventory its slots are
            // filled from: equipment-injected abilities ride along with the vitals they spend, so
            // the server fills these slots from gear before it validates any cast through them.
            // PlayerGathering comes after PlayerInventory because that is the bag its server-side
            // tick awards harvested items into and the equipment its tool checks read.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerStats>();
            go.AddComponent<PlayerCombat>();
            go.AddComponent<PlayerInventory>();
            go.AddComponent<PlayerAbilities>();
            go.AddComponent<PlayerGathering>();
            go.AddComponent<MMO.Crafting.PlayerCrafting>();

            // Wallet and auction house for player-driven trading.
            go.AddComponent<PlayerWallet>();
            go.AddComponent<PlayerAuctionHouse>();

            // Added last so its OnStartNetwork runs after the stats/inventory it applies a loaded
            // save through: it consumes the record the join gate fetched and subscribes the save
            // events only once that apply has settled.
            go.AddComponent<PlayerSaveTrigger>();

            // The social surface: intent-only RPCs whose state lives behind SocialHubService. After
            // the save trigger so ResolveIdentity finds this player's account already in the join cache.
            go.AddComponent<PlayerParty>();
            go.AddComponent<PlayerGuild>();

            // Chat and the friend list ride the same hub. Both resolve the account id from the join
            // cache the save trigger populated, so they land after it like the party/guild surfaces.
            go.AddComponent<PlayerChat>();
            go.AddComponent<PlayerFriends>();
            go.AddComponent<PlayerRaid>();

            // Travels with the prefab so a client that receives its player object has something to
            // look through. It decides whether it may drive the camera by asking IsOwner, which is
            // why it is safe for every client's copy of this prefab to carry it.
            go.AddComponent<LocalPlayerCamera>();

            // The material swap is shared with the other primitive factories, so a player and a
            // dummy never end up with different pipeline handling.
            PrimitiveAppearance.ApplyRenderPipelineMaterial(go);
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
    }
}
