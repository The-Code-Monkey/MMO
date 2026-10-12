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
    }
}
