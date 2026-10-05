using FishNet.Broadcast;

namespace MMO.Net
{
    public struct JoinRequestBroadcast : IBroadcast
    {
        public string PlayerName;
    }

    public struct JoinResultBroadcast : IBroadcast
    {
        public bool Approved;
        public string Reason;
    }
}
