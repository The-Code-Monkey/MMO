using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>Best-effort CAS updates of a zone's active raid-instance count.</summary>
    internal sealed class RaidHostCapacityService
    {
        private readonly IZoneRegistryRepository _zones;
        private readonly int _casRetries;

        public RaidHostCapacityService(IZoneRegistryRepository zones, int casRetries)
        {
            _zones = zones;
            _casRetries = casRetries;
        }

        /// <summary>
        /// Best-effort CAS bump of a zone's <see cref="ZoneRegistryRecord.ActiveInstances"/> so the
        /// next selection sees the new load. A missed write only leaves the load signal slightly
        /// stale and never fails the allocation.
        /// </summary>
        public async Task AdjustAsync(string serverId, int delta)
        {
            if (string.IsNullOrEmpty(serverId))
                return;

            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    List<ZoneRegistryRecord> zones = await _zones.LoadZonesAsync();
                    ZoneRegistryRecord zone = FindZone(zones, serverId);
                    if (zone == null)
                        return;

                    zone.ActiveInstances = Math.Max(0, zone.ActiveInstances + delta);
                    await _zones.SaveZoneAsync(zone);
                    return;
                }
                catch (StorageVersionConflictException)
                {
                    await RaidBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Raid] Failed to adjust host instance count for '{serverId}': {e.Message}");
                    return;
                }
            }
        }

        private static ZoneRegistryRecord FindZone(IReadOnlyList<ZoneRegistryRecord> zones, string serverId)
        {
            if (zones == null)
                return null;

            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null && zones[i].ServerId == serverId)
                    return zones[i];
            }

            return null;
        }
    }
}
