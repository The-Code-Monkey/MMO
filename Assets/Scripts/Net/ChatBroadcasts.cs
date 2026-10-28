using FishNet.Broadcast;

namespace MMO.Net
{
    /// <summary>
    /// Server → client chat text. The envelope is carried as JSON so the payload has to match
    /// <c>SocialBusEnvelope</c> exactly and one broadcast type serves every channel.
    /// </summary>
    public struct ChatMessageBroadcast : IBroadcast
    {
        public string EnvelopeJson;
    }

    /// <summary>Server → client chat toast: rate-limited, backend unavailable, and so on.</summary>
    public struct ChatEventBroadcast : IBroadcast
    {
        public int Rule;
    }
}
