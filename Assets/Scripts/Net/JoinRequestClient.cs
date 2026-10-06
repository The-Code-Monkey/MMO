using System;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Client half of the join handshake. The moment the local client connects it
    /// announces itself, which is the only way past <see cref="MMOConnectionGate"/>.
    /// The server does the deciding; this only reports who the player is.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Join Request Client")]
    public sealed class JoinRequestClient : MonoBehaviour
    {
        /// <summary>PlayerPrefs key the stable account id is persisted under.</summary>
        public const string AccountIdPrefsKey = "MMO.AccountId";

        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private string _playerName = "Player";

        private string _accountId;

        public string PlayerName => _playerName;

        /// <summary>
        /// Stable account key announced to the server. Generated once as a device GUID and persisted
        /// in PlayerPrefs so the same reeled-in player keeps the same database record across
        /// sessions.
        /// </summary>
        public string AccountId => _accountId;

        public void SetPlayerName(string playerName) => _playerName = playerName ?? string.Empty;

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = GetComponent<NetworkManager>();

            _accountId = EnsureAccountId();
        }

        // Start, not Awake: NetworkManager.Awake creates ClientManager, and component
        // Awake order on a GameObject is not guaranteed.
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

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            SendJoinRequest();
        }

        /// <summary>
        /// Announces the local player to the server. Must happen before authentication,
        /// so the guard is <c>ClientManager.Started</c> (the socket is up) rather
        /// than <see cref="NetworkManager.IsClientStarted"/> (already admitted).
        /// </summary>
        /// <returns>False when there is no active client connection to send over.</returns>
        public bool SendJoinRequest()
        {
            if (_networkManager == null || !_networkManager.ClientManager.Started)
                return false;

            _networkManager.ClientManager.Broadcast(new JoinRequestBroadcast
            {
                PlayerName = _playerName,
                AccountId = _accountId,
            });
            return true;
        }

        private static string EnsureAccountId()
        {
            string accountId = PlayerPrefs.GetString(AccountIdPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(accountId))
            {
                accountId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(AccountIdPrefsKey, accountId);
                PlayerPrefs.Save();
            }

            return accountId;
        }
    }
}