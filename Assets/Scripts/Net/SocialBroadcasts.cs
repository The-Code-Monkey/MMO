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

    /// <summary>Server → client full friend-list snapshot (small and always self-consistent).</summary>
    public struct FriendListBroadcast : IBroadcast
    {
        public string SnapshotJson;
    }

    /// <summary>Server → client friend toast: request received, friend accepted, rule errors.</summary>
    public struct FriendEventBroadcast : IBroadcast
    {
        public int Rule;
    }

    /// <summary>Server → client territory owner change (one flag per message).</summary>
    public struct TerritoryStateBroadcast : IBroadcast
    {
        public string TerritoryId;
        public string OwnerGuildId;
    }
}
