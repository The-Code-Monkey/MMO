using System;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// One player's mailbox: the owner-checked shell over <see cref="MailService"/>.
    ///
    /// <para>
    /// Reading, claiming and sending are all intents. The client sends a <c>[ServerRpc]</c>, the
    /// server resolves the account from the join cache, calls the hub, and answers with the inbox
    /// as JSON through a <c>[TargetRpc]</c> — the same JSON-list pattern the auction house uses.
    /// A refusal (full bag, full inbox, cooldown) comes back as a rule code and nothing moves.
    /// </para>
    /// </summary>
    public sealed class PlayerMail : NetworkBehaviour
    {
        private string _accountId;

        /// <summary>Raised on the owning client whenever the server answers with a fresh inbox.</summary>
        public event Action<MailInbox> InboxReceived;

        /// <summary>Raised on the owning client when a read/claim/send is refused.</summary>
        public event Action<MailRule> RuleFailed;

        /// <summary>Raised on the owning client after a player-to-player send is accepted.</summary>
        public event Action Sent;

        public override void OnStartNetwork()
        {
            if (IsServer)
                _accountId = ResolveAccountId();
        }

        /// <summary>Client intent: load the mailbox.</summary>
        public void RequestInbox()
        {
            if (OwnsSelf())
                RpcRequestInbox();
        }

        /// <summary>Client intent: claim one message's attachments and currency.</summary>
        public void RequestClaim(string mailId)
        {
            if (OwnsSelf() && !string.IsNullOrEmpty(mailId))
                RpcClaim(mailId);
        }

        /// <summary>Client intent: claim every claimable message.</summary>
        public void RequestClaimAll()
        {
            if (OwnsSelf())
                RpcClaimAll();
        }

        /// <summary>Client intent: send a message to another player by display name.</summary>
        public void RequestSend(string toName, string subject, string body)
        {
            if (!OwnsSelf() || string.IsNullOrWhiteSpace(toName))
                return;

            RpcSend(toName, subject ?? string.Empty, body ?? string.Empty);
        }

        [ServerRpc]
        private void RpcRequestInbox(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = DeliverInboxAsync(sender);
        }

        [ServerRpc]
        private void RpcClaim(string mailId, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = ClaimAsync(sender, mailId);
        }

        [ServerRpc]
        private void RpcClaimAll(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = ClaimAllAsync(sender);
        }

        [ServerRpc]
        private void RpcSend(string toName, string subject, string body, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            _ = SendAsync(sender, toName, subject, body);
        }

        private async Task ClaimAsync(NetworkConnection sender, string mailId)
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null)
            {
                TargetRpcRule(sender, (int)MailRule.BackendUnavailable);
                return;
            }

            MailRule rule = await hub.ClaimMailAsync(_accountId, mailId);
            if (rule != MailRule.Ok)
            {
                TargetRpcRule(sender, (int)rule);
                return;
            }

            await DeliverInboxAsync(sender);
        }

        private async Task ClaimAllAsync(NetworkConnection sender)
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null)
            {
                TargetRpcRule(sender, (int)MailRule.BackendUnavailable);
                return;
            }

            await hub.ClaimAllMailAsync(_accountId);
            await DeliverInboxAsync(sender);
        }

        private async Task SendAsync(NetworkConnection sender, string toName, string subject, string body)
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null)
            {
                TargetRpcRule(sender, (int)MailRule.BackendUnavailable);
                return;
            }

            MailRule rule = await hub.SendPlayerMailAsync(
                _accountId, toName, subject, body, Time.timeAsDouble);

            if (rule != MailRule.Ok)
            {
                TargetRpcRule(sender, (int)rule);
                return;
            }

            TargetRpcSent(sender);
            await DeliverInboxAsync(sender);
        }

        private async Task DeliverInboxAsync(NetworkConnection sender)
        {
            SocialHubService hub = SocialHubService.Instance;
            if (hub == null || sender == null)
                return;

            MailInbox inbox = await hub.LoadInboxAsync(_accountId);
            TargetRpcInbox(sender, JsonUtility.ToJson(inbox ?? new MailInbox { AccountId = _accountId }));
        }

        [TargetRpc]
        private void TargetRpcInbox(NetworkConnection conn, string json)
        {
            MailInbox inbox = Decode(json);
            if (inbox != null)
                InboxReceived?.Invoke(inbox);
        }

        [TargetRpc]
        private void TargetRpcRule(NetworkConnection conn, int rule)
            => RuleFailed?.Invoke((MailRule)rule);

        [TargetRpc]
        private void TargetRpcSent(NetworkConnection conn)
            => Sent?.Invoke();

        private static MailInbox Decode(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonUtility.FromJson<MailInbox>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private string ResolveAccountId()
        {
            PlayerGuild guild = GetComponent<PlayerGuild>();
            if (guild != null && !string.IsNullOrEmpty(guild.AccountId))
                return guild.AccountId;

            MMO.Persistence.PlayerPersistenceService service = MMO.Persistence.PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
