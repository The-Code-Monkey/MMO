using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The Nakama-backed <see cref="IZoneRegistryRepository"/>: the zone registry lives in the
    /// <c>zone_registry</c> collection keyed by server id, under the shared service account the game
    /// servers authenticate as. Pure translation between the record and the
    /// <see cref="NakamaRestClient"/> storage primitives — every selection decision lives in
    /// <see cref="RaidHostSelection"/>.
    /// </summary>
    public sealed class NakamaZoneRegistryRepository : IZoneRegistryRepository
    {
        public const string ZoneRegistryCollection = "zone_registry";

        private readonly NakamaRestClient _rest;

        public NakamaZoneRegistryRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        public async Task<List<ZoneRegistryRecord>> LoadZonesAsync(CancellationToken ct = default)
        {
            List<StorageObjectData> objects = await _rest.ListObjectsAsync(ZoneRegistryCollection, ct);

            var result = new List<ZoneRegistryRecord>(objects.Count);
            foreach (StorageObjectData obj in objects)
            {
                ZoneRegistryRecord record = Deserialize(obj);
                if (record == null)
                    continue;

                if (string.IsNullOrEmpty(record.ServerId))
                    record.ServerId = obj.Key;

                result.Add(record);
            }

            return result;
        }

        public async Task SaveZoneAsync(ZoneRegistryRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                ZoneRegistryCollection, record.ServerId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        private static ZoneRegistryRecord Deserialize(StorageObjectData obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return null;

            ZoneRegistryRecord record = JsonUtility.FromJson<ZoneRegistryRecord>(obj.Value);
            if (record != null)
                record.StorageVersion = obj.Version;

            return record;
        }
    }
}
