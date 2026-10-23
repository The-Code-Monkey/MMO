using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Auction;
using MMO.Client;
using MMO.Content;
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
        /// <see cref="PlayerAbilities"/> that carries this player's ability slots, and
        /// <see cref="PlayerGathering"/> that runs the server-side gathering loop this player
        /// started. The <see cref="LocalPlayerHud"/> gives the owning client its screen (carrying
        /// the crafting panel), and the <see cref="PlayerMover"/> is the server-authoritative
        /// writer of this player's position.
        ///
        /// The camera deliberately does <em>not</em> travel with the prefab: the one rig a client
        /// looks through is the scene's <c>CameraRig</c>, claimed by tag rather than by a
        /// per-player script, so a prefab carrying a camera claimer would race every other
        /// player's copy for it.
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

            // The death rules: subscribes to PlayerStats.Died and applies the zone-tier outcome.
            // After the stats and inventory it reads, and it resolves the loot-bag prefab from the
            // NetworkManager's spawnable collection rather than a per-prefab reference.
            go.AddComponent<PlayerDeath>();

            // Wallet and auction house for player-driven trading.
            go.AddComponent<PlayerWallet>();
            go.AddComponent<PlayerAuctionHouse>();

            // Quest state and the server-walked dialogue session. They sit before the save trigger
            // so a loaded quest state can be applied through them later.
            go.AddComponent<PlayerQuestLog>();
            go.AddComponent<PlayerDialogue>();

            // Added last so its OnStartNetwork runs after the stats/inventory it applies a loaded
            // save through: it consumes the record the join gate fetched and subscribes the save
            // events only once that apply has settled.
            go.AddComponent<PlayerSaveTrigger>();

            // The starter donkey: after the save trigger so a restored bag is checked first and
            // an existing donkey is not granted a second time.
            go.AddComponent<PlayerStartingItems>();

            // The social surface: intent-only RPCs whose state lives behind SocialHubService. After
            // the save trigger so ResolveIdentity finds this player's account already in the join cache.
            go.AddComponent<PlayerParty>();
            go.AddComponent<PlayerGuild>();

            // Chat and the friend list ride the same hub. Both resolve the account id from the join
            // cache the save trigger populated, so they land after it like the party/guild surfaces.
            go.AddComponent<PlayerChat>();
            go.AddComponent<PlayerFriends>();
            go.AddComponent<PlayerRaid>();

            // The server-authoritative mover: the only thing allowed to write this player's
            // position. The input drivers that submit to it come after it, so they can resolve it
            // from the same GameObject.
            go.AddComponent<PlayerMover>();

            // Mount state: drives the mover's speed and flight and reads the bag for the mount
            // item, so it sits after both. Its RPCs are owner-checked like every other shell.
            go.AddComponent<PlayerMount>();

            // Aura state: reads the player's stats and mover it scales, and gates actions through
            // the server-only BlocksActions/BlocksMovement properties.
            go.AddComponent<AuraController>();

            // Owner-side click-to-move: turns a mouse click into an intent through
            // PlayerMover.RequestMoveTo, where the server does the NavMesh validation. After
            // PlayerMover so it can reference it, and before NetworkTransform and the HUD so
            // neither of those shifts an index the baked prefab already uses.
            go.AddComponent<PlayerClickToMove>();

            // Syncs the server-authoritative transform to clients so movement is visible.
            go.AddComponent<NetworkTransform>();

            // Binds the local camera to this client's own player, by ownership rather than a tag
            // search, so a second player in the zone cannot steal the view. Clients only; a
            // dedicated server owns nothing local.
            go.AddComponent<LocalPlayerCameraTarget>();

            // The client HUD: the owning client builds the whole screen (vitals, inventory, action
            // bar, chat and the crafting panel) and binds it to this player's own sources. Does
            // nothing on a copy this client does not own, and builds nothing on a dedicated
            // server. Appended last so every behaviour added before it keeps the NetworkBehaviour
            // and RPC index the baked prefab already uses.
            go.AddComponent<LocalPlayerHud>();

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
        /// <remarks>
        /// A collection serialized by an earlier bake can hold null or destroyed entries, or can
        /// have the player somewhere other than index 0. Either desyncs the ids the two ends hand
        /// out (and FishNet logs "Prefab on id N is null"), so <see cref="SinglePrefabObjects"/>
        /// is rebuilt in place: <c>Clear()</c> then the player first, then the surviving prefabs
        /// in their original relative order. There is no surgical removal — the class exposes
        /// only <c>Clear()</c> — and re-adding is what keeps both ends canonical.
        /// </remarks>
        /// <returns>
        /// True when this call added or re-ordered the prefab, false when it was a no-op.
        /// </returns>
        public static bool Register(PrefabObjects prefabs, NetworkObject playerPrefab)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (playerPrefab == null)
                return false;

            if (prefabs is SinglePrefabObjects single && NeedsRewire(single, playerPrefab))
            {
                Rewire(single, playerPrefab);
                return true;
            }

            if (Contains(prefabs, playerPrefab))
                return false;

            // initializeAdded: false leaves prefab id assignment to NetworkManager.Awake, which is
            // the only place that knows the final list order and whether the game is playing.
            prefabs.AddObject(playerPrefab, checkForDuplicates: true, initializeAdded: false);
            return true;
        }

        /// <summary>
        /// True when the collection is not already canonical: it holds a null (or destroyed)
        /// entry, or does not have <paramref name="playerPrefab"/> at index 0.
        /// </summary>
        private static bool NeedsRewire(SinglePrefabObjects prefabs, NetworkObject playerPrefab)
        {
            IReadOnlyList<NetworkObject> list = prefabs.Prefabs;

            if (list.Count == 0)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                    return true;
            }

            return list[0] != playerPrefab;
        }

        /// <summary>
        /// Rebuilds the collection: <c>Clear()</c>, the player, then every surviving prefab in
        /// its original relative order.
        /// </summary>
        private static void Rewire(SinglePrefabObjects prefabs, NetworkObject playerPrefab)
        {
            var survivors = new List<NetworkObject>();
            foreach (NetworkObject existing in prefabs.Prefabs)
            {
                if (existing != null && existing != playerPrefab)
                    survivors.Add(existing);
            }

            prefabs.Clear();

            // checkForDuplicates: false — the list was just cleared, so nothing can collide.
            prefabs.AddObject(playerPrefab, checkForDuplicates: false, initializeAdded: false);
            if (survivors.Count > 0)
                prefabs.AddObjects(survivors, checkForDuplicates: false, initializeAdded: false);
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
