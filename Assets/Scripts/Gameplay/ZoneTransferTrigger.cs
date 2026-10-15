using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A physical trigger volume on the edge of a zone — the road to the next town. When the server
    /// sees a player's authoritative capsule enter it, it runs the handoff in
    /// <see cref="ZoneTransferHandoff"/>: force a final database save, mint a transfer token, then
    /// tell that client to disconnect and reconnect to the destination server with the token. The
    /// client's <see cref="ZoneTransferClient"/> performs the reconnect; the destination server's
    /// gate consumes the token and loads the freshly saved record.
    ///
    /// <para>
    /// Everything here is server-side: the client reaches its own copy of this scene but never has
    /// <c>IsServerStarted</c>, so it only ever re-acts to the resulting broadcast. If the save
    /// cannot be completed the handoff is aborted and the player stays put, ready to retry on the
    /// next entry rather than arriving at the destination as a fresh character.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Gameplay/Zone Transfer Trigger")]
    public sealed class ZoneTransferTrigger : MonoBehaviour
    {
        /// <summary>Destination host used when the inspector value is blank.</summary>
        public const string DefaultTargetHost = "127.0.0.1";

        /// <summary>Destination port used when the inspector value is zero.</summary>
        public const ushort DefaultTargetPort = 7777;

        [Header("Destination")]
        [SerializeField] private string _targetHost = DefaultTargetHost;
        [SerializeField] private ushort _targetPort = DefaultTargetPort;
        [SerializeField] private string _targetServerId = "server-b";
        [SerializeField] private string _targetZoneName = "Zone transition";

        [Header("Handoff")]
        [SerializeField] private string _sourceServerId = string.Empty;
        [SerializeField] private bool _disconnectAfterSend = true;
        [SerializeField, Min(0f)] private float _graceSeconds = 0.5f;

        private readonly HashSet<int> _transferringClientIds = new();

        private NetworkManager _networkManager;
        private IZoneTransferTokenStore _tokenStore;

        /// <summary>True while a handoff for this client is being prepared.</summary>
        public bool IsTransferring(int clientId) => _transferringClientIds.Contains(clientId);

        private void Awake()
        {
            Collider volume = GetComponent<Collider>();
            if (volume != null)
                volume.isTrigger = true;

            _networkManager = FindFirstObjectByType<NetworkManager>();
            ResolveStore();
        }

        /// <summary>Injects the token store; production resolves it from <see cref="ZoneTransferTokenService"/>.</summary>
        public void SetTokenStore(IZoneTransferTokenStore store) => _tokenStore = store;

        private void OnTriggerEnter(Collider other)
        {
            if (_networkManager == null || !_networkManager.IsServerStarted)
                return;

            NetworkObject player = other != null ? other.GetComponentInParent<NetworkObject>() : null;
            if (player == null || !player.IsSpawned)
                return;

            NetworkConnection owner = player.Owner;
            if (owner == null || !owner.IsValid)
                return;

            if (!_transferringClientIds.Add(owner.ClientId))
                return;

            _ = HandoffAsync(player, owner);
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null)
                return;

            NetworkObject player = other.GetComponentInParent<NetworkObject>();
            if (player != null && player.Owner != null)
                _transferringClientIds.Remove(player.Owner.ClientId);
        }

        private async System.Threading.Tasks.Task HandoffAsync(NetworkObject player, NetworkConnection owner)
        {
            int clientId = owner.ClientId;
            bool disconnectScheduled = false;

            try
            {
                if (_tokenStore == null)
                    ResolveStore();

                if (_tokenStore == null)
                {
                    Abort(clientId, "no zone transfer token service on this server");
                    return;
                }

                PlayerPersistenceService persistence = PlayerPersistenceService.Instance;
                if (persistence == null)
                {
                    Abort(clientId, "no persistence service on this server");
                    return;
                }

                PlayerSaveTrigger save = player.GetComponent<PlayerSaveTrigger>();
                string accountId = save != null ? save.AccountId : null;
                if (string.IsNullOrEmpty(accountId) &&
                    !persistence.TryGetJoin(clientId, out accountId, out _))
                {
                    accountId = null;
                }

                if (string.IsNullOrEmpty(accountId))
                {
                    Abort(clientId, "no account id for the entering player");
                    return;
                }

                // Snapshot while the SyncVars are still live and give it priority over the live
                // capture for the one flush that follows.
                PlayerSaveRecord snapshot = save != null ? save.CaptureNow() : null;
                if (snapshot != null)
                    persistence.QueueFinalSave(accountId, snapshot);

                var handoff = new ZoneTransferHandoff(
                    _tokenStore,
                    _targetHost,
                    _targetPort,
                    _targetServerId,
                    ResolveSourceServerId(),
                    persistence.FlushAsync);

                ZoneTransferBroadcast? redirect = await handoff.RunAsync(accountId, _targetZoneName, CancellationToken.None);
                if (redirect == null)
                {
                    Abort(clientId, "the final save could not be completed");
                    return;
                }

                if (!owner.IsValid)
                {
                    _transferringClientIds.Remove(clientId);
                    return;
                }

                _networkManager.ServerManager.Broadcast(owner, redirect.Value, requireAuthenticated: true);

                if (_disconnectAfterSend)
                {
                    disconnectScheduled = true;
                    StartCoroutine(DisconnectAfterGrace(owner));
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(ZoneTransferTrigger)}: handoff failed for client {clientId}. {e.Message}", this);
                Abort(clientId, e.Message);
            }
            finally
            {
                if (!disconnectScheduled)
                    _transferringClientIds.Remove(clientId);
            }
        }

        private IEnumerator DisconnectAfterGrace(NetworkConnection owner)
        {
            yield return new WaitForSecondsRealtime(_graceSeconds);

            if (owner != null && owner.IsValid)
                owner.Disconnect(immediately: false);

            _transferringClientIds.Remove(owner != null ? owner.ClientId : -1);
        }

        private void Abort(int clientId, string reason)
        {
            _transferringClientIds.Remove(clientId);
            Debug.LogWarning($"{nameof(ZoneTransferTrigger)}: zone transfer aborted for client {clientId}: {reason}.", this);
        }

        private void ResolveStore()
        {
            if (_tokenStore == null && ZoneTransferTokenService.Instance != null)
                _tokenStore = ZoneTransferTokenService.Instance.Store;
        }

        private string ResolveSourceServerId()
            => string.IsNullOrWhiteSpace(_sourceServerId) ? SystemInfo.deviceName : _sourceServerId;
    }
}
