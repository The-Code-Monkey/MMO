using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using UnityEngine;

namespace MMO.Persistence
{
    public sealed class NakamaRaidInstanceRepository : IRaidInstanceRepository
    {
        public const string RaidInstanceCollection = "raid_instances";
        private readonly NakamaRestClient _rest;

        public NakamaRaidInstanceRepository(NakamaRestClient rest)
        {
            _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        }

        public async Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(RaidInstanceCollection, instanceId, ct);
            return Deserialize(obj);
        }

        public async Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            record.StorageVersion = await _rest.WriteObjectAsync(
                RaidInstanceCollection, record.InstanceId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            => _rest.DeleteObjectAsync(RaidInstanceCollection, instanceId, ct);

        private static RaidInstanceRecord Deserialize(StorageObjectData obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return null;
            RaidInstanceRecord record = JsonUtility.FromJson<RaidInstanceRecord>(obj.Value);
            if (record != null)
                record.StorageVersion = obj.Version;
            return record;
        }
    }
}
