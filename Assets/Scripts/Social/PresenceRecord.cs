using System;

namespace MMO.Social
{
    /// <summary>
    /// Collection "social_presence", key = <see cref="AccountId"/>. Heartbeat-based rather than
    /// write-once: a zone server refreshes every live player's heartbeat, so a zone that crashes
    /// (and therefore stops heartbeating) lets its players go stale after
    /// <see cref="HeartbeatTtl"/> instead of appearing online forever. The party itself still lives
    /// in Nakama, so a player who rejoins any zone keeps their party.
    /// </summary>
    [Serializable]
    public sealed class PresenceRecord
    {
        /// <summary>How long a heartbeat is trusted before a player counts as offline.</summary>
        public static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(90);

        public string AccountId;
        public string PlayerName;
        public string ZoneId;
        public bool Online;
        public long HeartbeatAtTicks;

        /// <summary>The backend's revision hash for optimistic concurrency; not presence data.</summary>
        public string StorageVersion;

        /// <summary>True while the player is flagged online and their last heartbeat is fresh.</summary>
        public bool IsHeartbeatFresh(DateTimeOffset now)
        {
            if (!Online || HeartbeatAtTicks <= 0)
                return false;

            long age = now.UtcDateTime.Ticks - HeartbeatAtTicks;
            return age >= 0 && age <= HeartbeatTtl.Ticks;
        }

        public static PresenceRecord Online(string accountId, string playerName, string zoneId, DateTimeOffset now)
            => new PresenceRecord
            {
                AccountId = accountId,
                PlayerName = playerName,
                ZoneId = zoneId,
                Online = true,
                HeartbeatAtTicks = now.UtcDateTime.Ticks,
            };
    }
}
