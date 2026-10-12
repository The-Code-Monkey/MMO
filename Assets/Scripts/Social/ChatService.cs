using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Server-side chat routing. It validates and stamps identity, then picks the transport: global
    /// and party ride the service bus (their recipients may live on other zones), while zone chat
    /// short-circuits onto FishNet locally because every recipient is on this server by definition.
    ///
    /// <para>
    /// Pure logic against <see cref="ISocialBus"/> and <see cref="ILocalRoster"/>, so the whole
    /// service runs in EditMode against fakes. The bus only ever carries messages relayed from other
    /// zones back in through <see cref="RouteInbound"/>; a client has no path onto it.
    /// </para>
    /// </summary>
    public sealed class ChatService
    {
        private readonly Dictionary<string, double> _nextAllowedAt = new Dictionary<string, double>();

        private ISocialBus _bus;
        private ILocalRoster _roster;

        public ChatService(ISocialBus bus, ILocalRoster roster)
        {
            _bus = bus;
            _roster = roster;
        }

        /// <summary>Swaps the transport when the socket comes up (or in tests).</summary>
        public void SetBus(ISocialBus bus) => _bus = bus;

        /// <summary>Swaps the roster (the hub itself, in production).</summary>
        public void SetRoster(ILocalRoster roster) => _roster = roster;

        /// <summary>Server entry for a player's chat intent. Validates, stamps identity, routes.</summary>
        public ChatRule Send(string accountId, byte channel, string text, double now)
        {
            if (_roster == null)
                return ChatRule.BackendUnavailable;

            string partyId = _roster.PartyOf(accountId);
            double nextAllowedAt = _nextAllowedAt.TryGetValue(accountId ?? string.Empty, out double t) ? t : 0;

            ChatRule rule = ChatRules.CanSend(
                text, now, nextAllowedAt, !string.IsNullOrEmpty(partyId), channel);
            if (rule != ChatRule.Ok)
                return rule;

            _nextAllowedAt[accountId ?? string.Empty] = now + ChatRules.MinSecondsBetweenMessages;

            string channelName = ChatRules.ChannelName(channel);
            if (channelName == null)
                return ChatRule.Ok;

            var envelope = new SocialBusEnvelope
            {
                t = "chat",
                ch = channelName,
                target = channel == ChatRules.ChannelZone ? _roster.LocalZoneId
                       : channel == ChatRules.ChannelParty ? partyId
                       : string.Empty,
                from = accountId,
                name = _roster.NameOf(accountId),   // server-stamped; client text never carries identity
                body = text,
                ts = (long)now,
            };

            if (channel == ChatRules.ChannelZone)
            {
                Deliver(envelope, null);             // all recipients are local — no bus round trip
                return ChatRule.Ok;
            }

            if (_bus != null && _bus.TrySend(envelope))  // global + party ride the service bus
                return ChatRule.Ok;

            return ChatRule.BackendUnavailable;      // degrade: zone chat keeps working
        }

        /// <summary>Bus inbound (socket pump → hub): relay to the right local players.</summary>
        public void RouteInbound(SocialBusEnvelope env)
        {
            if (env == null || env.t != "chat" || string.IsNullOrEmpty(env.from) || _roster == null)
                return;

            Deliver(env, player => ChatRules.Receives(env, _roster.LocalZoneId, player.PartyId));
        }

        private void Deliver(SocialBusEnvelope env, Func<LocalPlayerHandle, bool> match)
        {
            string json = JsonUtility.ToJson(env);
            IReadOnlyList<LocalPlayerHandle> local = _roster.Local;

            for (int i = 0; i < local.Count; i++)
            {
                LocalPlayerHandle player = local[i];
                if (player.DeliverChat == null)
                    continue;

                if (match == null || match(player))
                    player.DeliverChat(json);
            }
        }
    }
}
