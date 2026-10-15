using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// The one component per zone that wires a scene to the server logic. It owns nothing
    /// gameplay-specific — it connects pieces that already exist:
    ///
    /// <list type="bullet">
    /// <item><b>on server start</b> it turns every authored <see cref="ResourceNodeMarker"/>,
    /// <see cref="CraftingStationMarker"/> and <see cref="EnemyMarker"/> into a networked object
    /// through <c>ServerManager.Spawn</c> — so a node, a station and an enemy exist in exactly the
    /// same state on every client because they only ever exist through the server's spawn path —
    /// and hands the registered <see cref="PlayerSpawnPoint"/>s to FishNet's
    /// <see cref="PlayerSpawner"/>;</item>
    /// <item><b>on player join</b> it reads the loaded save record from
    /// <see cref="PlayerPersistenceService"/> and moves the freshly-spawned player capsule to the
    /// position <see cref="PlayerSpawnPoint.ChooseSpawn"/> picks — nearest to their saved position,
    /// or the zone's default for a fresh character.</item>
    /// </list>
    ///
    /// <para>
    /// Everything is server-only: markers are inert on a pure client, and the join placement runs
    /// from the <see cref="PlayerSpawner.OnSpawned"/> callback, which FishNet only raises on the
    /// server. Placement writes through <see cref="PlayerMover.SetAuthoritativePosition"/>, so the
    /// mover's anti-cheat baseline is updated rather than the position being judged as a teleport.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/World Bootstrap")]
    public sealed class WorldBootstrap : MonoBehaviour
    {
        private NetworkManager _manager;
        private PlayerSpawner _playerSpawner;
        private bool _subscribed;
        private bool _spawnerHooked;

        // One spawned network object per authored marker, keyed by the marker component. Cleared
        // when the server stops, because FishNet despawns and destroys everything it spawned.
        private readonly Dictionary<Component, NetworkObject> _spawnedFromMarkers = new();

        // Built-in fallback, created at most once per server run and kept out of sight like every
        // other runtime source prefab.
        private NetworkObject _fallbackNodePrefab;

        /// <summary>How many world objects this bootstrap has spawned from markers.</summary>
        public int SpawnedWorldObjectCount => _spawnedFromMarkers.Count;

        private void OnEnable()
        {
            _manager = GetComponentInParent<NetworkManager>();
            if (_manager == null)
                _manager = FindFirstObjectByType<NetworkManager>();

            Subscribe();
        }

        private void Start()
        {
            // A dedicated-server process was started with `-zone <name>`; the launcher set this
            // flag and loaded this zone scene, so start the FishNet listener now. A client build
            // loading the same scene (FishNet syncs scenes to clients) leaves the flag false and
            // never opens a server of its own.
            if (!DedicatedServerMode.IsActive)
                return;

            if (_manager == null)
            {
                Debug.LogError($"[DedicatedServer] {nameof(WorldBootstrap)}: no NetworkManager found; cannot start the zone server.");
                return;
            }

            if (_manager.IsServerStarted)
                return;

            Debug.Log($"[DedicatedServer] Starting '{DedicatedServerMode.ZoneName}' zone server...");
            if (!_manager.ServerManager.StartConnection())
                Debug.LogError("[DedicatedServer] StartConnection() returned false.");
        }

        private void Update()
        {
            if (_manager == null)
                return;

            // NetworkManager.Awake creates ServerManager, and the factory attaches the
            // PlayerSpawner after activation; retry each frame until both are present rather than
            // guessing a delay or racing component Awake order.
            if (!_subscribed)
                Subscribe();
            else if (!_spawnerHooked)
                TryHookPlayerSpawner();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || _manager == null || _manager.ServerManager == null)
                return;

            _manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _subscribed = true;
            TryHookPlayerSpawner();

            if (_manager.IsServerStarted)
                SpawnWorld();
        }

        private void TryHookPlayerSpawner()
        {
            if (_spawnerHooked || _manager == null)
                return;

            _playerSpawner = _manager.GetComponent<PlayerSpawner>();
            if (_playerSpawner == null)
                return;

            _playerSpawner.OnSpawned += OnPlayerSpawned;
            _spawnerHooked = true;
        }

        private void Unsubscribe()
        {
            if (_manager != null && _manager.ServerManager != null)
                _manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            if (_spawnerHooked && _playerSpawner != null)
                _playerSpawner.OnSpawned -= OnPlayerSpawned;

            _spawnerHooked = false;
            _subscribed = false;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                SpawnWorld();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // FishNet destroyed every spawned object with the server; drop the bookkeeping so a
                // later restart spawns a fresh world instead of reusing dead references.
                _spawnedFromMarkers.Clear();
                _fallbackNodePrefab = null;
            }
        }

        /// <summary>
        /// Server-only: materialises every authored marker into a networked object and points
        /// FishNet's <see cref="PlayerSpawner"/> at the zone's spawn points. Idempotent — a marker
        /// whose object is still alive is left alone, so a double start cannot duplicate the world,
        /// and one bad marker is logged and skipped rather than aborting the whole spawn.
        /// </summary>
        public void SpawnWorld()
        {
            if (_manager == null || !_manager.IsServerStarted)
                return;

            WireSpawnPoints();

            SpawnFromMarkers(
                FindObjectsByType<ResourceNodeMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnResourceNode);

            SpawnFromMarkers(
                FindObjectsByType<CraftingStationMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnCraftingStation);

            SpawnFromMarkers(
                FindObjectsByType<EnemyMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                SpawnEnemy);
        }

        private void SpawnFromMarkers<T>(T[] markers, System.Func<T, NetworkObject> spawn) where T : Component
        {
            foreach (T marker in markers)
            {
                if (marker == null)
                    continue;

                if (_spawnedFromMarkers.TryGetValue(marker, out NetworkObject existing) &&
                    existing != null && existing.IsSpawned)
                {
                    continue;
                }

                try
                {
                    NetworkObject spawned = spawn(marker);
                    if (spawned != null)
                        _spawnedFromMarkers[marker] = spawned;
                }
                catch (System.Exception e)
                {
                    // One bad marker must not abort the whole world spawn.
                    Debug.LogError($"{nameof(WorldBootstrap)}: '{marker.name}' could not be spawned. {e.Message}", marker);
                }
            }
        }

        private NetworkObject SpawnResourceNode(ResourceNodeMarker marker)
        {
            NetworkObject prefab = marker.NodePrefab != null
                ? EnsureNodeRegistered(marker.NodePrefab)
                : ResolveDefaultNodePrefab();

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no ResourceNode prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            return SpawnAt(prefab, marker.transform);
        }

        private NetworkObject SpawnCraftingStation(CraftingStationMarker marker)
        {
            NetworkObject prefab = marker.StationPrefab != null
                ? EnsureStationRegistered(marker.StationPrefab)
                : ResolveRegisteredPrefab(CraftingStationPrefabFactory.PrefabName);

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no CraftingStation prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            NetworkObject spawned = SpawnAt(prefab, marker.transform);
            if (spawned != null && marker.StationType != CraftingStationType.None)
            {
                CraftingStation station = spawned.GetComponent<CraftingStation>();
                if (station != null)
                    station.Configure(marker.StationType);
            }

            return spawned;
        }

        private NetworkObject SpawnEnemy(EnemyMarker marker)
        {
            NetworkObject prefab = marker.EnemyPrefab != null
                ? EnsureEnemyRegistered(marker.EnemyPrefab)
                : ResolveRegisteredPrefab(TargetDummyPrefabFactory.PrefabName);

            if (prefab == null)
            {
                Debug.LogError($"{nameof(WorldBootstrap)}: no enemy prefab for marker '{marker.name}'; skipping.", marker);
                return null;
            }

            return SpawnAt(prefab, marker.transform);
        }

        private NetworkObject SpawnAt(NetworkObject prefab, Transform marker)
        {
            if (prefab == null)
                return null;

            // A prefab is never itself spawnable: instantiate, activate (a runtime-built source is
            // deactivated), place, then let the server validate and publish the clone.
            GameObject instance = Instantiate(prefab.gameObject);
            instance.name = prefab.name;
            instance.SetActive(true);
            instance.transform.SetPositionAndRotation(marker.position, marker.rotation);

            _manager.ServerManager.Spawn(instance);
            NetworkObject spawned = instance.GetComponent<NetworkObject>();
            return spawned != null && spawned.IsSpawned ? spawned : null;
        }

        private NetworkObject ResolveDefaultNodePrefab()
        {
            NetworkObject registered = ResolveRegisteredPrefab(ResourceNodePrefabFactory.PrefabName);
            if (registered != null)
                return registered;

            if (_fallbackNodePrefab == null)
            {
                _fallbackNodePrefab = ResourceNodePrefabFactory.Create();
                _fallbackNodePrefab.gameObject.SetActive(false);
            }

            return EnsureNodeRegistered(_fallbackNodePrefab);
        }

        private NetworkObject EnsureNodeRegistered(NetworkObject prefab)
        {
            if (prefab == null || _manager == null || _manager.SpawnablePrefabs == null)
                return prefab;

            ResourceNodePrefabFactory.Register(_manager.SpawnablePrefabs, prefab);
            return prefab;
        }

        private NetworkObject EnsureStationRegistered(NetworkObject prefab)
        {
            if (prefab == null || _manager == null || _manager.SpawnablePrefabs == null)
                return prefab;

            CraftingStationPrefabFactory.Register(_manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject EnsureEnemyRegistered(NetworkObject prefab)
        {
            if (prefab == null || _manager == null || _manager.SpawnablePrefabs == null)
                return prefab;

            TargetDummyPrefabFactory.Register(_manager.SpawnablePrefabs, prefab, initializeAdded: true);
            return prefab;
        }

        private NetworkObject ResolveRegisteredPrefab(string prefabName)
        {
            if (_manager == null || _manager.SpawnablePrefabs is not SinglePrefabObjects single)
                return null;

            foreach (NetworkObject prefab in single.Prefabs)
            {
                if (prefab != null && prefab.name == prefabName)
                    return prefab;
            }

            return null;
        }

        /// <summary>
        /// Server-only: hands the zone's authored spawn points to FishNet's
        /// <see cref="PlayerSpawner"/> so the initial server-side spawn lands on one of them. The
        /// authoritative placement is <see cref="PlacePlayerAt"/>; this only makes the pre-placement
        /// position sane for the frame between spawn and placement.
        /// </summary>
        private void WireSpawnPoints()
        {
            if (_playerSpawner == null)
                TryHookPlayerSpawner();

            if (_playerSpawner == null)
                return;

            PlayerSpawnPoint[] points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (points.Length == 0)
                return;

            var transforms = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
                transforms[i] = points[i].transform;

            _playerSpawner.Spawns = transforms;
        }

        private void OnPlayerSpawned(NetworkObject player)
        {
            if (player == null || _manager == null || !_manager.IsServerStarted)
                return;

            Vector3? savedPosition = null;
            PlayerPersistenceService persistence = PlayerPersistenceService.Instance;
            int clientId = player.Owner != null ? player.Owner.ClientId : -1;

            if (persistence != null && clientId >= 0 &&
                persistence.TryGetJoin(clientId, out _, out PlayerSaveRecord record) && record != null)
            {
                savedPosition = record.Position;
            }

            PlacePlayerAt(player, savedPosition);
        }

        /// <summary>
        /// Server-only: moves a freshly-spawned player to the spawn point chosen for them. Exposed
        /// so the placement can be exercised without a live backend record: pass a saved position
        /// and the nearest spawn point wins, pass null and the zone's default wins.
        /// </summary>
        public void PlacePlayerAt(NetworkObject player, Vector3? savedPosition)
        {
            if (player == null || _manager == null || !_manager.IsServerStarted)
                return;

            PlayerSpawnPoint[] points = FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Vector3 position = PlayerSpawnPoint.ChooseSpawn(points, savedPosition);

            PlayerMover mover = player.GetComponent<PlayerMover>();
            if (mover != null)
                mover.SetAuthoritativePosition(position);
            else
                player.transform.position = position;
        }
    }
}
