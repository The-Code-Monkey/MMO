using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The player-record repository: collection "players", key = the player's AccountId, every
    /// object written under the game server's shared service account (see
    /// <see cref="NakamaRestClient"/>). This is only translation — the handshake, the HTTP calls
    /// and the optimistic version handling all live in the shared client, so the same connection
    /// also serves the social collections.
    ///
    /// <para>
    /// Optimistic concurrency: a save carries the version read with the record (or "*" for a first
    /// write), and the write fails if another server instance has already moved the object. The
    /// caller (see <see cref="PlayerPersistenceService"/>) keeps the player dirty and retries on that
    /// failure rather than ever overwriting blindly — that is the property that makes N game servers
    /// share one source of truth.
    /// </para>
    /// </summary>
    public sealed class NakamaPlayerRepository : IPlayerRepository, INakamaConnected
    {
        /// <summary>Storage collection every player record lives under.</summary>
        public const string StorageCollection = "players";

        private readonly NakamaRestClient _rest;

        public NakamaPlayerRepository(BackendSettings settings)
            : this(new NakamaRestClient(settings))
        {
        }

        /// <summary>Shares an already-connected client (e.g. the social repository's).</summary>
        public NakamaPlayerRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        /// <summary>The shared gateway connection; the social repository reuses it.</summary>
        public NakamaRestClient Rest => _rest;

        public bool IsConnected => _rest.IsConnected;

        public Task ConnectAsync(CancellationToken ct) => _rest.ConnectAsync(ct);

        public Task DisconnectAsync() => _rest.DisconnectAsync();

        public async Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(StorageCollection, userId, ct);
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return null;

            PlayerSaveRecord record = JsonUtility.FromJson<PlayerSaveRecord>(obj.Value);
            if (record == null)
                return null;

            record.UserId = userId;
            record.StorageVersion = obj.Version;
            return record;
        }

        public async Task SaveAsync(PlayerSaveRecord record, CancellationToken ct)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                StorageCollection, record.UserId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }
    }
}
