using FishNet.Broadcast;

namespace MMO.Networking
{
    public struct JoinRequestBroadcast : IBroadcast
    {
        public string PlayerName;
    }
}
