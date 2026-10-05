using FishNet.Managing.Transporting;
using UnityEngine;

namespace MMO.Networking
{
    public sealed class JoinRequestClient : MonoBehaviour
    {
        [SerializeField] private FishNet.Managing.NetworkManager _networkManager;
        [SerializeField] private string _playerName = "Player";

        public void SetPlayerName(string playerName)
        {
            _playerName = playerName;
        }

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = GetComponent<FishNet.Managing.NetworkManager>();
        }

        private void Start()
        {
            if (_networkManager == null || _networkManager.ClientManager == null)
                return;

            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        private void OnDestroy()
        {
            if (_networkManager != null && _networkManager.ClientManager != null)
                _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        private void OnClientConnectionState(FishNet.Transporting.ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != FishNet.Transporting.LocalConnectionState.Started)
                return;

            _networkManager.ClientManager.Broadcast(new JoinRequestBroadcast
            {
                PlayerName = _playerName
            });
        }
    }
}
