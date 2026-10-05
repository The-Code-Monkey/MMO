using FishNet;
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
            if (_networkManager.IsListening)
                return;

            bool started = _networkManager.StartHost();
            Debug.Log(started ? "Host started." : "Failed to start host.");
        }

        public void OnClientClicked()
        {
            if (_networkManager.IsListening)
                return;

            bool started = _networkManager.StartClient();
            Debug.Log(started ? "Client started." : "Failed to start client.");
        }

        public void OnStopClicked() => _networkManager.StopAll();
    }
}
