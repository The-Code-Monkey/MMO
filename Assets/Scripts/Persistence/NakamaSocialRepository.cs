using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The Nakama-backed <see cref="ISocialRepository"/>: parties, the derived social index, guilds,
    /// presence and the name lookup, all stored under the shared service account the game server
    /// authenticates as. This is pure translation between the records and the
    /// <see cref="NakamaRestClient"/> primitives — every rule lives in <c>PartyRules</c>/<c>GuildRules</c>
    /// and every consistency decision lives in <see cref="SocialHubService"/>.
    /// </summary>
    public sealed class NakamaSocialRepository : ISocialRepository
    {
        public const string PartyCollection = "parties";
        public const string IndexCollection = "social_index";
        public const string PresenceCollection = "social_presence";
        public const string NameIndexCollection = "name_index";
        public const string GuildCollection = "guilds";
        public const string FriendsCollection = "friends";
        public const string GuildBankCollection = "guild_banks";
        public const string GuildBankLedgerCollection = "guild_bank_ledgers";

        private readonly NakamaRestClient _rest;

        public NakamaSocialRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        /// <summary>
        /// The shared HTTP/auth client underneath. The hub reads it back in
        /// <see cref="SocialHubService.Configure"/> so it can drive the connection lifecycle itself.
        /// </summary>
        public NakamaRestClient Rest => _rest;

        public async Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(PartyCollection, partyId, ct);
            return Deserialize<PartyRecord>(obj);
        }

        public async Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                PartyCollection, record.PartyId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public Task DeletePartyAsync(string partyId, CancellationToken ct = default)
            => _rest.DeleteObjectAsync(PartyCollection, partyId, ct);

        public async Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(IndexCollection, accountId, ct);
            SocialIndexRecord record = Deserialize<SocialIndexRecord>(obj);
            if (record != null && string.IsNullOrEmpty(record.AccountId))
                record.AccountId = accountId;

            return record;
        }

        public async Task<List<SocialIndexRecord>> LoadIndexesAsync(
            IReadOnlyList<string> accountIds, CancellationToken ct = default)
        {
            List<StorageObjectData> objects = await _rest.ReadObjectsAsync(IndexCollection, accountIds, ct);

            var result = new List<SocialIndexRecord>(objects.Count);
            foreach (StorageObjectData obj in objects)
            {
                SocialIndexRecord record = Deserialize<SocialIndexRecord>(obj);
                if (record == null)
                    continue;

                if (string.IsNullOrEmpty(record.AccountId))
                    record.AccountId = obj.Key;

                result.Add(record);
            }

            return result;
        }

        public async Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                IndexCollection, record.AccountId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(PresenceCollection, accountId, ct);
            return Deserialize<PresenceRecord>(obj);
        }

        public async Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                PresenceCollection, record.AccountId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(lowercasedName))
                return null;

            StorageObjectData obj = await _rest.ReadObjectAsync(NameIndexCollection, lowercasedName, ct);
            NameIndexRecord index = Deserialize<NameIndexRecord>(obj);
            return string.IsNullOrEmpty(index?.AccountId) ? null : index.AccountId;
        }

        public async Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(lowercasedName) || string.IsNullOrEmpty(accountId))
                return;

            // Upsert: reuse the existing revision when there is one so a renamed player overwrites
            // their old mapping instead of colliding with it.
            StorageObjectData existing = await _rest.ReadObjectAsync(NameIndexCollection, lowercasedName, ct);
            var record = new NameIndexRecord { AccountId = accountId };
            await _rest.WriteObjectAsync(
                NameIndexCollection, lowercasedName, JsonUtility.ToJson(record), existing?.Version, ct);
        }

        public async Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(GuildCollection, guildId, ct);
            return Deserialize<GuildRecord>(obj);
        }

        public async Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                GuildCollection, record.GuildId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public Task DeleteGuildAsync(string guildId, CancellationToken ct = default)
            => _rest.DeleteObjectAsync(GuildCollection, guildId, ct);

        public async Task<FriendListRecord> LoadFriendsAsync(string accountId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(FriendsCollection, accountId, ct);
            FriendListRecord record = Deserialize<FriendListRecord>(obj);
            if (record != null && string.IsNullOrEmpty(record.AccountId))
                record.AccountId = accountId;

            return record;
        }

        public async Task SaveFriendsAsync(FriendListRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                FriendsCollection, record.AccountId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<List<PresenceRecord>> LoadPresencesAsync(
            IReadOnlyList<string> accountIds, CancellationToken ct = default)
        {
            List<StorageObjectData> objects = await _rest.ReadObjectsAsync(PresenceCollection, accountIds, ct);

            var result = new List<PresenceRecord>(objects.Count);
            foreach (StorageObjectData obj in objects)
            {
                PresenceRecord record = Deserialize<PresenceRecord>(obj);
                if (record == null)
                    continue;

                if (string.IsNullOrEmpty(record.AccountId))
                    record.AccountId = obj.Key;

                result.Add(record);
            }

            return result;
        }

        public async Task<GuildBankRecord> LoadGuildBankAsync(string guildId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(GuildBankCollection, guildId, ct);
            return Deserialize<GuildBankRecord>(obj);
        }

        public async Task SaveGuildBankAsync(GuildBankRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                GuildBankCollection, record.GuildId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task<GuildBankLedgerRecord> LoadGuildBankLedgerAsync(string guildId, CancellationToken ct = default)
        {
            StorageObjectData obj = await _rest.ReadObjectAsync(GuildBankLedgerCollection, guildId, ct);
            return Deserialize<GuildBankLedgerRecord>(obj);
        }

        public async Task SaveGuildBankLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                GuildBankLedgerCollection, record.GuildId, JsonUtility.ToJson(record), record.StorageVersion, ct);
        }

        public async Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default)
        {
            if (notification == null)
                return;

            // The Lua social_notify module relays a flat bus envelope to every zone's socket; only
            // ids ride the wire, and the stream is best-effort (a dropped frame is repaired by the
            // 2 s reconcile pass), so a module that is not loaded yet is not an error here.
            SocialBusEnvelope envelope = SocialNotification.ToEnvelope(notification);
            await _rest.CallRpcAsync<NotifyResponse>("social_notify", JsonUtility.ToJson(envelope), ct);
        }

        private static T Deserialize<T>(StorageObjectData obj) where T : class
        {
            if (obj == null || string.IsNullOrEmpty(obj.Value))
                return null;

            T record = JsonUtility.FromJson<T>(obj.Value);
            if (record is PartyRecord party)
                party.StorageVersion = obj.Version;
            else if (record is GuildRecord guild)
                guild.StorageVersion = obj.Version;
            else if (record is SocialIndexRecord index)
                index.StorageVersion = obj.Version;
            else if (record is PresenceRecord presence)
                presence.StorageVersion = obj.Version;
            else if (record is FriendListRecord friends)
                friends.StorageVersion = obj.Version;
            else if (record is GuildBankRecord bank)
                bank.StorageVersion = obj.Version;
            else if (record is GuildBankLedgerRecord bankLedger)
                bankLedger.StorageVersion = obj.Version;

            return record;
        }

        [Serializable]
        private class NameIndexRecord
        {
            public string AccountId;
        }
    }
}
