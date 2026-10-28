using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>Party invites, membership and leadership, with CAS retries.</summary>
    internal sealed class PartyService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly SocialAccountResolver _accounts;
        private readonly Func<PartyRecord, Task> _teardownRaid;
        private readonly Func<Task> _reconcile;
        private readonly Action<string, PendingPartyInvite> _inviteReceived;
        private readonly int _casRetries;

        public PartyService(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            SocialAccountResolver accounts,
            Func<PartyRecord, Task> teardownRaid,
            Func<Task> reconcile,
            Action<string, PendingPartyInvite> inviteReceived,
            int casRetries)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _accounts = accounts;
            _teardownRaid = teardownRaid;
            _reconcile = reconcile;
            _inviteReceived = inviteReceived;
            _casRetries = casRetries;
        }

        /// <summary>Invites the named player to the inviter's party, creating one if needed.</summary>
        public async Task<PartyRule> InviteAsync(string inviterId, string targetName)
        {
            if (!await _ensureConnected())
                return PartyRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            string targetId = await repository.ResolveNameAsync(SocialAccountResolver.Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return PartyRule.TargetNotFound;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            PresenceRecord targetPresence = await repository.LoadPresenceAsync(targetId, CancellationToken.None);
            bool targetOnline = targetPresence != null && targetPresence.IsHeartbeatFresh(now);

            PartyRule inviteResult = await RetryAsync(async () =>
            {
                SocialIndexRecord inviterIndex = await _accounts.LoadOrCreateIndexAsync(inviterId);
                SocialIndexRecord targetIndex = await _accounts.LoadOrCreateIndexAsync(targetId);

                PartyRecord inviterParty = null;
                if (!string.IsNullOrEmpty(inviterIndex.PartyId))
                    inviterParty = await repository.LoadPartyAsync(inviterIndex.PartyId, CancellationToken.None);

                PartyRule rule = PartyRules.CanInvite(inviterId, inviterParty, targetIndex, targetOnline);
                if (rule != PartyRule.Ok)
                    return rule;

                if (inviterParty == null)
                {
                    // Claim the pointer before the party exists: a conflict on this index save
                    // aborts the attempt with no party written, so a retry cannot orphan one. A
                    // crash between the two writes leaves the pointer dangling, which the reconcile
                    // pass repairs — never a leaked party.
                    string partyId = Guid.NewGuid().ToString("N");
                    inviterIndex.PartyId = partyId;
                    await repository.SaveIndexAsync(inviterIndex, CancellationToken.None);

                    inviterParty = new PartyRecord { PartyId = partyId, LeaderId = inviterId };
                    inviterParty.Members.Add(_accounts.NewMember(inviterId, now));
                    await repository.SavePartyAsync(inviterParty, CancellationToken.None);
                }

                string fromName = await _accounts.ResolveDisplayNameAsync(inviterId);
                targetIndex.PendingPartyInvite = new PendingPartyInvite
                {
                    FromAccountId = inviterId,
                    FromPlayerName = fromName,
                    PartyId = inviterParty.PartyId,
                    ExpiresAtTicks = now.Add(PartyRules.InviteTtl).UtcDateTime.Ticks,
                };
                await repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (inviteResult == PartyRule.Ok)
            {
                PendingPartyInvite invite = default;
                SocialIndexRecord target = await repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (target != null)
                    invite = target.PendingPartyInvite;

                _inviteReceived?.Invoke(targetId, invite);
            }

            return inviteResult;
        }

        /// <summary>Accepts the party invite this account holds.</summary>
        public async Task<PartyRule> AcceptAsync(string accountId)
        {
            if (!await _ensureConnected())
                return PartyRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PartyRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                PartyRecord party = null;
                if (index != null && !string.IsNullOrEmpty(index.PendingPartyInvite.PartyId))
                    party = await repository.LoadPartyAsync(index.PendingPartyInvite.PartyId, CancellationToken.None);

                PartyRule rule = PartyRules.CanAccept(party, index, now);
                if (rule != PartyRule.Ok)
                    return rule;

                PartyRules.Accept(party, _accounts.NewMember(accountId, now));
                await repository.SavePartyAsync(party, CancellationToken.None);

                index.PartyId = party.PartyId;
                index.PendingPartyInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Declines the party invite this account holds.</summary>
        public async Task<PartyRule> DeclineAsync(string accountId)
        {
            if (!await _ensureConnected())
                return PartyRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            return await RetryAsync(async () =>
            {
                SocialIndexRecord index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || index.PendingPartyInvite.ExpiresAtTicks <= 0)
                    return PartyRule.NoParty;

                index.PendingPartyInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });
        }

        /// <summary>Leaves the party this account belongs to, disbanding it when it was the last member.</summary>
        public async Task<PartyRule> LeaveAsync(string accountId)
        {
            if (!await _ensureConnected())
                return PartyRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            PartyRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord index = await repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.PartyId))
                    return PartyRule.NoParty;

                PartyRecord party = await repository.LoadPartyAsync(index.PartyId, CancellationToken.None);
                if (party == null)
                {
                    index.PartyId = null;
                    index.PendingPartyInvite = default;
                    await repository.SaveIndexAsync(index, CancellationToken.None);
                    return PartyRule.Ok;
                }

                PartyRecord remaining = PartyRules.Remove(party, accountId);
                if (remaining == null)
                {
                    await _teardownRaid(party);
                    await repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                }
                else
                    await repository.SavePartyAsync(remaining, CancellationToken.None);

                index.PartyId = null;
                index.PendingPartyInvite = default;
                await repository.SaveIndexAsync(index, CancellationToken.None);
                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await _reconcile();

            return result;
        }

        /// <summary>Leader-only: removes <paramref name="targetId"/> from the leader's party.</summary>
        public async Task<PartyRule> KickAsync(string leaderId, string targetId)
        {
            if (!await _ensureConnected())
                return PartyRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            PartyRule result = await RetryAsync(async () =>
            {
                SocialIndexRecord leaderIndex = await repository.LoadIndexAsync(leaderId, CancellationToken.None);
                if (leaderIndex == null || string.IsNullOrEmpty(leaderIndex.PartyId))
                    return PartyRule.NoParty;

                PartyRecord party = await repository.LoadPartyAsync(leaderIndex.PartyId, CancellationToken.None);
                PartyRule rule = PartyRules.CanKick(party, leaderId, targetId);
                if (rule != PartyRule.Ok)
                    return rule;

                PartyRecord remaining = PartyRules.Remove(party, targetId);
                if (remaining == null)
                {
                    await _teardownRaid(party);
                    await repository.DeletePartyAsync(party.PartyId, CancellationToken.None);
                }
                else
                    await repository.SavePartyAsync(remaining, CancellationToken.None);

                SocialIndexRecord targetIndex = await repository.LoadIndexAsync(targetId, CancellationToken.None);
                if (targetIndex != null)
                {
                    targetIndex.PartyId = null;
                    targetIndex.PendingPartyInvite = default;
                    await repository.SaveIndexAsync(targetIndex, CancellationToken.None);
                }

                return PartyRule.Ok;
            });

            if (result == PartyRule.Ok)
                await _reconcile();

            return result;
        }

        private async Task<PartyRule> RetryAsync(Func<Task<PartyRule>> attempt)
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
                    Debug.LogError($"[Social] Party mutation failed: {e.Message}");
                    return PartyRule.BackendUnavailable;
                }
            }

            return PartyRule.Conflict;
        }
    }
}
