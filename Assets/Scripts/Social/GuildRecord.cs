using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>
    /// A rank inside a guild. Stored as an int on the record (JsonUtility does not round-trip
    /// enums as readable names, and ints keep the wire shape stable across renames).
    /// </summary>
    public enum GuildRank
    {
        /// <summary>Not a rank: a system deposit that spends no member's quota.</summary>
        None = -1,

        Member = 0,
        Officer = 1,
        Leader = 2,
    }

    /// <summary>One member of a guild as stored in the authoritative <see cref="GuildRecord"/>.</summary>
    [Serializable]
    public struct GuildMemberRecord
    {
        public string AccountId;
        public string PlayerName;

        /// <summary>A <see cref="GuildRank"/> stored as its int value.</summary>
        public int Rank;

        /// <summary>UTC ticks when the member joined. Orders leader succession deterministically.</summary>
        public long JoinedAtTicks;
    }

    /// <summary>
    /// Collection "guilds", key = <see cref="GuildId"/>, owner = the shared service account. Unlike
    /// parties, guilds persist across sessions; membership lives here and the per-account
    /// <see cref="SocialIndexRecord.GuildId"/> is the derived pointer to it. Only
    /// <see cref="SocialHubService"/>'s CAS retry loop writes it.
    /// </summary>
    [Serializable]
    public sealed class GuildRecord
    {
        public const int DefaultMaxMembers = 50;

        public string GuildId;
        public string Name;
        public string LeaderId;
        public List<GuildMemberRecord> Members = new List<GuildMemberRecord>();

        /// <summary>The backend's revision hash for optimistic concurrency; not guild data.</summary>
        public string StorageVersion;
    }
}
