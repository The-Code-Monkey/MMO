using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's guild view, the guild half of <see cref="PlayerParty"/>: intent-only RPCs to the
    /// server, an authoritative roster replicated back through SyncVars. Guild membership persists
    /// across sessions (unlike parties), so a reconnecting player's guild reappears on the next
    /// reconcile pass. The guild bank rides the same surface: deposit/withdraw/permission intents
    /// to <see cref="SocialHubService"/>, and one bank SyncVar replicated back.
    /// </summary>
    public sealed class PlayerGuild : NetworkBehaviour
    {
        private readonly SyncVar<string> _guildId = new SyncVar<string>();
        private readonly SyncVar<string> _rosterJson = new SyncVar<string>();
        private readonly SyncVar<string> _bankJson = new SyncVar<string>();

        private string _accountId;

        /// <summary>Raised on the owning client whenever the replicated guild roster changes.</summary>
        public event Action RosterChanged;

        /// <summary>Raised on the owning client whenever the replicated guild bank changes.</summary>
        public event Action BankChanged;

        /// <summary>Raised on the invited client when a guild invite popup should appear.</summary>
        public event Action<string, string> InviteReceived;

        /// <summary>Raised on the acting client when the server refuses a guild intent.</summary>
        public event Action<GuildRule> RuleFailed;

        /// <summary>Raised on the acting client when the server refuses a bank intent.</summary>
        public event Action<GuildBankRule> BankRuleFailed;

        public string GuildId => _guildId.Value;

        public string RosterJson => _rosterJson.Value;

        /// <summary>
        /// The Nakama account id resolved for this player's connection, or null before
        /// <see cref="ResolveAccountId"/> has run. Used by systems that need the account id a
        /// player's guild view was built around (for example, routing auction calls).
        /// </summary>
        public string AccountId => _accountId;

        /// <summary>The authoritative bank parsed from the replicate SyncVar, or null before one arrives.</summary>
        public GuildBankRecord Bank
            => string.IsNullOrEmpty(_bankJson.Value) ? null : JsonUtility.FromJson<GuildBankRecord>(_bankJson.Value);

        /// <summary>The player's bag, cached on the server so bank intents can move items out of it.</summary>
        public PlayerInventory Inventory { get; private set; }

        public override void OnStartNetwork()
        {
            _rosterJson.OnChange += OnRosterChanged;
            _bankJson.OnChange += OnBankChanged;

            if (!IsServer)
                return;

            ResolveAccountId();
            Inventory = GetComponent<PlayerInventory>();
            if (Inventory != null && !string.IsNullOrEmpty(_accountId))
                Inventory.SetHolderId(_accountId);
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.AttachGuildView(_accountId, this);
                hub.GuildChanged += OnGuildChanged;
                hub.GuildInviteReceived += OnGuildInviteReceived;
                hub.BankChanged += OnHubBankChanged;
            }
        }

        public override void OnStopNetwork()
        {
            _rosterJson.OnChange -= OnRosterChanged;
            _bankJson.OnChange -= OnBankChanged;

            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.DetachGuildView(_accountId);
                hub.GuildChanged -= OnGuildChanged;
                hub.GuildInviteReceived -= OnGuildInviteReceived;
                hub.BankChanged -= OnHubBankChanged;
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

        public void RequestDeposit(int tabIndex, int bagX, int bagY)
        {
            if (!OwnsSelf())
                return;

            RpcRequestDeposit(tabIndex, bagX, bagY);
        }

        public void RequestWithdraw(int tabIndex, int gridX, int gridY, int stackCount = -1)
        {
            if (!OwnsSelf())
                return;

            RpcRequestWithdraw(tabIndex, gridX, gridY, stackCount);
        }

        /// <param name="rank">The <see cref="GuildRank"/> whose bank permissions are edited, as its int value.</param>
        public void RequestSetPermission(int rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit)
        {
            if (!OwnsSelf())
                return;

            RpcRequestSetPermission(rank, allowDeposit, allowWithdraw, withdrawLimit, GuildBankRules.UnlimitedValue, GuildBankRules.UnlimitedValue);
        }

        public void RequestSetPermission(int rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit, long withdrawValueLimit, long depositValueLimit)
        {
            if (!OwnsSelf())
                return;

            RpcRequestSetPermission(rank, allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit);
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

        [ServerRpc]
        private void RpcRequestDeposit(int tabIndex, int bagX, int bagY, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveBankAsync(SocialHubService.Instance.DepositAsync(_accountId, tabIndex, bagX, bagY));
        }

        [ServerRpc]
        private void RpcRequestWithdraw(int tabIndex, int gridX, int gridY, int stackCount = -1, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveBankAsync(SocialHubService.Instance.WithdrawAsync(_accountId, tabIndex, gridX, gridY, stackCount));
        }

        [ServerRpc]
        private void RpcRequestSetPermission(
            int rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit, long withdrawValueLimit, long depositValueLimit, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveBankRuleAsync(
                SocialHubService.Instance.SetBankPermissionAsync(_accountId, (GuildRank)rank, allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit));
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

        private async System.Threading.Tasks.Task ResolveBankAsync(System.Threading.Tasks.Task<GuildBankResult> pending)
        {
            GuildBankResult result = await pending;
            if (result.Rule != GuildBankRule.Ok)
                TargetNotifyBank(Owner, (int)result.Rule);
        }

        private async System.Threading.Tasks.Task ResolveBankRuleAsync(System.Threading.Tasks.Task<GuildBankRule> pending)
        {
            GuildBankRule rule = await pending;
            if (rule != GuildBankRule.Ok)
                TargetNotifyBank(Owner, (int)rule);
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

        /// <summary>Called by the hub when a guild bank this zone hosts changed; applies the local view.</summary>
        private void OnHubBankChanged(GuildBankRecord record)
        {
            if (!IsServer || record == null || record.GuildId != _guildId.Value)
                return;

            ApplyBank(record);
        }

        [Server]
        public void ApplyRoster(GuildRecord record)
        {
            _guildId.Value = record?.GuildId;
            _rosterJson.Value = record == null ? null : JsonUtility.ToJson(record);
        }

        /// <summary>
        /// Server: pushed by <see cref="SocialHubService"/> after every winning bank write or
        /// reconcile pass; replicates to every observer of this player.
        /// </summary>
        [Server]
        public void ApplyBank(GuildBankRecord record)
            => _bankJson.Value = record == null ? null : JsonUtility.ToJson(record);

        [TargetRpc]
        private void TargetReceiveInvite(NetworkConnection conn, string fromName, string guildId)
            => InviteReceived?.Invoke(fromName, guildId);

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((GuildRule)rule);

        [TargetRpc]
        private void TargetNotifyBank(NetworkConnection conn, int rule)
            => BankRuleFailed?.Invoke((GuildBankRule)rule);

        #endregion

        private void ClearRoster()
        {
            _guildId.Value = null;
            _rosterJson.Value = null;
            _bankJson.Value = null;
        }

        private void OnRosterChanged(string previous, string current, bool asServer)
            => RosterChanged?.Invoke();

        private void OnBankChanged(string previous, string current, bool asServer)
            => BankChanged?.Invoke();

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
