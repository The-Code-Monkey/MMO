using System;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>
    /// Simple player wallet for gold currency. Stored in Nakama storage under "player_wallet" collection.
    /// </summary>
    [Serializable]
    public sealed class PlayerWalletRecord
    {
        public const string Collection = "player_wallet";

        public string AccountId;
        public long Gold;
        public string StorageVersion;
    }

    /// <summary>
    /// Repository for player wallet operations.
    /// </summary>
    public interface IWalletRepository
    {
        Task<PlayerWalletRecord> LoadWalletAsync(string accountId, CancellationToken ct = default);
        Task SaveWalletAsync(PlayerWalletRecord record, CancellationToken ct = default);
    }

    /// <summary>
    /// Nakama-backed wallet repository.
    /// </summary>
    public sealed class NakamaWalletRepository : IWalletRepository
    {
        private readonly NakamaRestClient _rest;

        public NakamaWalletRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        public async Task<PlayerWalletRecord> LoadWalletAsync(string accountId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(PlayerWalletRecord.Collection, "wallet", ct);
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return new PlayerWalletRecord { AccountId = accountId, Gold = 0 };

            PlayerWalletRecord record = JsonUtility.FromJson<PlayerWalletRecord>(obj.Value);
            if (record != null)
            {
                record.AccountId = accountId;
                record.StorageVersion = obj.Version;
            }
            return record;
        }

        public async Task SaveWalletAsync(PlayerWalletRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                PlayerWalletRecord.Collection, "wallet", JsonUtility.ToJson(record), record.StorageVersion, ct);
        }
    }
}