using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using MMO.Crafting;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Single source of truth for the "NetworkManager" GameObject. Both the editor baker
    /// and the tests build the object through here so a scene and a fresh process end up
    /// wired identically — including the server-spawned target dummy that every client sees
    /// in the exact same state because it only ever exists through <c>ServerManager.Spawn</c>.
    /// </summary>
    public static class NetworkManagerFactory
    {
        public const string GameObjectName = "NetworkManager";
        public const string SpawnablePrefabsName = "MMOSpawnablePrefabs";

        /// <summary>
        /// Builds the "NetworkManager" GameObject: transport, spawnables, join gate, the client
        /// half of the handshake, the player spawner that hands every admitted client a player
        /// object, and the target-dummy spawner that puts a server-spawned enemy in the world when
        /// the server starts.
        /// </summary>
        /// <param name="settings">Gate rules; null falls back to the defaults.</param>
        /// <param name="gate">The join gate added to the object.</param>
        /// <param name="playerPrefab">
        /// The prefab to spawn for each connecting client. Pass null and one is built in memory,
        /// which is what a fresh process does. The editor baker passes the saved prefab asset so a
        /// scene references an asset instead of carrying a scene copy.
        /// </param>
        /// <param name="targetDummyPrefab">
        /// The prefab the server spawns as an enemy when it starts. Pass null and one is built in
        /// memory, which is what a fresh process does. The editor baker passes the saved prefab
        /// asset so a scene references an asset instead of carrying a scene copy.
        /// </param>
        public static NetworkManager Create(
            MMOConnectionSettings settings,
            out MMOConnectionGate gate,
            NetworkObject playerPrefab = null,
            NetworkObject targetDummyPrefab = null,
            NetworkObject craftingStationPrefab = null)
        {
            settings ??= new MMOConnectionSettings();

            var go = new GameObject(GameObjectName);

            // Awake() runs the instant a component lands on an *active* object, and
            // NetworkManager.Awake() dereferences SpawnablePrefabs and looks up the
            // Authenticator. Build inactive, wire everything, then activate last.
            go.SetActive(false);

            NetworkObject builtPlayerPrefab = null;
            NetworkObject builtTargetDummyPrefab = null;
            NetworkObject builtCraftingStationPrefab = null;

            try
            {
                Tugboat transport = go.AddComponent<Tugboat>();
                transport.SetPort(settings.Port);
                transport.SetMaximumClients(settings.MaxClients);

                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = SpawnablePrefabsName;

                NetworkManager manager = go.AddComponent<NetworkManager>();
                manager.SpawnablePrefabs = prefabs;

                if (playerPrefab == null)
                {
                    builtPlayerPrefab = PlayerPrefabFactory.Create();
                    playerPrefab = builtPlayerPrefab;

                    // A prefab built at runtime is a real scene object. Keep it out of sight:
                    // FishNet's pool activates the clone it spawns, so an inactive source is
                    // still perfectly spawnable.
                    playerPrefab.gameObject.SetActive(false);
                }

                if (targetDummyPrefab == null)
                {
                    builtTargetDummyPrefab = TargetDummyPrefabFactory.Create();
                    targetDummyPrefab = builtTargetDummyPrefab;
                    targetDummyPrefab.gameObject.SetActive(false);
                }

                if (craftingStationPrefab == null)
                {
                    builtCraftingStationPrefab = CraftingStationPrefabFactory.Create();
                    craftingStationPrefab = builtCraftingStationPrefab;
                    craftingStationPrefab.gameObject.SetActive(false);
                }

                // Before activation, so the prefabs are in the collection when Awake hands out
                // prefab ids from the list order. Both ends build through this same factory, so
                // the player is always id 0 and the target dummy id 1 and crafting station id 2.
                PlayerPrefabFactory.Register(prefabs, playerPrefab);
                TargetDummyPrefabFactory.Register(prefabs, targetDummyPrefab);
                CraftingStationPrefabFactory.Register(prefabs, craftingStationPrefab);

                gate = go.AddComponent<MMOConnectionGate>();
                gate.SetSettings(settings);

                // Tells the server who we are as soon as we connect, which is what
                // lets the gate let us in.
                go.AddComponent<JoinRequestClient>();

                // Re-wires the gate's token validator from the serialized UseTokenAuth flag at
                // scene load — the validator itself is a runtime property Unity cannot serialize.
                go.AddComponent<TokenAuthBootstrap>();

                // Server-side owner of the database connection and the save loop. Its Awake falls
                // back to the Nakama repository; Configure() is only for tests.
                go.AddComponent<PlayerPersistenceService>();

                // Server-side issuer/validator of zone-transfer tokens (and, on the same object,
                // the client that reconnects to the destination zone on a transfer).
                ZoneTransferTokenService tokenService = go.AddComponent<ZoneTransferTokenService>();
                go.AddComponent<ZoneTransferClient>();

                go.SetActive(true);

                // Deliberately after activation. PlayerSpawner subscribes to
                // SceneManager.OnClientLoadedStartScenes from its own Awake, and SceneManager is
                // only created by NetworkManager.Awake — so this must not race component Awake
                // order on the same object. The target dummy spawner needs ServerManager to exist
                // for the same reason.
                PlayerPrefabFactory.Attach(manager, playerPrefab);
                TargetDummyPrefabFactory.Attach(manager, targetDummyPrefab);
                CraftingStationPrefabFactory.Attach(manager, craftingStationPrefab);

                // Give the gate its async loader and let the service claim each loaded record. The
                // gate's unauthenticated broadcast path is open from the moment the server starts,
                // so this must happen before any connection can be admitted — it is.
                var persistence = go.GetComponent<PlayerPersistenceService>();
                persistence.AttachTo(gate);

                // Hand the gate the zone-transfer store — its Awake runs during SetActive above —
                // so a redirected join is consumed against the same tokens the triggers issue.
                gate.TransferTokenStore = tokenService.Store;

                return manager;
            }
            catch
            {
                if (builtPlayerPrefab != null)
                    Object.DestroyImmediate(builtPlayerPrefab.gameObject);

                if (builtTargetDummyPrefab != null)
                    Object.DestroyImmediate(builtTargetDummyPrefab.gameObject);

                if (builtCraftingStationPrefab != null)
                    Object.DestroyImmediate(builtCraftingStationPrefab.gameObject);

                Object.DestroyImmediate(go);
                throw;
            }
        }
    }
}