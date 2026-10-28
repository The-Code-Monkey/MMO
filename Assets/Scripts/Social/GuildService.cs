using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>Guild creation, invitations, membership and rank changes, with CAS retries.</summary>
    internal sealed class GuildService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly SocialAccountResolver _accounts;
        private readonly Func<Task> _reconcile;
        private readonly Action<string, PendingGuildInvite> _inviteReceived;
        private readonly int _casRetries;

        public GuildService(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            SocialAccountResolver accounts,
            Func<Task> reconcile,
            Action<string, PendingGuildInvite> inviteReceived,
            int casRetries)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _accounts = accounts;
            _reconcile = reconcile;
            _inviteReceived = inviteReceived;
            _casRetries = casRetries;
        }

        /// <summary>Creates a guild with this account as its leader.</summary>
        public async Task<GuildRule> CreateAsync(string accountId, string guildName)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord index = await _accounts.LoadOrCreateIndexAsync(accountId);
                GuildRule rule = GuildRules.CanCreate(accountId, guildName, index);
                if (rule != GuildRule.Ok)
                    return rule;

                // Claim the pointer before the guild exists: a conflict aborts cleanly and the
                // reconcile pass repairs a pointer whose guild write never landed.
                string guildId = Guid.NewGuid().ToString("N");
                index.GuildId = guildId;
                index.PendingGuildInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);

                var guild = new GuildRecord
                {
                    GuildId = guildId,
                    Name = guildName.Trim(),
                    LeaderId = accountId,
                };
                guild.Members.Add(new GuildMemberRecord
                {
                    AccountId = accountId,
                    PlayerName = await _accounts.ResolveDisplayNameAsync(accountId),
                    Rank = (int)GuildRank.Leader,
                    JoinedAtTicks = now.UtcDateTime.Ticks,
                });
                await repository.SaveGuildAsync(guild, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Leader/officer-only: invites the named player to the inviter's guild.</summary>
        public async Task<GuildRule> InviteAsync(string inviterId, string targetName)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            string targetId = await repository.ResolveNameAsync(SocialAccountResolver.Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return GuildRule.TargetNotFound;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PresenceRecord targetPresence = await repository.LoadPresenceAsync(targetId, CancellationToken.None);
            bool targetOnline = targetPresence != null && targetPresence.IsHeartbeatFresh(now);

            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord inviterIndex = await repository.LoadIndexAsync(inviterId, CancellationToken.None);
                SocialIndexRecord targetIndex = await _accounts.LoadOrCreateIndexAsync(targetId);

                GuildRecord guild = null;
                if (inviterIndex != null && !string.IsNullOrEmpty(inviterIndex.GuildId))
                    guild = await repository.LoadGuildAsync(inviterIndex.GuildId, CancellationToken.None);

                GuildRule rule = GuildRules.CanInvite(inviterId, guild, targetIndex, targetOnline);
                if (rule != GuildRule.Ok)
                    return rule;

                targetIndex.PendingGuildInvite = new PendingGuildInvite
                {
                    FromAccountId = inviterId,
                    FromPlayerName = await _accounts.ResolveDisplayNameAsync(inviterId),
                    GuildId = guild.GuildId,
                    ExpiresAtTicks = now.Add(GuildRules.InviteTtl).UtcDateTime.Ticks,
                };
                await repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
            {
                SocialIndexRecord target = await repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (target != null)
                    _inviteReceived?.Invoke(targetId, target.PendingGuildInvite);
            }

            return result;
        }

        /// <summary>Accepts the guild invite this account holds.</summary>
        public async Task<GuildRule> AcceptInviteAsync(string accountId)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                GuildRecord guild = null;
                if (index != null && !string.IsNullOrEmpty(index.PendingGuildInvite.GuildId))
                    guild = await repository.LoadGuildAsync(index.PendingGuildInvite.GuildId, CancellationToken.None);

                GuildRule rule = GuildRules.CanAccept(guild, index, now);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRules.Accept(guild, new GuildMemberRecord
                {
                    AccountId = accountId,
                    PlayerName = await _accounts.ResolveDisplayNameAsync(accountId),
                    Rank = (int)GuildRank.Member,
                    JoinedAtTicks = now.UtcDateTime.Ticks,
                });
                await repository.SaveGuildAsync(guild, CancellationToken.None);

                index.GuildId = guild.GuildId;
                index.PendingGuildInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Leaves the guild this account belongs to, disbanding it when it was the last member.</summary>
        public async Task<GuildRule> LeaveAsync(string accountId)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await repository.LoadGuildAsync(index.GuildId, CancellationToken.None);
                if (guild == null)
                {
                    index.GuildId = null;
                    index.PendingGuildInvite = default;
                    await repository.SaveIndexAsync(index, CancellationToken.None);
                    return GuildRule.Ok;
                }

                GuildRecord remaining = GuildRules.Remove(guild, accountId);
                if (remaining == null)
                    await repository.DeleteGuildAsync(guild.GuildId, CancellationToken.None);
                else
                    await repository.SaveGuildAsync(remaining, CancellationToken.None);

                index.GuildId = null;
                index.PendingGuildInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Leader-only: promotes the target one rank.</summary>
        public async Task<GuildRule> PromoteAsync(string leaderId, string targetId)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord leaderIndex = await repository.LoadIndexAsync(leaderId, CancellationToken.None);
                if (leaderIndex == null || string.IsNullOrEmpty(leaderIndex.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await repository.LoadGuildAsync(leaderIndex.GuildId, CancellationToken.None);
                GuildRule rule = GuildRules.CanPromote(guild, leaderId, targetId);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRules.Promote(guild, targetId);
                await repository.SaveGuildAsync(guild, CancellationToken.None);
                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Leader/officer-only: removes the target from the guild.</summary>
        public async Task<GuildRule> KickAsync(string actorId, string targetId)
        {
            if (!await _ensureConnected())
                return GuildRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            GuildRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord actorIndex = await repository.LoadIndexAsync(actorId, CancellationToken.None);
                if (actorIndex == null || string.IsNullOrEmpty(actorIndex.GuildId))
                    return GuildRule.NoGuild;

                GuildRecord guild = await repository.LoadGuildAsync(actorIndex.GuildId, CancellationToken.None);
                GuildRule rule = GuildRules.CanKick(guild, actorId, targetId);
                if (rule != GuildRule.Ok)
                    return rule;

                GuildRecord remaining = GuildRules.Remove(guild, targetId);
                if (remaining == null)
                    await repository.DeleteGuildAsync(guild.GuildId, CancellationToken.None);
                else
                    await repository.SaveGuildAsync(remaining, CancellationToken.None);

                SocialIndexRecord targetIndex = await repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (targetIndex != null)
                {
                    targetIndex.GuildId = null;
                    targetIndex.PendingGuildInvite = default;
                    await repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                }

                return GuildRule.Ok;
            });

            if (result == GuildRule.Ok)
                await _reconcile();

            return result;
        }

        private async Task<GuildRule> RetryAsync(Func<Task<GuildRule>> attempt)
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
                    Debug.LogError($"[Social] Guild mutation failed: {e.Message}");
                    return GuildRule.BackendUnavailable;
                }
            }

            return GuildRule.Conflict;
        }
    }
}
