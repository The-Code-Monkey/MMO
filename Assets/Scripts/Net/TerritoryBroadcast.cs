using FishNet.Broadcast;

namespace MMO.Net
{
    /// <summary>Server → client territory owner change (one flag per message).</summary>
    public struct TerritoryStateBroadcast : IBroadcast
    {
        public string TerritoryId;
        public string OwnerGuildId;
    }
}
