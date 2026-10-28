using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>Resolves accounts, display names and new party members from the social store.</summary>
    internal sealed class SocialAccountResolver
    {
        private readonly ISocialRepository _repository;
        private readonly LocalPlayerRegistry _players;
        private readonly Func<string> _zoneId;

        public SocialAccountResolver(ISocialRepository repository, LocalPlayerRegistry players, Func<string> zoneId)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _zoneId = zoneId;
        }

        public async Task<SocialIndexRecord> LoadOrCreateIndexAsync(string accountId)
        {
            SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
            return index ?? new SocialIndexRecord { AccountId = accountId };
        }

        public async Task<string> ResolveDisplayNameAsync(string accountId)
        {
            if (_players.TryGet(accountId, out LocalPlayerHandle local))
                return local.PlayerName;

            PresenceRecord presence = await _repository.LoadPresenceAsync(accountId, CancellationToken.None);
            return string.IsNullOrEmpty(presence?.PlayerName) ? accountId : presence.PlayerName;
        }

        public async Task PublishNameAsync(LocalPlayerHandle local)
        {
            if (local == null)
                return;

            try
            {
                await _repository.SaveNameAsync(Normalize(local.PlayerName), local.AccountId, CancellationToken.None);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Social] Failed to publish the name index for '{local.AccountId}': {e.Message}");
            }
        }

        public PartyMemberRecord NewMember(string accountId, DateTimeOffset now)
            => new PartyMemberRecord
            {
                AccountId = accountId,
                PlayerName = _players.TryGet(accountId, out LocalPlayerHandle local) ? local.PlayerName : accountId,
                ZoneId = _zoneId != null ? _zoneId() : null,
                JoinedAtTicks = now.UtcDateTime.Ticks,
            };

        public static string Normalize(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();
    }
}
