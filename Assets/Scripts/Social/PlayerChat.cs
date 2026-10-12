using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using MMO.Net;
using MMO.Persistence;
using MMO.UI;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's chat surface: intent-only <c>[ServerRpc]</c>s up, authoritative text back through
    /// a registered broadcast. Identity and routing are the server's job — the client only names a
    /// channel and some text.
    ///
    /// <para>
    /// The delivery broadcast is the repo's existing single-connection pattern
    /// (<c>ServerManager.Broadcast(owner, …)</c> + <c>ClientManager.RegisterBroadcast&lt;T&gt;</c>),
    /// the same shape <see cref="MMO.Net.ZoneTransferClient"/> uses.
    /// </para>
    /// </summary>
    public sealed class PlayerChat : NetworkBehaviour
    {
        private string _accountId;

        /// <summary>Raised on the acting client when the server refuses a send.</summary>
        public event Action<ChatRule> RuleFailed;

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _accountId = ResolveAccountId();
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null && !string.IsNullOrEmpty(_accountId))
                hub.AttachChat(_accountId, DeliverChat);
        }

        public override void OnStopNetwork()
        {
            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null && !string.IsNullOrEmpty(_accountId))
                hub.DetachChat(_accountId);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (ClientManager == null)
                return;

            ClientManager.RegisterBroadcast<ChatMessageBroadcast>(OnChatMessage);
            ClientManager.RegisterBroadcast<ChatEventBroadcast>(OnChatEvent);
        }

        public override void OnStopClient()
        {
            if (ClientManager != null)
            {
                ClientManager.UnregisterBroadcast<ChatMessageBroadcast>(OnChatMessage);
                ClientManager.UnregisterBroadcast<ChatEventBroadcast>(OnChatEvent);
            }

            base.OnStopClient();
        }

        /// <summary>Owner-side intent: sends text only — identity and routing are the server's job.</summary>
        public void RequestSend(byte channel, string text)
        {
            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner || string.IsNullOrWhiteSpace(text))
                return;

            RpcSendChat(channel, text);
        }

        [ServerRpc]
        private void RpcSendChat(byte channel, string text, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub?.Chat == null)
                return;

            ChatRule rule = hub.Chat.Send(
                string.IsNullOrEmpty(_accountId) ? ResolveAccountId() : _accountId,
                channel,
                text,
                Time.timeAsDouble);

            if (rule != ChatRule.Ok)
                DeliverEvent(rule);   // toast on the sender only
        }

        [Server]
        private void DeliverChat(string envelopeJson)
            => ServerManager.Broadcast(
                Owner,
                new ChatMessageBroadcast { EnvelopeJson = envelopeJson },
                requireAuthenticated: true);

        [Server]
        private void DeliverEvent(ChatRule rule)
            => ServerManager.Broadcast(
                Owner,
                new ChatEventBroadcast { Rule = (int)rule },
                requireAuthenticated: true);

        private void OnChatMessage(ChatMessageBroadcast msg, Channel channel)
            => ChatFeed.PushChat(msg.EnvelopeJson);

        private void OnChatEvent(ChatEventBroadcast msg, Channel channel)
            => RuleFailed?.Invoke((ChatRule)msg.Rule);

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
