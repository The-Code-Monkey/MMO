using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>Friend requests, replies, removals and snapshot pushes, with CAS retries.</summary>
    internal sealed class FriendService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly LocalPlayerRegistry _players;
        private readonly SocialHintClient _hints;
        private readonly int _casRetries;

        public FriendService(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            LocalPlayerRegistry players,
            SocialHintClient hints,
            int casRetries)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _players = players;
            _hints = hints;
            _casRetries = casRetries;
        }

        /// <summary>Sends a friend request from <paramref name="requesterId"/> to the named player.</summary>
        public async Task<FriendRule> RequestAsync(string requesterId, string targetName)
        {
            if (string.IsNullOrEmpty(requesterId))
                return FriendRule.NotFound;

            if (!await _ensureConnected())
                return FriendRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            string targetId = await repository.ResolveNameAsync(SocialAccountResolver.Normalize(targetName), CancellationToken.None);
            if (string.IsNullOrEmpty(targetId))
                return FriendRule.NotFound;

            FriendRule result = await RetryAsync(async () =>
            {
                FriendListRecord mine = await LoadAsync(requesterId);
                FriendListRecord theirs = await LoadAsync(targetId);

                FriendRule rule = FriendRules.CanRequest(requesterId, targetId, mine, theirs);
                if (rule != FriendRule.Ok)
                    return rule;

                long now = NowTicks();

                // Target first: if the second write dies, the dangling PendingIn is derived state
                // that the reconcile pass or the accept path repairs — never a half-friend pair.
                FriendRules.ApplyRequest(theirs, requesterId, _players.NameOf(requesterId), now);
                await repository.SaveFriendsAsync(theirs, CancellationToken.None);

                FriendRules.ApplyPendingOut(mine, targetId, now);
                await repository.SaveFriendsAsync(mine, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await _hints.SendAsync("friend", string.Empty, requesterId, targetId);
                await PushAsync(requesterId);
                await PushAsync(targetId);
                NotifyEvent(targetId, FriendEvent.RequestReceived);
            }

            return result;
        }

        /// <summary>Accepts the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public async Task<FriendRule> AcceptAsync(string accountId, string fromAccountId)
        {
            if (!await _ensureConnected())
                return FriendRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            FriendRule result = await RetryAsync(async () =>
            {
                FriendListRecord mine = await LoadAsync(accountId);
                FriendRule rule = FriendRules.CanAccept(mine, fromAccountId);
                if (rule != FriendRule.Ok)
                    return rule;

                long now = NowTicks();
                FriendRules.ApplyAccept(mine, fromAccountId, now);
                await repository.SaveFriendsAsync(mine, CancellationToken.None);

                // Materialize the other side immediately; if this best-effort write fails, the
                // reconcile pass sees NeedsRepair and finishes it.
                FriendListRecord theirs = await LoadAsync(fromAccountId);
                if (theirs != null && FriendRules.NeedsRepair(mine, theirs, accountId, fromAccountId))
                {
                    FriendRules.ApplyAccept(theirs, accountId, now);
                    await repository.SaveFriendsAsync(theirs, CancellationToken.None);
                }

                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await _hints.SendAsync("friend", string.Empty, accountId, fromAccountId);
                await PushAsync(accountId);
                await PushAsync(fromAccountId);
                NotifyEvent(fromAccountId, FriendEvent.Accepted);
            }

            return result;
        }

        /// <summary>Declines the pending friend request from <paramref name="fromAccountId"/>.</summary>
        public async Task<FriendRule> DeclineAsync(string accountId, string fromAccountId)
        {
            if (!await _ensureConnected())
                return FriendRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            FriendRule result = await RetryAsync(async () =>
            {
                FriendListRecord mine = await LoadAsync(accountId);
                FriendRule rule = FriendRules.CanDecline(mine, fromAccountId);
                if (rule != FriendRule.Ok)
                    return rule;

                FriendRules.Remove(mine, fromAccountId);
                await repository.SaveFriendsAsync(mine, CancellationToken.None);

                FriendListRecord theirs = await LoadAsync(fromAccountId);
                FriendRules.Remove(theirs, accountId);
                await repository.SaveFriendsAsync(theirs, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await _hints.SendAsync("friend", string.Empty, accountId, fromAccountId);
                await PushAsync(accountId);
                await PushAsync(fromAccountId);
                NotifyEvent(fromAccountId, FriendEvent.Declined);
            }

            return result;
        }

        /// <summary>Removes an accepted friend from both sides.</summary>
        public async Task<FriendRule> RemoveAsync(string accountId, string targetId)
        {
            if (!await _ensureConnected())
                return FriendRule.BackendUnavailable;

            ISocialRepository repository = _repository();
            FriendRule result = await RetryAsync(async () =>
            {
                FriendListRecord mine = await LoadAsync(accountId);
                FriendRule rule = FriendRules.CanRemove(mine, targetId);
                if (rule != FriendRule.Ok)
                    return rule;

                FriendRules.Remove(mine, targetId);
                await repository.SaveFriendsAsync(mine, CancellationToken.None);

                FriendListRecord theirs = await LoadAsync(targetId);
                FriendRules.Remove(theirs, accountId);
                await repository.SaveFriendsAsync(theirs, CancellationToken.None);
                return FriendRule.Ok;
            });

            if (result == FriendRule.Ok)
            {
                await _hints.SendAsync("friend", string.Empty, accountId, targetId);
                await PushAsync(accountId);
                await PushAsync(targetId);
                NotifyEvent(targetId, FriendEvent.Removed);
            }

            return result;
        }

        /// <summary>Pushes a fresh snapshot to a local player's friend list.</summary>
        public async Task PushAsync(string accountId)
        {
            ISocialRepository repository = _repository();
            if (repository == null || string.IsNullOrEmpty(accountId))
                return;

            if (!_players.TryGet(accountId, out LocalPlayerHandle handle) || handle.DeliverFriends == null)
                return;

            FriendListRecord record = await LoadAsync(accountId);
            var ids = new List<string>();
            if (record?.Entries != null)
            {
                foreach (FriendEntry entry in record.Entries)
                    ids.Add(entry.AccountId);
            }

            var online = new HashSet<string>();
            if (ids.Count > 0)
            {
                List<PresenceRecord> presences = await repository.LoadPresencesAsync(ids, CancellationToken.None);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (PresenceRecord presence in presences)
                {
                    if (presence != null && presence.IsHeartbeatFresh(now))
                        online.Add(presence.AccountId);
                }
            }

            var snapshot = new FriendListSnapshot();
            if (record?.Entries != null)
            {
                foreach (FriendEntry entry in record.Entries)
                {
                    snapshot.Friends.Add(new FriendView
                    {
                        AccountId = entry.AccountId,
                        PlayerName = string.IsNullOrEmpty(entry.PlayerName) ? entry.AccountId : entry.PlayerName,
                        State = entry.State,
                        Online = online.Contains(entry.AccountId),
                    });
                }
            }

            handle.DeliverFriends(JsonUtility.ToJson(snapshot));
        }

        public async Task RefreshLocalAsync()
        {
            if (_repository() == null || _players.Count == 0)
                return;

            foreach (string accountId in _players.AccountIds())
                await PushAsync(accountId);
        }

        public async Task RepairAsync()
        {
            ISocialRepository repository = _repository();
            foreach (string accountId in _players.AccountIds())
            {
                FriendListRecord mine = await LoadAsync(accountId);
                if (mine?.Entries == null)
                    continue;

                foreach (FriendEntry entry in new List<FriendEntry>(mine.Entries))
                {
                    if (entry.State != FriendState.Accepted)
                        continue;

                    FriendListRecord theirs = await LoadAsync(entry.AccountId);
                    if (!FriendRules.NeedsRepair(mine, theirs, accountId, entry.AccountId))
                        continue;

                    try
                    {
                        FriendRules.ApplyAccept(theirs, accountId, NowTicks());
                        await repository.SaveFriendsAsync(theirs, CancellationToken.None);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Social] friend repair failed for '{entry.AccountId}': {e.Message}");
                    }
                }
            }
        }

        private async Task<FriendListRecord> LoadAsync(string accountId)
            => await _repository().LoadFriendsAsync(accountId, CancellationToken.None)
               ?? new FriendListRecord { AccountId = accountId };

        private void NotifyEvent(string accountId, FriendEvent evt)
        {
            if (_players.TryGet(accountId, out LocalPlayerHandle handle))
                handle.NotifyFriendEvent?.Invoke((int)evt);
        }

        private static long NowTicks() => DateTimeOffset.UtcNow.UtcDateTime.Ticks;

        private async Task<FriendRule> RetryAsync(Func<Task<FriendRule>> attempt)
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
                    Debug.LogError($"[Social] Friend mutation failed: {e.Message}");
                    return FriendRule.BackendUnavailable;
                }
            }

            return FriendRule.Conflict;
        }
    }
}
