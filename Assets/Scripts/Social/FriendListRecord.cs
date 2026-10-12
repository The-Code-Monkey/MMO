using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>The relationship a player holds toward one account.</summary>
    public static class FriendState
    {
        /// <summary>I asked them; waiting on their side.</summary>
        public const byte PendingOut = 0;

        /// <summary>They asked me; waiting on my accept.</summary>
        public const byte PendingIn = 1;

        /// <summary>Mutual, accepted friends.</summary>
        public const byte Accepted = 2;
    }

    /// <summary>One entry in a player's friend list.</summary>
    [Serializable]
    public struct FriendEntry
    {
        public string AccountId;
        public string PlayerName;
        public byte State;
        public long UpdatedAtTicks;
    }

    /// <summary>
    /// Collection "friends", key = <see cref="AccountId"/>, owner = the shared service account.
    /// State is stored twice, once per side: A holds PendingOut toward B while B holds PendingIn
    /// toward A, so every mutation stays a single-object CAS with no cross-object transaction.
    /// </summary>
    [Serializable]
    public sealed class FriendListRecord
    {
        public string AccountId;
        public List<FriendEntry> Entries = new List<FriendEntry>();

        /// <summary>The backend's revision hash for optimistic concurrency; not friend data.</summary>
        public string StorageVersion;
    }

    /// <summary>One row a client renders: the entry plus an online flag derived from presence.</summary>
    [Serializable]
    public struct FriendView
    {
        public string AccountId;
        public string PlayerName;
        public byte State;

        /// <summary>Presence heartbeat within the 90 s window (see <see cref="PresenceRecord"/>).</summary>
        public bool Online;
    }

    /// <summary>A full, always self-consistent friend list push.</summary>
    [Serializable]
    public sealed class FriendListSnapshot
    {
        public List<FriendView> Friends = new List<FriendView>();
    }
}
