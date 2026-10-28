using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using MMO.Crafting;
using MMO.Persistence;
using MMO.Social;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Single source of truth for the "NetworkManager" GameObject. Both the editor baker
    /// and the tests build the object through here so a scene and a fresh process end up
    /// wired identically — including the registered enemy prefab that scene-marker spawn
    /// controllers instantiate through the server's spawn path.
    /// </summary>
    public static class NetworkManagerFactory
    {
        public const string GameObjectName = "NetworkManager";
        public const string SpawnablePrefabsName = "MMOSpawnablePrefabs";

        /// <summary>
        /// Builds the "NetworkManager" GameObject: transport, spawnables, join gate, the client
        /// half of the handshake, and the player spawner that hands every admitted client a player
        /// object. Enemy populations are owned by scene-marker spawn controllers, not a fixed
        /// dummy spawner.
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
            NetworkObject craftingStationPrefab = null,
            NetworkObject lootBagPrefab = null,
            NetworkObject resourceNodePrefab = null,
            NetworkObject bankTellerPrefab = null)
        {
            settings ??= new MMOConnectionSettings();

            var go = new GameObject(GameObjectName);

            // Awake() runs the instant a component lands on an *active* object, and
            // NetworkManager.Awake() dereferences SpawnablePrefabs and looks up the
            // Authenticator. Build inactive, wire everything, then activate last.
            go.SetActive(false);

            NetworkPrefabSet prefabSet = null;

            try
            {
                Tugboat transport = go.AddComponent<Tugboat>();
                transport.SetPort(settings.Port);
                transport.SetMaximumClients(settings.MaxClients);

                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = SpawnablePrefabsName;

                NetworkManager manager = go.AddComponent<NetworkManager>();
                manager.SpawnablePrefabs = prefabs;

                prefabSet = NetworkPrefabSet.Resolve(
                    playerPrefab, targetDummyPrefab, craftingStationPrefab, lootBagPrefab, resourceNodePrefab, bankTellerPrefab);
                prefabSet.Register(prefabs);

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

                // Server-side owner of global party/guild state. Routes cross-zone social mutations
                // through Nakama; its Awake falls back to the Nakama-backed repository.
                go.AddComponent<SocialHubService>();

                // Server-side issuer/validator of zone-transfer tokens (and, on the same object,
                // the client that reconnects to the destination zone on a transfer).
                ZoneTransferTokenService tokenService = go.AddComponent<ZoneTransferTokenService>();
                go.AddComponent<ZoneTransferClient>();

                go.SetActive(true);

                // Client-side game socket. Activation above ran SocialHubService.Awake in PlayMode,
                // so an authenticated session exists and the socket can be wired now. In EditMode
                // Awake does not run, so Rest is null: skip here and let the hub materialise the
                // socket itself when the server starts (SocialHubService.EnsureSocket). Everything
                // the bus carries is a hint — the hub's reconcile pass is the authority — so a
                // failed connect degrades chat only.
                SocialHubService socialHub = go.GetComponent<SocialHubService>();
                if (socialHub != null && socialHub.Rest != null)
                {
                    NakamaSocialSocket socialSocket = go.AddComponent<NakamaSocialSocket>();
                    socialSocket.Configure(new WebSocketTransport(), socialHub.Rest);
                    socialHub.AttachSocket(socialSocket);
                }

                // Deliberately after activation. PlayerSpawner subscribes to
                // SceneManager.OnClientLoadedStartScenes from its own Awake, and SceneManager is
                // only created by NetworkManager.Awake — so this must not race component Awake
                // order on the same object.
                PlayerPrefabFactory.Attach(manager, prefabSet.Player);
                CraftingStationPrefabFactory.Attach(manager, prefabSet.CraftingStation);

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
                prefabSet?.DestroyBuilt();
                Object.DestroyImmediate(go);
                throw;
            }
        }
    }
}
