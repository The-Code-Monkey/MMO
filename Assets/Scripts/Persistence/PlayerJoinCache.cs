using System.Collections.Generic;

namespace MMO.Persistence
{
    /// <summary>
    /// Join-handshake handoff: the record loaded for a client, kept until the spawned player reads
    /// it, plus the storage version every later save must carry.
    /// </summary>
    internal sealed class PlayerJoinCache
    {
        private readonly Dictionary<int, string> _accountByClientId = new();
        private readonly Dictionary<string, PlayerSaveRecord> _spawnRecords = new();
        private readonly Dictionary<string, string> _storageVersions = new();

        public void Record(int clientId, string accountId, PlayerSaveRecord record)
        {
            _accountByClientId[clientId] = accountId;
            _spawnRecords[accountId] = record;

            // The record's version is the optimistic-concurrency token every later save must
            // carry. Captured snapshots are fresh objects, so the service remembers it here.
            _storageVersions[accountId] = record != null ? record.StorageVersion : null;
        }

        public bool TryGetJoin(int clientId, out string accountId, out PlayerSaveRecord record)
        {
            accountId = null;
            record = null;

            if (!_accountByClientId.TryGetValue(clientId, out accountId))
                return false;

            _spawnRecords.TryGetValue(accountId, out record);
            return true;
        }

        public void ForgetClient(int clientId)
        {
            if (_accountByClientId.TryGetValue(clientId, out string accountId))
            {
                _accountByClientId.Remove(clientId);
                _spawnRecords.Remove(accountId);
            }
        }

        public bool TryGetVersion(string accountId, out string version)
            => _storageVersions.TryGetValue(accountId, out version);

        public void SetVersion(string accountId, string version)
            => _storageVersions[accountId] = version;
    }
}
