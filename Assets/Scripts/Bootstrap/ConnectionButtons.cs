using FishNet.Managing;
using UnityEngine;

namespace MMO.Bootstrap
{
    public sealed class ConnectionButtons : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        private void Start()
        {
            // Standalone servers/headless clients report no buttons.
            if (_networkManager == null)
            {
                Debug.LogError($"{nameof(ConnectionButtons)}: no NetworkManager assigned.");
                return;
            }
        }

        public void OnHostClicked()
        {
            if (IsAnyConnectionStarted())
                return;

            bool server = _networkManager.ServerManager.StartConnection();
            bool client = _networkManager.ClientManager.StartConnection();

            Debug.Log(server && client ? "Host started." : "Failed to start host.");
        }

        public void OnClientClicked()
        {
            if (IsAnyConnectionStarted())
                return;

            bool started = _networkManager.ClientManager.StartConnection();
            Debug.Log(started ? "Client started." : "Failed to start client.");
        }

        public void OnStopClicked()
        {
            if (_networkManager == null)
                return;

            _networkManager.ServerManager.StopConnection(true);
            _networkManager.ClientManager.StopConnection();
        }

        private bool IsAnyConnectionStarted() =>
            _networkManager != null &&
            (_networkManager.ServerManager.Started || _networkManager.ClientManager.Started);
    }
}
