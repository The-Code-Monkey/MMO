using System;
using System.Collections.Generic;

namespace MMO.Social
{
    [Serializable]
    public struct RaidInstanceMember
    {
        public string AccountId;
        public string PlayerName;
    }

    [Serializable]
    public sealed class RaidInstanceRecord
    {
        public string InstanceId;
        public string PartyId;
        public string HostZoneId;
        public string HostAddress;
        public ushort HostPort;
        public List<RaidInstanceMember> Members = new List<RaidInstanceMember>();
        public string StorageVersion;
    }
}
