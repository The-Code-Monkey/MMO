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

    /// <summary>
    /// A return anchor recorded by the source zone when a member is transferred to the raid host.
    /// Used to send the member back to their origin when the raid completes.
    /// </summary>
    [Serializable]
    public struct RaidReturnAnchor
    {
        public string AccountId;
        public string OriginHost;
        public ushort OriginPort;
        public string OriginServerId;
        public bool Returned;
    }

    /// <summary>
    /// The authoritative raid object: collection <c>raid_instances</c>, key = <see cref="InstanceId"/>,
    /// shared service account, versioned. Introduced by #51 as a 1:1 companion to a single party and
    /// extended by #54 so one raid groups <em>multiple</em> member parties.
    ///
    /// <para>
    /// <see cref="MemberPartyIds"/> is the source of truth for who is in the raid; each member party
    /// carries its own derived <see cref="PartyRecord.RaidInstanceId"/> pointer and judges membership
    /// by this list. <see cref="Members"/> is a derived flat snapshot of every member party's roster,
    /// refreshed on each write so notifications and the replicated view never have to fan out.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class RaidInstanceRecord
    {
        public string InstanceId;

        /// <summary>The party the raid was created from (the seed); kept for #51 compatibility.</summary>
        public string PartyId;

        /// <summary>Party the raid was created from; its leader seeds raid leadership.</summary>
        public string SeedPartyId;

        /// <summary>Authoritative membership: every party in the raid, seed first.</summary>
        public List<string> MemberPartyIds = new List<string>();

        /// <summary>Account id of the raid leader (party leader of the seed by default).</summary>
        public string LeaderId;

        /// <summary>Derived flat account roster across <see cref="MemberPartyIds"/>; never consulted for membership.</summary>
        public List<RaidInstanceMember> Members = new List<RaidInstanceMember>();

        public string HostZoneId;
        public string HostAddress;
        public ushort HostPort;

        /// <summary>Per-member return anchors recorded at hand-out time (source zone CAS-appends).</summary>
        public List<RaidReturnAnchor> ReturnAnchors = new List<RaidReturnAnchor>();

        /// <summary>Backend revision hash for optimistic concurrency; not raid data.</summary>
        public string StorageVersion;
    }
}
