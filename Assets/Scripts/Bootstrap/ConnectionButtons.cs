using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Persistence;
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
            _ = StopAsync();
        }

        /// <summary>
        /// Flushes every dirty player to the backend <em>before</em> the transport stops: once the
        /// server session is gone the writes have no path out, so OnApplicationQuit is too late.
        /// </summary>
        private async Task StopAsync()
        {
            if (PlayerPersistenceService.Instance != null)
                await PlayerPersistenceService.Instance.FlushAsync();

            _networkManager.ServerManager.StopConnection(true);
            _networkManager.ClientManager.StopConnection();
        }
    }
}
