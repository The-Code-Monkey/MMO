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
        private string _authToken = string.Empty;
        private string _transferToken = string.Empty;

        public string PlayerName => _playerName;

        /// <summary>
        /// The session token received from the login screen, announced to the server so its gate
        /// can authenticate the join. Empty when token authentication is not in use.
        /// </summary>
        public string AuthToken => _authToken;

        /// <summary>
        /// Stable account key announced to the server. Generated once as a device GUID and persisted
        /// in PlayerPrefs so the same reeled-in player keeps the same database record across
        /// sessions.
        /// </summary>
        public string AccountId => _accountId;

        /// <summary>
        /// Single-shot zone-transfer authorization, armed by <see cref="ZoneTransferClient"/> just
        /// before it reconnects to the destination zone and cleared the moment the join announcement
        /// is sent, so a later reconnect never replays a consumed token.
        /// </summary>
        public string TransferToken => _transferToken;

        public void SetPlayerName(string playerName) => _playerName = playerName ?? string.Empty;

        /// <summary>Points the join announcement at the session token the login screen received.</summary>
        public void SetAuthToken(string authToken) => _authToken = authToken ?? string.Empty;

        /// <summary>Arms the next join announcement with a zone-transfer token.</summary>
        public void SetTransferToken(string transferToken) => _transferToken = transferToken ?? string.Empty;

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
        /// The join announcement exactly as <see cref="SendJoinRequest"/> would send it. Public so
        /// the wiring — name, account id, session token, transfer token — is testable without a
        /// socket.
        /// </summary>
        public JoinRequestBroadcast BuildJoinRequest()
            => new JoinRequestBroadcast
            {
                PlayerName = _playerName,
                AccountId = _accountId,
                AuthToken = _authToken,
                TransferToken = _transferToken,
            };

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

            _networkManager.ClientManager.Broadcast(BuildJoinRequest());
            _transferToken = string.Empty;
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