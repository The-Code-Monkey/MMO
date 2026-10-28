using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Territory reads and CAS-guarded owner/tax mutations. Guild membership is re-checked inside
    /// the retry loop so a race cannot capture on stale membership.
    /// </summary>
    internal sealed class TerritoryService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly int _casRetries;
        private readonly Action<TerritoryRecord> _changed;

        public TerritoryService(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            int casRetries,
            Action<TerritoryRecord> changed)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _casRetries = casRetries;
            _changed = changed;
        }

        public async Task<TerritoryRecord> LoadAsync(string territoryId)
        {
            if (string.IsNullOrEmpty(territoryId) || !await _ensureConnected())
                return null;

            try
            {
                return await _repository().LoadTerritoryAsync(territoryId, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Territory read failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures an unowned flag, or an owned one inside its vulnerability window, for the
        /// requesting player's guild. Membership and the window are re-checked inside the CAS loop.
        /// </summary>
        public async Task<TerritoryRule> CaptureAsync(
            string accountId, string territoryId, VulnerabilityWindow window, DateTimeOffset now)
        {
            if (!await _ensureConnected())
                return TerritoryRule.BackendUnavailable;

            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(territoryId))
                return TerritoryRule.NoGuild;

            ISocialRepository repository = _repository();
            SocialIndexRecord index;
            GuildRecord guild;
            try
            {
                index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return TerritoryRule.NoGuild;

                guild = await repository.LoadGuildAsync(index.GuildId, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Territory membership read failed: {e.Message}");
                return TerritoryRule.BackendUnavailable;
            }

            if (!GuildRules.IsMember(guild, accountId))
                return TerritoryRule.NoGuild;

            TerritoryRecord saved = null;
            TerritoryRule result = await RetryAsync(async () =>
            {
                TerritoryRecord record = await repository.LoadTerritoryAsync(
                    territoryId, CancellationToken.None);

                TerritoryRule rule = TerritoryRules.CanCapture(
                    window, now, record?.OwnerGuildId, index.GuildId);
                if (rule != TerritoryRule.Ok)
                    return rule;

                record ??= new TerritoryRecord { TerritoryId = territoryId };
                record.OwnerGuildId = index.GuildId;
                record.CapturedAtTicks = now.UtcDateTime.Ticks;
                await repository.SaveTerritoryAsync(record, CancellationToken.None);

                saved = record;
                return TerritoryRule.Ok;
            });

            if (result == TerritoryRule.Ok && saved != null)
                _changed?.Invoke(saved);

            return result;
        }

        /// <summary>
        /// Leader-only: sets a claimed territory's tax rate. The value is clamped to the ceiling and
        /// the write is CAS'd like any other territory mutation; the change is broadcast so the
        /// claim UI repaints.
        /// </summary>
        public async Task<TerritoryRule> SetTaxAsync(string accountId, string territoryId, int taxRateBp)
        {
            if (!await _ensureConnected())
                return TerritoryRule.BackendUnavailable;

            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(territoryId))
                return TerritoryRule.NoGuild;

            ISocialRepository repository = _repository();
            SocialIndexRecord index;
            GuildRecord guild;
            try
            {
                index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return TerritoryRule.NoGuild;

                guild = await repository.LoadGuildAsync(index.GuildId, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Territory tax membership read failed: {e.Message}");
                return TerritoryRule.BackendUnavailable;
            }

            if (guild == null || guild.LeaderId != accountId)
                return TerritoryRule.NotOwner;

            TerritoryRecord saved = null;
            TerritoryRule result = await RetryAsync(async () =>
            {
                TerritoryRecord record = await repository.LoadTerritoryAsync(
                    territoryId, CancellationToken.None);

                if (record == null || record.OwnerGuildId != index.GuildId)
                    return TerritoryRule.NotOwner;

                record.TaxRateBp = TaxationRules.ClampTax(taxRateBp);
                record.SetAtTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                await repository.SaveTerritoryAsync(record, CancellationToken.None);

                saved = record;
                return TerritoryRule.Ok;
            });

            if (result == TerritoryRule.Ok && saved != null)
                _changed?.Invoke(saved);

            return result;
        }

        private async Task<TerritoryRule> RetryAsync(Func<Task<TerritoryRule>> attempt)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await SocialBackoff.DelayAsync(i);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Territory mutation failed: {e.Message}");
                    return TerritoryRule.BackendUnavailable;
                }
            }

            return TerritoryRule.Conflict;
        }
    }
}
