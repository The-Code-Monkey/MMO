using System;

namespace MMO.Social
{
    /// <summary>
    /// Collection "zone_registry", key = <see cref="ServerId"/>, owner = the shared service account,
    /// versioned. The mirror of <see cref="PresenceRecord"/> for servers rather than players:
    /// heartbeat-based, so a zone that crashes (and stops refreshing) goes stale instead of looking
    /// healthy forever, and carrying the capacity numbers the raid host selector reads.
    ///
    /// <para>
    /// Introduced by #57. Every candidate host for a raid instance is drawn from here, so the
    /// allocating zone no longer assumes it is the host (the v1 default from #51).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ZoneRegistryRecord
    {
        /// <summary>How long a heartbeat is trusted before the zone drops out of selection.</summary>
        public static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(90);

        public string ServerId;

        /// <summary>Advertised address clients/zones can reach this zone at as a raid host.</summary>
        public string Host;

        public ushort Port;

        /// <summary>Which scene/build this zone serves, recorded on the instances it hosts.</summary>
        public string ZoneTemplateId;

        /// <summary>Raid instances this zone is willing to host.</summary>
        public int MaxInstances;

        /// <summary>Currently hosted raid instances; moved by allocation and teardown.</summary>
        public int ActiveInstances;

        public long HeartbeatAtTicks;

        /// <summary>The backend's revision hash for optimistic concurrency; not zone data.</summary>
        public string StorageVersion;

        /// <summary>Remaining instance slots before this zone is at capacity.</summary>
        public int Headroom => Math.Max(0, MaxInstances - ActiveInstances);

        /// <summary>True while the last heartbeat is within <see cref="HeartbeatTtl"/> of now.</summary>
        public bool IsHeartbeatFresh(DateTimeOffset now)
        {
            if (HeartbeatAtTicks <= 0)
                return false;

            long age = now.UtcDateTime.Ticks - HeartbeatAtTicks;
            return age >= 0 && age <= HeartbeatTtl.Ticks;
        }

        /// <summary>A fresh self-registration for this zone, preserving the existing load if any.</summary>
        public static ZoneRegistryRecord Create(
            string serverId, string host, ushort port, string zoneTemplateId, int maxInstances,
            DateTimeOffset now, int activeInstances = 0, string storageVersion = null)
            => new ZoneRegistryRecord
            {
                ServerId = serverId,
                Host = host,
                Port = port,
                ZoneTemplateId = zoneTemplateId,
                MaxInstances = maxInstances,
                ActiveInstances = activeInstances,
                HeartbeatAtTicks = now.UtcDateTime.Ticks,
                StorageVersion = storageVersion,
            };
    }
}
