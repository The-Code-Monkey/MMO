using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Lives on the NetworkManager GameObject and turns a "server started" event into a target
    /// dummy in the world. The dummy is a <em>server-spawned</em> entity: this component
    /// instantiates the prefab and hands it to <c>ServerManager.Spawn</c>, so every client sees the
    /// dummy in the exact same state because it only ever exists through the server's spawn path.
    /// Clients never author it and no scene carries it.
    /// </summary>
    public sealed class TargetDummySpawner : MonoBehaviour
    {
        [SerializeField]
        private NetworkObject _prefab;

        [SerializeField]
        private Vector3 _spawnPosition = TargetDummyPrefabFactory.DefaultSpawnPosition;

        private NetworkObject _spawned;
        private NetworkManager _manager;
        private bool _subscribed;

        /// <summary>The prefab the server instantiates, or null until it is configured.</summary>
        public NetworkObject Prefab => _prefab;

        /// <summary>The spawned copy, or null while no dummy exists.</summary>
        public NetworkObject Spawned => _spawned;

        private void OnEnable()
        {
            _manager = GetComponentInParent<NetworkManager>();
            if (_manager == null)
            {
                Debug.LogError($"{name} is not under a FishNet NetworkManager; no dummy can be spawned.", this);
                return;
            }

            Subscribe();
        }

        private void Update()
        {
            if (_subscribed || _manager == null)
                return;

            // NetworkManager.Awake creates the ServerManager; until it has, there is no event to
            // listen on. This only matters when the spawner lands on an already-active object
            // before the manager initialised, so retry each frame rather than guess a delay.
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>
        /// Configures the prefab to spawn. Safe to call again; a later call repoints the spawner.
        /// </summary>
        public void SetDummyPrefab(NetworkObject prefab)
        {
            _prefab = prefab;

            // The server event only fires on transitions. If the server is already listening when
            // the prefab arrives (e.g. the spawner was attached before the prefab was configured),
            // catch up on the spot, otherwise OnServerConnectionState will do it.
            if (_manager != null && _manager.ServerManager != null && _manager.IsServerStarted)
                SpawnDummies();
        }

        /// <summary>
        /// Instantiates the dummy prefab and spawns it over the network. Called from the server's
        /// connection-state event; idempotent, so an already-present dummy is left alone.
        /// </summary>
        /// <returns>True when a dummy is (or already was) in the world.</returns>
        public bool SpawnDummies()
        {
            if (_prefab == null)
                return false;

            if (_manager == null || _manager.ServerManager == null || !_manager.IsServerStarted)
                return false;

            if (_spawned != null && _spawned.IsSpawned)
                return true;

            // A prefab is never itself spawnable — ServerManager.Spawn on an asset aborts with
            // "must instantiate the prefab first". Instantiate, then activate (a runtime-built
            // source is deactivated), then let the server validate and publish the clone.
            GameObject instance = Instantiate(_prefab.gameObject);
            instance.name = _prefab.name;
            instance.SetActive(true);
            instance.transform.position = _spawnPosition;

            _manager.ServerManager.Spawn(instance);
            _spawned = instance.GetComponent<NetworkObject>();
            return true;
        }

        private void Subscribe()
        {
            if (_subscribed || _manager == null || _manager.ServerManager == null)
                return;

            _manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _subscribed = true;

            // The event only reports transitions; catch a server that was already running when
            // this component subscribed.
            if (_manager.IsServerStarted)
                SpawnDummies();
        }

        private void Unsubscribe()
        {
            if (_subscribed && _manager != null && _manager.ServerManager != null)
                _manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            _subscribed = false;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                SpawnDummies();
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // FishNet despawns and destroys every spawned object on stop; drop the reference so
                // a later restart spawns a fresh dummy instead of reusing a dead one.
                _spawned = null;
            }
        }
    }
}