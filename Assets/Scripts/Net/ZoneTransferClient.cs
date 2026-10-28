using System;
using System.Collections;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Client half of a cross-server zone transfer. Listens for the
    /// <see cref="ZoneTransferBroadcast"/> the source server's trigger sends, and performs the
    /// reconnect the issue describes: show a loading screen, disconnect from the current server,
    /// point the transport at the destination, and reconnect. The join announcement is made by
    /// <see cref="JoinRequestClient"/> the moment the new connection comes up, now carrying the
    /// transfer token so the destination's gate can admit the same account.
    ///
    /// <para>
    /// The destination address and token come from the server — never from the client — and the
    /// whole redirect waits for the old socket to actually stop before the transport is retargeted,
    /// because a FishNet client cannot jump servers without a clean stop/start.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Zone Transfer Client")]
    public sealed class ZoneTransferClient : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private JoinRequestClient _joinRequestClient;
        [SerializeField] private GameObject _loadingScreen;
        [SerializeField, Min(0.1f)] private float _retargetTimeoutSeconds = 5f;

        private NakamaApiGateway _gateway;
        private LastZoneInfo _targetZone;

        /// <summary>Raised with a player-facing status line at every step of a transfer.</summary>
        public event Action<string> StatusChanged;

        /// <summary>True from the moment a transfer broadcast lands until the new server admits the client.</summary>
        public bool IsTransferring { get; private set; }

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = GetComponentInParent<NetworkManager>();

            if (_joinRequestClient == null)
                _joinRequestClient = GetComponentInParent<JoinRequestClient>();
        }

        private void Start()
        {
            if (_networkManager == null || _networkManager.ClientManager == null)
                return;

            _networkManager.ClientManager.RegisterBroadcast<ZoneTransferBroadcast>(OnTransferBroadcast);
            _networkManager.ClientManager.OnAuthenticated += OnAdmitted;
        }

        private void OnDestroy()
        {
            if (_networkManager == null || _networkManager.ClientManager == null)
                return;

            _networkManager.ClientManager.UnregisterBroadcast<ZoneTransferBroadcast>(OnTransferBroadcast);
            _networkManager.ClientManager.OnAuthenticated -= OnAdmitted;
        }

        /// <summary>Handles a redirect: arms the transfer token and connects to the destination.</summary>
        public void OnTransferBroadcast(ZoneTransferBroadcast message, Channel channel)
        {
            if (IsTransferring)
                return;

            if (!message.IsValid)
            {
                SetStatus("Zone transfer failed: the destination was malformed.");
                return;
            }

            if (_networkManager == null || _networkManager.ClientManager == null)
                return;

            IsTransferring = true;
            _targetZone = LastZoneInfo.Create(message.TargetHost, message.TargetPort, DateTime.UtcNow);
            SetStatus(string.IsNullOrWhiteSpace(message.Reason) ? ZoneTransferHandoff.DefaultReason : message.Reason);

            if (_loadingScreen != null)
                _loadingScreen.SetActive(true);

            if (_joinRequestClient != null)
                _joinRequestClient.SetTransferToken(message.TransferToken);

            StartCoroutine(Redirect(message));
        }

        /// <summary>
        /// The sequence: stop the current connection, wait until it is actually down, retarget the
        /// transport at the destination, then start a fresh connection. <see cref="JoinRequestClient"/>
        /// announces on the new connection's Started state, carrying the armed transfer token.
        /// </summary>
        private IEnumerator Redirect(ZoneTransferBroadcast message)
        {
            // A long play session can outlive the session token. Refresh before the new join so the
            // destination (and the last-zone write that follows) sees a live token.
            yield return RefreshSession();

            if (_networkManager.ClientManager.Started)
            {
                _networkManager.ClientManager.StopConnection();
                float stoppedBy = Time.realtimeSinceStartup + _retargetTimeoutSeconds;
                while (_networkManager.ClientManager.Started && Time.realtimeSinceStartup < stoppedBy)
                    yield return null;
            }

            Transport transport = _networkManager.TransportManager != null
                ? _networkManager.TransportManager.Transport
                : null;
            if (transport == null)
            {
                SetStatus("Zone transfer failed: no transport.");
                Finish();
                yield break;
            }

            transport.SetClientAddress(message.TargetHost);
            transport.SetPort(message.TargetPort);

            if (!_networkManager.ClientManager.StartConnection())
            {
                SetStatus("Zone transfer failed: could not reach the destination.");
                Finish();
                yield break;
            }

            // Wait for admission (JoinRequestClient broadcasts the token); the loading screen stays
            // up until the destination server has let the same account back in.
            float admittedBy = Time.realtimeSinceStartup + _retargetTimeoutSeconds;
            while (IsTransferring && !IsClientAdmitted() && Time.realtimeSinceStartup < admittedBy)
                yield return null;

            if (!IsClientAdmitted())
            {
                SetStatus("Zone transfer failed: the destination did not admit the connection.");
                Finish();
            }
        }

        private void OnAdmitted()
        {
            if (!IsTransferring)
                return;

            SetStatus("Zone transfer complete.");
            _ = RecordZoneAsync();
            Finish();
        }

        /// <summary>Renews the session token before the redirect; a failure leaves the old one armed.</summary>
        private IEnumerator RefreshSession()
        {
            if (_joinRequestClient == null)
                yield break;

            Task<SessionTokens> refresh = RenewSessionAsync();
            while (!refresh.IsCompleted)
                yield return null;

            if (refresh.Status != TaskStatus.RanToCompletion || refresh.Result == null)
            {
                Debug.LogWarning("Could not renew the session before the zone transfer; using the old token.");
                yield break;
            }

            _joinRequestClient.ApplyRefreshedSession(refresh.Result.Token, refresh.Result.RefreshToken);
        }

        /// <summary>
        /// Refreshes first; if Nakama refuses the refresh token, signs in again with the session's
        /// credentials. The credentials never leave this process.
        /// </summary>
        private async Task<SessionTokens> RenewSessionAsync()
        {
            if (_joinRequestClient == null)
                return null;

            _gateway ??= new NakamaApiGateway();

            if (!string.IsNullOrEmpty(_joinRequestClient.RefreshToken))
            {
                SessionTokens refreshed = await _gateway.RefreshSessionAsync(
                    _joinRequestClient.RefreshToken);

                if (refreshed != null)
                    return refreshed;
            }

            if (!_joinRequestClient.HasCredentials)
                return null;

            LoginResult login = await _gateway.LoginAsync(
                _joinRequestClient.LoginUsername, _joinRequestClient.LoginPassword);

            return login.Success
                ? new SessionTokens(login.Token, login.RefreshToken)
                : null;
        }

        /// <summary>Stores the zone the redirected join was actually admitted into.</summary>
        private async Task RecordZoneAsync()
        {
            if (_joinRequestClient == null || string.IsNullOrEmpty(_joinRequestClient.UserId))
                return;

            /* The transport's GetPort() is the local client socket after connection, not the
             * destination's listening port. The broadcast carried the real target; use it. */
            LastZoneInfo zone = _targetZone;
            _targetZone = null;
            if (zone == null || !zone.IsValid)
                return;

            _gateway ??= new NakamaApiGateway();
            try
            {
                await _gateway.SaveLastZoneAsync(_joinRequestClient.UserId, _joinRequestClient.AuthToken, zone);
            }
            catch (Exception e)
            {
                // The token may have expired between admission and this write. Re-authenticate and
                // retry once; the zone is still known, so a transient 401 must not lose it.
                SessionTokens renewed = await RenewSessionAsync();
                if (renewed == null)
                {
                    Debug.LogWarning($"Could not record the last zone after transfer: {e.Message}");
                    return;
                }

                _joinRequestClient.ApplyRefreshedSession(renewed.Token, renewed.RefreshToken);
                try
                {
                    await _gateway.SaveLastZoneAsync(_joinRequestClient.UserId, renewed.Token, zone);
                }
                catch (Exception retry)
                {
                    Debug.LogWarning($"Could not record the last zone after transfer: {retry.Message}");
                }
            }
        }

        private void Finish()
        {
            IsTransferring = false;
            if (_loadingScreen != null)
                _loadingScreen.SetActive(false);
        }

        private bool IsClientAdmitted()
            => _networkManager.ClientManager != null
               && _networkManager.ClientManager.Connection != null
               && _networkManager.ClientManager.Connection.IsAuthenticated;

        private void SetStatus(string message) => StatusChanged?.Invoke(message);
    }
}