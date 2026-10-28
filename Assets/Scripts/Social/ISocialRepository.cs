using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// The social storage boundary between <see cref="SocialHubService"/> (which owns the rules)
    /// and whatever backend holds the records. The interface is deliberately version-on-record, the
    /// same shape <c>IPlayerRepository</c> uses: each load stamps <c>StorageVersion</c>, each save
    /// if-matches it, and a losing race throws
    /// <see cref="Persistence.StorageVersionConflictException"/> so the service re-reads and retries.
    /// Tests inject an in-memory fake with no backend at all.
    /// </summary>
    public interface ISocialRepository
    {
        Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default);

        /// <summary>Creates when <c>record.StorageVersion</c> is empty, otherwise if-matches it.</summary>
        Task SavePartyAsync(PartyRecord record, CancellationToken ct = default);

        Task DeletePartyAsync(string partyId, CancellationToken ct = default);

        Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default);

        /// <summary>Batch read for the reconcile pass (Nakama allows 100 object ids per call).</summary>
        Task<List<SocialIndexRecord>> LoadIndexesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default);

        Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default);

        Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default);

        Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default);

        /// <summary>Resolves a lowercased player name to an account id, or null when unknown.</summary>
        Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default);

        /// <summary>Publishes a lowercased player name → account id mapping for cross-zone invites.</summary>
        Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default);

        Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default);

        Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default);

        Task DeleteGuildAsync(string guildId, CancellationToken ct = default);

        Task<FriendListRecord> LoadFriendsAsync(string accountId, CancellationToken ct = default);

        /// <summary>Creates when <c>record.StorageVersion</c> is empty, otherwise if-matches it.</summary>
        Task SaveFriendsAsync(FriendListRecord record, CancellationToken ct = default);

        /// <summary>Null when the guild has never deposited; <c>StorageVersion</c> carries the if-match token.</summary>
        Task<GuildBankRecord> LoadGuildBankAsync(string guildId, CancellationToken ct = default);

        /// <summary>Creates when <c>record.StorageVersion</c> is empty, otherwise if-matches it.</summary>
        Task SaveGuildBankAsync(GuildBankRecord record, CancellationToken ct = default);

        Task<GuildBankLedgerRecord> LoadGuildBankLedgerAsync(string guildId, CancellationToken ct = default);
        Task SaveGuildBankLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default);

        /// <summary>Null when the territory has never been captured; <c>StorageVersion</c> is the if-match token.</summary>
        Task<TerritoryRecord> LoadTerritoryAsync(string territoryId, CancellationToken ct = default);

        /// <summary>Creates when <c>record.StorageVersion</c> is empty, otherwise if-matches it.</summary>
        Task SaveTerritoryAsync(TerritoryRecord record, CancellationToken ct = default);

        /// <summary>Batch presence read for the friend-list online flags (100 ids per call).</summary>
        Task<List<PresenceRecord>> LoadPresencesAsync(
            IReadOnlyList<string> accountIds, CancellationToken ct = default);

        /// <summary>
        /// Best-effort cross-zone push fired after a winning CAS so peer zones can react before the
        /// next reconcile tick. The body is ids-only; a receiving zone re-reads authoritative state
        /// through the same rules as the poll path. A failure must never fail the caller's write —
        /// the reconcile pass is the fallback — so callers swallow it.
        /// </summary>
        Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default);
    }
}
