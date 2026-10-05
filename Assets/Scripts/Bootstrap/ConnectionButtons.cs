using FishNet.Managing;
using UnityEngine;

namespace MMO.Bootstrap
{
    public sealed class ConnectionButtons : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        private void Start()
        {
            if (_networkManager == null)
                Debug.LogError($"{nameof(ConnectionButtons)}: no NetworkManager assigned.");
        }

        public void OnHostClicked()
        {
            if (_networkManager.IsServerStarted || _networkManager.IsClientStarted)
                return;

            bool serverStarted = _networkManager.ServerManager.StartConnection();
            bool clientStarted = serverStarted && _networkManager.ClientManager.StartConnection();
            Debug.Log(serverStarted && clientStarted ? "Host started." : "Failed to start host.");
        }

        public void OnClientClicked()
        {
            if (_networkManager.IsClientStarted)
                return;

            bool started = _networkManager.ClientManager.StartConnection();
            Debug.Log(started ? "Client started." : "Failed to start client.");
        }

        public void OnStopClicked()
        {
            _networkManager.ServerManager.StopConnection(true);
            _networkManager.ClientManager.StopConnection();
        }
    }
}
