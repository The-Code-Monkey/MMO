using FishNet.Broadcast;

namespace MMO.Net
{
    public struct JoinRequestBroadcast : IBroadcast
    {
        public string PlayerName;

        /// <summary>
        /// The stable account key (a device GUID the client generates once and persists). The
        /// server loads and saves this player's record under it.
        /// </summary>
        public string AccountId;
    }

    public struct JoinResultBroadcast : IBroadcast
    {
        public bool Approved;
        public string Reason;
    }
}
