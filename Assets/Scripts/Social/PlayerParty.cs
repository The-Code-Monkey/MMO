using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's party view. Clients only submit intent; the server validates it, talks to Nakama
    /// through <see cref="SocialHubService"/>, and the authoritative roster flows back through a
    /// SyncVar so every client holding this player renders the same party frame.
    ///
    /// <para>
    /// The roster travels as one JSON string rather than a raw list: FishNet needs a single SyncVar,
    /// the payload has to match the storage record exactly, and a string diffs cheaply. The
    /// <c>SyncVar&lt;T&gt;</c> object form is used deliberately — the <c>[SyncVar]</c> attribute is
    /// obsolete and silently replicates nothing in FishNet 4.x.
    /// </para>
    /// </summary>
    public sealed class PlayerParty : NetworkBehaviour
    {
        private readonly SyncVar<string> _partyId = new SyncVar<string>();
        private readonly SyncVar<string> _rosterJson = new SyncVar<string>();

        private string _accountId;
        private string _playerName;

        /// <summary>Raised on the owning client whenever the replicated roster changes.</summary>
        public event Action RosterChanged;

        /// <summary>Raised on the invited client when a party invite popup should appear.</summary>
        public event Action<string, string> InviteReceived;

        /// <summary>Raised on the acting client when the server refuses an intent.</summary>
        public event Action<PartyRule> RuleFailed;

        public string PartyId => _partyId.Value;

        public string RosterJson => _rosterJson.Value;

        /// <summary>The server-side account id this player's party lives under; null on clients.</summary>
        public string AccountId => _accountId;

        public override void OnStartNetwork()
        {
            _rosterJson.OnChange += OnRosterChanged;

            if (!IsServer)
                return;

            ResolveIdentity();
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null)
            {
                hub.RegisterLocal(_accountId, _playerName);
                hub.PartyChanged += OnPartyChanged;
                hub.PartyInviteReceived += OnPartyInviteReceived;
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
                hub.PartyChanged -= OnPartyChanged;
                hub.PartyInviteReceived -= OnPartyInviteReceived;
                hub.UnregisterLocal(_accountId);
            }
        }

        #region Client intent (ownership checked locally and again on the server)

        public void RequestInvite(string targetName)
        {
            if (!OwnsSelf() || string.IsNullOrWhiteSpace(targetName))
                return;

            RpcInvite(targetName);
        }

        public void RequestAccept()
        {
            if (OwnsSelf())
                RpcAccept();
        }

        public void RequestDecline()
        {
            if (OwnsSelf())
                RpcDecline();
        }

        public void RequestLeave()
        {
            if (OwnsSelf())
                RpcLeave();
        }

        public void RequestKick(string targetAccountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(targetAccountId))
                return;

            RpcKick(targetAccountId);
        }

        [ServerRpc]
        private void RpcInvite(string targetName, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = InviteAsync(targetName);
        }

        [ServerRpc]
        private void RpcAccept(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.AcceptAsync(_accountId));
        }

        [ServerRpc]
        private void RpcDecline(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.DeclineAsync(_accountId));
        }

        [ServerRpc]
        private void RpcLeave(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.LeaveAsync(_accountId));
        }

        [ServerRpc]
        private void RpcKick(string targetAccountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveRuleAsync(SocialHubService.Instance.KickAsync(_accountId, targetAccountId));
        }

        private async System.Threading.Tasks.Task InviteAsync(string targetName)
        {
            PartyRule rule = await SocialHubService.Instance.InviteAsync(_accountId, targetName);
            if (rule != PartyRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        private async System.Threading.Tasks.Task ResolveRuleAsync(System.Threading.Tasks.Task<PartyRule> pending)
        {
            PartyRule rule = await pending;
            if (rule != PartyRule.Ok)
                TargetNotify(Owner, (int)rule);
        }

        #endregion

        #region Backend → client delivery

        /// <summary>Called by the hub for one invited account; forwards to a single client popup.</summary>
        private void OnPartyInviteReceived(string accountId, PendingPartyInvite invite)
        {
            if (!IsServer || accountId != _accountId)
                return;

            TargetReceiveInvite(Owner, invite.FromPlayerName, invite.PartyId);
        }

        /// <summary>Called by the hub when any hosted party changed; applies the local roster.</summary>
        private void OnPartyChanged(PartyRecord record)
        {
            if (!IsServer)
                return;

            if (record == null)
            {
                ClearRoster();
                return;
            }

            if (PartyRules.IsMember(record, _accountId))
                ApplyRoster(record);
            else if (_partyId.Value == record.PartyId)
                ClearRoster();
        }

        /// <summary>Server: pushes the authoritative roster into the replicated SyncVars.</summary>
        [Server]
        public void ApplyRoster(PartyRecord record)
        {
            _partyId.Value = record?.PartyId;
            _rosterJson.Value = record == null ? null : JsonUtility.ToJson(record);
        }

        [TargetRpc]
        private void TargetReceiveInvite(NetworkConnection conn, string fromName, string partyId)
            => InviteReceived?.Invoke(fromName, partyId);

        [TargetRpc]
        private void TargetNotify(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((PartyRule)rule);

        #endregion

        private void ClearRoster()
        {
            _partyId.Value = null;
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

        private void ResolveIdentity()
        {
            _accountId = ResolveAccountId();
            _playerName = _accountId;
        }

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
