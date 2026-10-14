using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
    ///
    /// <para>
    /// Since #54 a raid groups several parties, so the derived raid roster is replicated as one JSON
    /// <c>SyncVar</c> — the same string-payload rationale as <see cref="PlayerParty"/>'s roster —
    /// and the hub repaints it through <see cref="SocialHubService.RaidChanged"/>.
    /// </para>
    /// </summary>
    public sealed class PlayerRaid : NetworkBehaviour
    {
        private readonly SyncVar<string> _raidRosterJson = new SyncVar<string>();

        /// <summary>Raised on the acting client when the server refuses a raid intent.</summary>
        public event Action<RaidRule> RuleFailed;

        /// <summary>Raised on the owning client whenever the replicated raid roster changes.</summary>
        public event Action RaidRosterChanged;

        /// <summary>The account id the server resolved for the owning player.</summary>
        public string AccountId => _accountId;

        /// <summary>The replicated raid roster as JSON, or null when this player is not raiding.</summary>
        public string RaidRosterJson => _raidRosterJson.Value;

        private string _accountId;

        public override void OnStartNetwork()
        {
            _raidRosterJson.OnChange += OnRaidRosterJsonChanged;

            if (!IsServer)
                return;

            _accountId = ResolveAccountId();

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.RaidTransferRequested += OnRaidTransferRequested;
                hub.RaidReturnRequested += OnRaidReturnRequested;
                hub.RaidChanged += OnRaidChanged;
            }
        }

        public override void OnStopNetwork()
        {
            _raidRosterJson.OnChange -= OnRaidRosterJsonChanged;

            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.RaidTransferRequested -= OnRaidTransferRequested;
                hub.RaidReturnRequested -= OnRaidReturnRequested;
                hub.RaidChanged -= OnRaidChanged;
            }
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

        /// <summary>Leader-only intent: joins the requester's party into an existing raid.</summary>
        public void RequestJoinRaid(string raidId)
        {
            if (IsOwner && !string.IsNullOrEmpty(raidId))
                RpcJoinRaid(raidId);
        }

        /// <summary>Leader-only intent: pulls the requester's party out of its raid.</summary>
        public void RequestLeaveRaid()
        {
            if (IsOwner)
                RpcLeaveRaid();
        }

        /// <summary>Raid-leader-only intent: hands raid leadership to another member.</summary>
        public void RequestPromoteLeader(string targetAccountId)
        {
            if (IsOwner && !string.IsNullOrEmpty(targetAccountId))
                RpcPromoteLeader(targetAccountId);
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

        [ServerRpc]
        private void RpcJoinRaid(string raidId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.JoinRaidAsync(_accountId, raidId));
        }

        [ServerRpc]
        private void RpcLeaveRaid(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.LeaveRaidAsync(_accountId));
        }

        [ServerRpc]
        private void RpcPromoteLeader(string targetAccountId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || SocialHubService.Instance == null)
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.PromoteRaidLeaderAsync(_accountId, targetAccountId));
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

        #region Backend → client delivery

        /// <summary>
        /// Called by the hub for any hosted raid change. We only apply a roster we are actually a
        /// member of; another party's raid on this zone must never overwrite our view.
        /// </summary>
        private void OnRaidChanged(RaidInstanceRecord record)
        {
            if (!IsServer || record == null)
                return;

            if (IsRaidMember(record, _accountId))
                ApplyRaid(record);
        }

        /// <summary>Server: pushes the authoritative raid roster into the replicated SyncVar.</summary>
        [Server]
        public void ApplyRaid(RaidInstanceRecord record)
        {
            string json = record == null ? null : JsonUtility.ToJson(record);
            if (_raidRosterJson.Value != json)
                _raidRosterJson.Value = json;
        }

        private void OnRaidRosterJsonChanged(string previous, string current, bool asServer)
            => RaidRosterChanged?.Invoke();

        private static bool IsRaidMember(RaidInstanceRecord record, string accountId)
        {
            if (record?.Members == null || string.IsNullOrEmpty(accountId))
                return false;

            foreach (RaidInstanceMember member in record.Members)
            {
                if (member.AccountId == accountId)
                    return true;
            }

            return false;
        }

        #endregion

        #region Transfer

        private void OnRaidTransferRequested(string accountId, RaidInstanceRecord instance)
        {
            if (!IsServer || accountId != _accountId)
                return;

            _ = HandoffAsync(instance);
        }

        private void OnRaidReturnRequested(string accountId, RaidReturnAnchor anchor)
        {
            if (!IsServer || accountId != _accountId)
                return;

            _ = HandoffHomeAsync(anchor);
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

        /// <summary>
        /// Flush, mint a token bound to the member's origin zone, and broadcast the redirect.
        /// Used when the raid completes to send the member back home.
        /// </summary>
        private async Task HandoffHomeAsync(RaidReturnAnchor anchor)
        {
            if (string.IsNullOrEmpty(anchor.OriginHost) || anchor.OriginPort == 0 || string.IsNullOrEmpty(anchor.OriginServerId))
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
                anchor.OriginHost,
                anchor.OriginPort,
                anchor.OriginServerId,
                ResolveSourceServerId(),
                persistence.FlushAsync);

            ZoneTransferBroadcast? redirect = await handoff.RunAsync(_accountId, "Raid completed", CancellationToken.None);
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
