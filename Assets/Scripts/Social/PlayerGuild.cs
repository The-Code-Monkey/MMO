using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's guild view, the guild half of <see cref="PlayerParty"/>: intent-only RPCs to the
    /// server, an authoritative roster replicated back through SyncVars. Guild membership persists
    /// across sessions (unlike parties), so a reconnecting player's guild reappears on the next
    /// reconcile pass.
    /// </summary>
    public sealed class PlayerGuild : NetworkBehaviour
    {
        private readonly SyncVar<string> _guildId = new SyncVar<string>();
        private readonly SyncVar<string> _rosterJson = new SyncVar<string>();

        private string _accountId;

        /// <summary>Raised on the owning client whenever the replicated guild roster changes.</summary>
        public event Action RosterChanged;

        /// <summary>Raised on the invited client when a guild invite popup should appear.</summary>
        public event Action<string, string> InviteReceived;

        /// <summary>Raised on the acting client when the server refuses an intent.</summary>
        public event Action<GuildRule> RuleFailed;

        public string GuildId => _guildId.Value;

        public string RosterJson => _rosterJson.Value;

        public override void OnStartNetwork()
        {
            _rosterJson.OnChange += OnRosterChanged;

            if (!IsServer)
                return;

            ResolveAccountId();
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.GuildChanged += OnGuildChanged;
                hub.GuildInviteReceived += OnGuildInviteReceived;
            }
        }

        public override void OnStopNetwork()
        {
            _rosterJson.OnChange -= OnRosterChanged;

            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.GuildChanged -= OnGuildChanged;
                hub.GuildInviteReceived -= OnGuildInviteReceived;
            }
        }

        #region Client intent

        public void RequestCreate(string guildName)
        {
            if (!OwnsSelf() || string.IsNullOrWhiteSpace(guildName))
                return;

            RpcCreate(guildName);
        }

        public void RequestInvite(string targetName)
        {
            if (!OwnsSelf() || string.IsNullOrWhiteSpace(targetName))
                return;

            RpcInvite(targetName);
        }

        public void RequestAcceptInvite()
        {
            if (OwnsSelf())
                RpcAcceptInvite();
        }

        public void RequestLeave()
        {
            if (OwnsSelf())
                RpcLeave();
        }

        public void RequestPromote(string targetAccountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(targetAccountId))
                return;

            RpcPromote(targetAccountId);
        }

        public void RequestKick(string targetAccountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(targetAccountId))
                return;

            RpcKick(targetAccountId);
        }

        [ServerRpc]
        private void RpcCreate(string guildName, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.CreateGuildAsync(_accountId, guildName));
        }

        [ServerRpc]
        private void RpcInvite(string targetName, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = InviteAsync(targetName);
        }

        [ServerRpc]
        private void RpcAcceptInvite(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.AcceptGuildInviteAsync(_accountId));
        }

        [ServerRpc]
        private void RpcLeave(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.LeaveGuildAsync(_accountId));
        }

        [ServerRpc]
        private void RpcPromote(string targetAccountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.PromoteAsync(_accountId, targetAccountId));
        }

        [ServerRpc]
        private void RpcKick(string targetAccountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.KickFromGuildAsync(_accountId, targetAccountId));
        }

        private async System.Threading.Tasks.Task InviteAsync(string targetName)
        {
            GuildRule rule = await SocialHubService.Instance.InviteToGuildAsync(_accountId, targetName);
            if (rule != GuildRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        private async System.Threading.Tasks.Task ResolveRuleAsync(System.Threading.Tasks.Task<GuildRule> pending)
        {
            GuildRule rule = await pending;
            if (rule != GuildRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        #endregion

        #region Backend → client delivery

        private void OnGuildInviteReceived(string accountId, PendingGuildInvite invite)
        {
            if (!IsServer || accountId != _accountId)
                return;

            TargetReceiveInvite(Owner, invite.FromPlayerName, invite.GuildId);
        }

        private void OnGuildChanged(GuildRecord record)
        {
            if (!IsServer)
                return;

            if (record == null)
            {
                ClearRoster();
                return;
            }

            if (GuildRules.IsMember(record, _accountId))
                ApplyRoster(record);
            else if (_guildId.Value == record.GuildId)
                ClearRoster();
        }

        [Server]
        public void ApplyRoster(GuildRecord record)
        {
            _guildId.Value = record?.GuildId;
            _rosterJson.Value = record == null ? null : JsonUtility.ToJson(record);
        }

        [TargetRpc]
        private void TargetReceiveInvite(NetworkConnection conn, string fromName, string guildId)
            => InviteReceived?.Invoke(fromName, guildId);

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((GuildRule)rule);

        #endregion

        private void ClearRoster()
        {
            _guildId.Value = null;
            _rosterJson.Value = null;
        }

        private void OnRosterChanged(string previous, string current, bool asServer)
            => RosterChanged?.Invoke();

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private bool ValidSender(NetworkConnection sender)
            => sender != null && OwnerMatches(sender) && SocialHubService.Instance != null;

        private void ResolveAccountId()
        {
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                _accountId = accountId;
        }
    }
}
