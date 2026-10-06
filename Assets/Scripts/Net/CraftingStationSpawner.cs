using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace MMO.Net
{
    public sealed class CraftingStationSpawner : MonoBehaviour
    {
        [SerializeField]
        private NetworkObject _prefab;

        [SerializeField]
        private Vector3 _spawnPosition = CraftingStationPrefabFactory.DefaultSpawnPosition;

        private NetworkObject _spawned;
        private NetworkManager _manager;
        private bool _subscribed;

        public NetworkObject Prefab => _prefab;
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

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void SetDummyPrefab(NetworkObject prefab)
        {
            _prefab = prefab;

            if (_manager != null && _manager.ServerManager != null && _manager.IsServerStarted)
                SpawnDummies();
        }

        public bool SpawnDummies()
        {
            if (_prefab == null)
                return false;

            if (_manager == null || _manager.ServerManager == null || !_manager.IsServerStarted)
                return false;

            if (_spawned != null && _spawned.IsSpawned)
                return true;

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
                SpawnDummies();
        }
    }
}
