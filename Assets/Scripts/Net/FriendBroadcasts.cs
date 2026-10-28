using FishNet.Broadcast;

namespace MMO.Net
{
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
}
