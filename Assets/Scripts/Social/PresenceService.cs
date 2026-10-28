using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;

namespace MMO.Social
{
    /// <summary>
    /// Writes local-player presence and the zone registry heartbeat. Both are best-effort: a lost
    /// write is refreshed by the next tick, and a version race lets the other writer win.
    /// </summary>
    internal sealed class PresenceService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<IZoneRegistryRepository> _zones;
        private readonly LocalPlayerRegistry _players;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly Func<string> _zoneId;
        private readonly Func<string> _hostAddress;
        private readonly Func<ushort> _hostPort;
        private readonly Func<string> _zoneTemplateId;
        private readonly Func<int> _maxInstances;

        public PresenceService(
            Func<ISocialRepository> repository,
            Func<IZoneRegistryRepository> zones,
            LocalPlayerRegistry players,
            Func<Task<bool>> ensureConnected,
            Func<string> zoneId,
            Func<string> hostAddress,
            Func<ushort> hostPort,
            Func<string> zoneTemplateId,
            Func<int> maxInstances)
        {
            _repository = repository;
            _zones = zones;
            _players = players;
            _ensureConnected = ensureConnected;
            _zoneId = zoneId;
            _hostAddress = hostAddress;
            _hostPort = hostPort;
            _zoneTemplateId = zoneTemplateId;
            _maxInstances = maxInstances;
        }

        public async Task<bool> AnnounceAsync(LocalPlayerHandle local)
        {
            if (!await _ensureConnected())
                return false;

            await SavePresenceAsync(local);
            return true;
        }

        public async Task HeartbeatAsync()
        {
            foreach (LocalPlayerHandle local in _players.Local)
                await SavePresenceAsync(local);
        }

        /// <summary>
        /// Refreshes this zone's <see cref="ZoneRegistryRecord"/> so it stays a fresh host candidate
        /// and publishes its advertised address, template and capacity (#57). The existing load is
        /// preserved; the version is reused so a racing write from another process wins cleanly.
        /// </summary>
        public async Task HeartbeatZoneAsync()
        {
            IZoneRegistryRepository zones = _zones();
            if (zones == null)
                return;

            try
            {
                List<ZoneRegistryRecord> records = await zones.LoadZonesAsync(CancellationToken.None);
                string zoneId = _zoneId();
                ZoneRegistryRecord existing = FindZone(records, zoneId);

                ZoneRegistryRecord record = ZoneRegistryRecord.Create(
                    zoneId,
                    _hostAddress(),
                    _hostPort(),
                    _zoneTemplateId(),
                    _maxInstances(),
                    DateTimeOffset.UtcNow,
                    existing?.ActiveInstances ?? 0,
                    existing?.StorageVersion);

                await zones.SaveZoneAsync(record, CancellationToken.None);
            }
            catch (StorageVersionConflictException)
            {
                // A racing heartbeat from another write wins; the next tick refreshes ours.
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Social] Failed to write zone registry heartbeat for '{_zoneId()}': {e.Message}");
            }
        }

        public async Task SavePresenceAsync(LocalPlayerHandle local)
        {
            if (local == null)
                return;

            ISocialRepository repository = _repository();
            if (repository == null)
                return;

            try
            {
                PresenceRecord existing = await repository.LoadPresenceAsync(local.AccountId, CancellationToken.None);
                PresenceRecord record = PresenceRecord.CreateOnline(
                    local.AccountId, local.PlayerName, _zoneId(), DateTimeOffset.UtcNow);

                if (existing != null)
                    record.StorageVersion = existing.StorageVersion;

                await repository.SavePresenceAsync(record, CancellationToken.None);
            }
            catch (StorageVersionConflictException)
            {
                // A racing heartbeat from another zone wins; the next tick refreshes ours.
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Social] Failed to write presence for '{local.AccountId}': {e.Message}");
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
