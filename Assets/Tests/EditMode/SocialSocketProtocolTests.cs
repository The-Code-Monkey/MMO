using System;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The realtime envelope builders and parser, exercised with no socket at all: every frame the
    /// zone server can receive is constructed as JSON and fed back through
    /// <see cref="SocialSocketProtocol.TryParse"/> to prove the round trip.
    /// </summary>
    public sealed class SocialSocketProtocolTests
    {
        [Test]
        public void BuildJoin_TargetsTheNamedSocialRoom()
        {
            string json = SocialSocketProtocol.BuildJoin("7");

            Assert.IsTrue(json.Contains(SocialSocketProtocol.SocialRoom),
                "The join must name the room the Lua module registers.");
        }

        [Test]
        public void TryParse_JoinAck_ExposesTheChannelId()
        {
            string frame = JsonUtility.ToJson(new Inbound
            {
                channel = new Channel { id = "chan-1", room_name = SocialSocketProtocol.SocialRoom },
            });

            Assert.IsTrue(SocialSocketProtocol.TryParse(frame, out SocketMessage message));
            Assert.AreEqual(SocketKind.JoinAck, message.Kind);
            Assert.AreEqual("chan-1", message.ChannelId,
                "The channel id must come from the server, never be constructed locally.");
        }

        [Test]
        public void TryParse_ChannelMessage_ReturnsTheEnvelope()
        {
            var envelope = new SocialBusEnvelope
            {
                t = "chat",
                ch = "global",
                from = "a",
                name = "alice",
                body = "hello",
                ts = 42,
            };

            string frame = JsonUtility.ToJson(new Inbound
            {
                channel_message = new ChannelMessage
                {
                    channel_id = "chan-1",
                    content = JsonUtility.ToJson(envelope),
                },
            });

            Assert.IsTrue(SocialSocketProtocol.TryParse(frame, out SocketMessage message));
            Assert.AreEqual(SocketKind.Bus, message.Kind);
            Assert.AreEqual("hello", message.Envelope.body);
            Assert.AreEqual("alice", message.Envelope.name);
            Assert.AreEqual("global", message.Envelope.ch);
        }

        [Test]
        public void TryParse_MessageAck_IsRecognised()
        {
            string frame = JsonUtility.ToJson(new Inbound
            {
                channel_message_ack = new ChannelMessageAck { channel_id = "chan-1" },
            });

            Assert.IsTrue(SocialSocketProtocol.TryParse(frame, out SocketMessage message));
            Assert.AreEqual(SocketKind.SendAck, message.Kind);
        }

        [Test]
        public void TryParse_Error_IsRecognised()
        {
            string frame = JsonUtility.ToJson(new Inbound
            {
                error = new Error { code = 1, message = "nope" },
            });

            Assert.IsTrue(SocialSocketProtocol.TryParse(frame, out SocketMessage message));
            Assert.AreEqual(SocketKind.Error, message.Kind);
            Assert.AreEqual("nope", message.Error);
        }

        [Test]
        public void TryParse_ChannelMessageWithoutAnEnvelope_IsIgnored()
        {
            string frame = JsonUtility.ToJson(new Inbound
            {
                channel_message = new ChannelMessage { channel_id = "chan-1", content = "{}" },
            });

            Assert.IsFalse(SocialSocketProtocol.TryParse(frame, out _),
                "A frame with no envelope type cannot be routed and must be dropped.");
        }

        [Test]
        public void TryParse_UnknownFrame_IsIgnored()
        {
            Assert.IsFalse(SocialSocketProtocol.TryParse("{\"heartbeat\":true}", out _));
        }

        [Serializable]
        private sealed class Inbound
        {
            public Channel channel;
            public ChannelMessage channel_message;
            public ChannelMessageAck channel_message_ack;
            public Error error;
        }

        [Serializable]
        private sealed class Channel
        {
            public string id;
            public string room_name;
        }

        [Serializable]
        private sealed class ChannelMessage
        {
            public string channel_id;
            public string content;
        }

        [Serializable]
        private sealed class ChannelMessageAck
        {
            public string channel_id;
        }

        [Serializable]
        private sealed class Error
        {
            public int code;
            public string message;
        }
    }
}
