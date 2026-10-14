using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// The single-zone fallback for <see cref="IZoneRegistryRepository"/>: it registers the local
    /// zone on every read, so host selection always has exactly one candidate and behaves like the
    /// v1 #51 default ("the allocating zone is the host"). A deployment with a single zone injects
    /// this through <see cref="SocialHubService.Configure"/>; writes are no-ops.
    /// </summary>
    public sealed class SingleZoneRegistryRepository : IZoneRegistryRepository
    {
        private readonly string _serverId;
        private readonly string _host;
        private readonly ushort _port;
        private readonly string _zoneTemplateId;
        private readonly int _maxInstances;

        public SingleZoneRegistryRepository(
            string serverId, string host, ushort port, string zoneTemplateId, int maxInstances)
        {
            _serverId = string.IsNullOrEmpty(serverId) ? "local" : serverId;
            _host = host;
            _port = port;
            _zoneTemplateId = zoneTemplateId;
            _maxInstances = Math.Max(1, maxInstances);
        }

        public Task<List<ZoneRegistryRecord>> LoadZonesAsync(CancellationToken ct = default)
        {
            var record = ZoneRegistryRecord.Create(
                _serverId, _host, _port, _zoneTemplateId, _maxInstances, DateTimeOffset.UtcNow);
            return Task.FromResult(new List<ZoneRegistryRecord> { record });
        }

        public Task SaveZoneAsync(ZoneRegistryRecord record, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteZoneAsync(string serverId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
