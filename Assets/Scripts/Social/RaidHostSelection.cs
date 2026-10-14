using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>The outcome of a host-selection attempt. <see cref="Ok"/> is the only success.</summary>
    public enum RaidHostChoice
    {
        Ok,

        /// <summary>No healthy, ready zone with spare capacity; the allocation must fail cleanly.</summary>
        NoCandidate,
    }

    /// <summary>
    /// Chooses the zone that hosts a newly allocated raid instance (#57), replacing the v1
    /// "the allocating zone is the host" default from #51. Pure and network-free so it is
    /// EditMode-testable per AGENTS.md, and deterministic so two allocators reading the same
    /// registry tend to spread rather than both pile onto the same zone.
    ///
    /// <para>
    /// A candidate must have a fresh heartbeat (so a crashed zone drops out) and spare headroom
    /// (<see cref="ZoneRegistryRecord.Headroom"/> &gt; 0). Among candidates the lowest
    /// <see cref="ZoneRegistryRecord.ActiveInstances"/> wins, ties broken by ordinal
    /// <see cref="ZoneRegistryRecord.ServerId"/> so the choice is reproducible.
    /// </para>
    /// </summary>
    public static class RaidHostSelection
    {
        /// <summary>
        /// Picks the least-loaded healthy zone, or reports <see cref="RaidHostChoice.NoCandidate"/>
        /// when the registry has none. <paramref name="chosen"/> is null unless the choice is Ok.
        /// </summary>
        public static RaidHostChoice Choose(
            IReadOnlyList<ZoneRegistryRecord> zones, DateTimeOffset now, out ZoneRegistryRecord chosen)
        {
            chosen = null;
            if (zones == null)
                return RaidHostChoice.NoCandidate;

            ZoneRegistryRecord best = null;
            for (int i = 0; i < zones.Count; i++)
            {
                ZoneRegistryRecord zone = zones[i];
                if (zone == null || !zone.IsHeartbeatFresh(now) || zone.Headroom <= 0)
                    continue;

                if (best == null ||
                    zone.ActiveInstances < best.ActiveInstances ||
                    (zone.ActiveInstances == best.ActiveInstances &&
                     string.CompareOrdinal(zone.ServerId, best.ServerId) < 0))
                    best = zone;
            }

            chosen = best;
            return best == null ? RaidHostChoice.NoCandidate : RaidHostChoice.Ok;
        }
    }
}
