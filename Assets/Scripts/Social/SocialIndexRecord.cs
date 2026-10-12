using System;

namespace MMO.Social
{
    /// <summary>A party invite waiting for its target to accept or decline.</summary>
    [Serializable]
    public struct PendingPartyInvite
    {
        public string FromAccountId;
        public string FromPlayerName;
        public string PartyId;
        public long ExpiresAtTicks;
    }

    /// <summary>A guild invite waiting for its target to accept or decline.</summary>
    [Serializable]
    public struct PendingGuildInvite
    {
        public string FromAccountId;
        public string FromPlayerName;
        public string GuildId;
        public long ExpiresAtTicks;
    }

    /// <summary>
    /// Collection "social_index", key = <see cref="AccountId"/>. The O(1) answer to "is this player
    /// already in a party / guild, and do they have a pending invite?" so an invite never has to
    /// scan every party. It is <em>derived</em> state: the party and guild objects are
    /// authoritative, and <see cref="SocialHubService"/>'s reconcile pass repairs a pointer whose
    /// companion write failed.
    /// </summary>
    [Serializable]
    public sealed class SocialIndexRecord
    {
        public string AccountId;
        public string PartyId;
        public string GuildId;
        public PendingPartyInvite PendingPartyInvite;
        public PendingGuildInvite PendingGuildInvite;

        /// <summary>The backend's revision hash for optimistic concurrency; not index data.</summary>
        public string StorageVersion;
    }
}
