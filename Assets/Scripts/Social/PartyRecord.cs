using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>One member of a party as stored in the authoritative <see cref="PartyRecord"/>.</summary>
    [Serializable]
    public struct PartyMemberRecord
    {
        public string AccountId;
        public string PlayerName;

        /// <summary>The zone server the member was last seen on; refreshed by presence heartbeats.</summary>
        public string ZoneId;

        /// <summary>UTC ticks when the member joined. Orders leader succession deterministically.</summary>
        public long JoinedAtTicks;
    }

    /// <summary>
    /// The authoritative party object: collection "parties", key = <see cref="PartyId"/>, owner =
    /// the shared service account. Only <see cref="SocialHubService"/>'s CAS retry loop writes it.
    ///
    /// <para>
    /// <see cref="StorageVersion"/> is the backend's revision hash, carried on the record so the IO
    /// layer can <em>if-match</em> the version it read; a losing write surfaces as
    /// <see cref="Persistence.StorageVersionConflictException"/> and the caller retries.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PartyRecord
    {
        public string PartyId;
        public string LeaderId;
        public List<PartyMemberRecord> Members = new List<PartyMemberRecord>();

        /// <summary>
        /// Reserved for dynamic raid instances (the prerequisite this issue exists for): set when an
        /// instance is allocated, then replicated to every member's zone.
        /// </summary>
        public string RaidInstanceId;

        /// <summary>The backend's revision hash for optimistic concurrency; not party data.</summary>
        public string StorageVersion;
    }
}
