using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's raid view. Clients submit leader intents; the server validates them through
    /// <see cref="SocialHubService"/> and, once a raid instance is allocated, moves this player to
    /// the instance host through the existing zone-transfer pipeline. Transfer is server-initiated:
    /// the hub raises <see cref="SocialHubService.RaidTransferRequested"/> on the zone owning the
    /// member, and this shell performs the flush → token → broadcast sequence.
    /// </summary>
    public sealed class PlayerRaid : NetworkBehaviour
    {
        /// <summary>Raised on the acting client when the server refuses a raid intent.</summary>
        public event Action<RaidRule> RuleFailed;

        /// <summary>The account id the server resolved for the owning player.</summary>
        public string AccountId => _accountId;

        private string _accountId;

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _accountId = ResolveAccountId();

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
                hub.RaidTransferRequested += OnRaidTransferRequested;
        }

        public override void OnStopNetwork()
        {
            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
                hub.RaidTransferRequested -= OnRaidTransferRequested;
        }

        #region Client intent

        public void RequestAllocateRaid()
        {
            if (IsOwner)
                RpcAllocateRaid();
        }

        public void RequestCompleteRaid()
        {
            if (IsOwner)
                RpcCompleteRaid();
        }

        [ServerRpc]
        private void RpcAllocateRaid(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.AllocateRaidAsync(_accountId));
        }

        [ServerRpc]
        private void RpcCompleteRaid(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.CompleteRaidAsync(_accountId));
        }

        private async Task ResolveRuleAsync(Task<(RaidRule Rule, RaidInstanceRecord Instance)> pending)
        {
            var result = await pending;
            if (result.Rule != RaidRule.Ok)
                TargetNotify(Owner, (int)result.Rule);
        }

        private async Task ResolveRuleAsync(Task<RaidRule> pending)
        {
            RaidRule rule = await pending;
            if (rule != RaidRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((RaidRule)rule);

        #endregion

        #region Transfer

        private void OnRaidTransferRequested(string accountId, RaidInstanceRecord instance)
        {
            if (!IsServer || accountId != _accountId)
                return;

            _ = HandoffAsync(instance);
        }

        /// <summary>
        /// Flush, mint a token bound to the instance host, and broadcast the redirect. A failed
        /// save aborts the handoff so the member is never told to leave before their state lands.
        /// </summary>
        private async Task HandoffAsync(RaidInstanceRecord instance)
        {
            if (instance == null || string.IsNullOrEmpty(instance.HostAddress))
                return;

            NetworkConnection owner = Owner;
            if (owner == null || !owner.IsValid)
                return;

            IZoneTransferTokenStore store = ZoneTransferTokenService.Instance != null
                ? ZoneTransferTokenService.Instance.Store
                : null;
            PlayerPersistenceService persistence = PlayerPersistenceService.Instance;
            if (store == null || persistence == null)
                return;

            var handoff = new ZoneTransferHandoff(
                store,
                instance.HostAddress,
                instance.HostPort,
                instance.HostZoneId,
                ResolveSourceServerId(),
                persistence.FlushAsync);

            ZoneTransferBroadcast? redirect = await handoff.RunAsync(_accountId, "Raid instance", CancellationToken.None);
            if (redirect == null || owner == null || !owner.IsValid)
                return;

            NetworkManager manager = NetworkManager;
            if (manager != null && manager.ServerManager != null)
                manager.ServerManager.Broadcast(owner, redirect.Value, requireAuthenticated: true);
        }

        private string ResolveSourceServerId()
        {
            SocialHubService hub = SocialHubService.Instance;
            return hub != null ? hub.ZoneId : SystemInfo.deviceName;
        }

        #endregion

        private string ResolveAccountId()
        {
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
