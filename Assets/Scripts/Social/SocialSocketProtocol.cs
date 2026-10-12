using System;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>What a parsed Nakama realtime frame turned out to be.</summary>
    public enum SocketKind
    {
        Other,
        JoinAck,
        SendAck,
        Bus,
        Error,
    }

    /// <summary>The result of parsing one frame. <see cref="SocketKind.Other"/> means ignore it.</summary>
    public readonly struct SocketMessage
    {
        public SocketKind Kind { get; }

        /// <summary>The room channel id from a join ack — never construct this id ourselves.</summary>
        public string ChannelId { get; }

        /// <summary>The parsed bus envelope from a <c>channel_message</c>.</summary>
        public SocialBusEnvelope Envelope { get; }

        /// <summary>The message from an error frame.</summary>
        public string Error { get; }

        public SocketMessage(
            SocketKind kind,
            string channelId = null,
            SocialBusEnvelope envelope = null,
            string error = null)
        {
            Kind = kind;
            ChannelId = channelId;
            Envelope = envelope;
            Error = error;
        }
    }

    /// <summary>
    /// Builders and parsers for the handful of Nakama realtime envelopes a zone server uses.
    /// Static, stateless and network-free, so every message round-trips through EditMode tests —
    /// mirroring <c>NakamaApiGateway</c>'s pure URL/JSON builder pattern.
    /// </summary>
    public static class SocialSocketProtocol
    {
        /// <summary>The room every zone server joins: no history, no presence exposure.</summary>
        public const string SocialRoom = "room.system-social";

        private const int TypeRoom = 1;

        public static string BuildJoin(string cid) =>
            JsonUtility.ToJson(new JoinEnvelope
            {
                cid = cid,
                channel_join = new ChannelJoinBody
                {
                    target = SocialRoom,
                    type = TypeRoom,
                    persistence = false,
                    hidden = true,
                },
            });

        public static string BuildBusMessage(string cid, string channelId, string contentJson) =>
            JsonUtility.ToJson(new MessageSendEnvelope
            {
                cid = cid,
                channel_message_send = new MessageSendBody
                {
                    channel_id = channelId,
                    content = contentJson,
                },
            });

        /// <summary>
        /// Parses one frame. Returns false for anything the zone server deliberately ignores
        /// (presence noise, heartbeats, malformed content).
        /// </summary>
        public static bool TryParse(string frame, out SocketMessage message)
        {
            message = default;
            if (string.IsNullOrEmpty(frame))
                return false;

            InboundEnvelope inbound;
            try
            {
                inbound = JsonUtility.FromJson<InboundEnvelope>(frame);
            }
            catch (Exception)
            {
                return false;
            }

            if (inbound == null)
                return false;

            // Each branch requires the discriminating field to carry content: a frame that merely
            // mentions the key as an empty object (which JsonUtility emits for a null reference)
            // must fall through to the branches it could still be.
            if (inbound.channel != null && !string.IsNullOrEmpty(inbound.channel.id))
            {
                message = new SocketMessage(SocketKind.JoinAck, inbound.channel.id);
                return true;
            }

            if (inbound.channel_message != null && !string.IsNullOrEmpty(inbound.channel_message.content))
            {
                SocialBusEnvelope envelope = ParseEnvelope(inbound.channel_message.content);
                if (envelope == null || string.IsNullOrEmpty(envelope.t))
                    return false;

                message = new SocketMessage(SocketKind.Bus, inbound.channel_message.channel_id, envelope);
                return true;
            }

            if (inbound.channel_message_ack != null && !string.IsNullOrEmpty(inbound.channel_message_ack.channel_id))
            {
                message = new SocketMessage(SocketKind.SendAck, inbound.channel_message_ack.channel_id);
                return true;
            }

            if (inbound.error != null && !string.IsNullOrEmpty(inbound.error.message))
            {
                message = new SocketMessage(SocketKind.Error, error: inbound.error.message);
                return true;
            }

            return false;
        }

        private static SocialBusEnvelope ParseEnvelope(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            try
            {
                return JsonUtility.FromJson<SocialBusEnvelope>(content);
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Serializable]
        private sealed class JoinEnvelope
        {
            public string cid;
            public ChannelJoinBody channel_join;
        }

        [Serializable]
        private sealed class ChannelJoinBody
        {
            public string target;
            public int type;
            public bool persistence;
            public bool hidden;
        }

        [Serializable]
        private sealed class MessageSendEnvelope
        {
            public string cid;
            public MessageSendBody channel_message_send;
        }

        [Serializable]
        private sealed class MessageSendBody
        {
            public string channel_id;
            public string content;
        }

        [Serializable]
        private sealed class InboundEnvelope
        {
            public ChannelPayload channel;
            public ChannelMessageAckPayload channel_message_ack;
            public ChannelMessagePayload channel_message;
            public ErrorPayload error;
        }

        [Serializable]
        private sealed class ChannelPayload
        {
            public string id;
            public string room_name;
        }

        [Serializable]
        private sealed class ChannelMessageAckPayload
        {
            public string channel_id;
        }

        [Serializable]
        private sealed class ChannelMessagePayload
        {
            public string channel_id;
            public string content;
        }

        [Serializable]
        private sealed class ErrorPayload
        {
            public int code;
            public string message;
        }
    }
}
