using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
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
            Finish();
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