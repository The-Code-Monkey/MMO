namespace MMO.Social
{
    /// <summary>The outcome of a friend decision. <see cref="Ok"/> is the only success.</summary>
    public enum FriendRule
    {
        Ok,
        SelfRequest,
        AlreadyFriends,
        RequestAlreadyPending,
        NotPending,
        NotFound,
        BackendUnavailable,
        Conflict,
    }

    /// <summary>Non-error friend toasts pushed to a client.</summary>
    public enum FriendEvent
    {
        RequestReceived = 0,
        Accepted = 1,
        Declined = 2,
        Removed = 3,
    }

    /// <summary>
    /// Every friend decision as a pure function — isolated EditMode tests per AGENTS.md. Nothing
    /// here touches the network, storage or Unity.
    /// </summary>
    public static class FriendRules
    {
        /// <summary>
        /// Whether <paramref name="requesterId"/> may send a request to <paramref name="targetId"/>,
        /// given both sides' records.
        /// </summary>
        public static FriendRule CanRequest(
            string requesterId, string targetId, FriendListRecord mine, FriendListRecord theirs)
        {
            if (string.IsNullOrEmpty(requesterId) || requesterId == targetId)
                return FriendRule.SelfRequest;

            // Any existing relationship on my side decides first.
            FriendEntry towardsThem = Find(mine, targetId);
            if (towardsThem.AccountId == targetId)
                return towardsThem.State == FriendState.Accepted
                    ? FriendRule.AlreadyFriends
                    : FriendRule.RequestAlreadyPending;   // PendingOut, or they already asked me

            // Their side already knows about me: a dangling accept or a request in flight.
            FriendEntry towardsMe = Find(theirs, requesterId);
            if (towardsMe.AccountId == requesterId)
                return towardsMe.State == FriendState.Accepted
                    ? FriendRule.AlreadyFriends
                    : FriendRule.RequestAlreadyPending;

            return FriendRule.Ok;
        }

        /// <summary>Whether <paramref name="mine"/> holds a PendingIn from <paramref name="fromAccountId"/>.</summary>
        public static FriendRule CanAccept(FriendListRecord mine, string fromAccountId)
        {
            FriendEntry entry = Find(mine, fromAccountId);
            return entry.AccountId == fromAccountId && entry.State == FriendState.PendingIn
                ? FriendRule.Ok
                : FriendRule.NotPending;
        }

        /// <summary>Whether <paramref name="mine"/> holds a PendingIn from <paramref name="targetId"/>.</summary>
        public static FriendRule CanDecline(FriendListRecord mine, string targetId)
        {
            FriendEntry entry = Find(mine, targetId);
            return entry.AccountId == targetId && entry.State == FriendState.PendingIn
                ? FriendRule.Ok
                : FriendRule.NotPending;
        }

        /// <summary>Whether <paramref name="mine"/> is accepted friends with <paramref name="targetId"/>.</summary>
        public static FriendRule CanRemove(FriendListRecord mine, string targetId)
        {
            FriendEntry entry = Find(mine, targetId);
            return entry.AccountId == targetId && entry.State == FriendState.Accepted
                ? FriendRule.Ok
                : FriendRule.NotPending;
        }

        /// <summary>Target's side of a request: record the incoming pending entry.</summary>
        public static void ApplyRequest(
            FriendListRecord target, string requesterId, string requesterName, long nowTicks)
            => Upsert(target, new FriendEntry
            {
                AccountId = requesterId,
                PlayerName = requesterName,
                State = FriendState.PendingIn,
                UpdatedAtTicks = nowTicks,
            });

        /// <summary>Requester's side of a request: record the outgoing pending entry.</summary>
        public static void ApplyPendingOut(FriendListRecord mine, string targetId, long nowTicks)
            => Upsert(mine, new FriendEntry
            {
                AccountId = targetId,
                State = FriendState.PendingOut,
                UpdatedAtTicks = nowTicks,
            });

        /// <summary>Either side of an accept: record the mutual entry.</summary>
        public static void ApplyAccept(FriendListRecord side, string otherId, long nowTicks)
            => Upsert(side, new FriendEntry
            {
                AccountId = otherId,
                State = FriendState.Accepted,
                UpdatedAtTicks = nowTicks,
            });

        /// <summary>Removes an entry, whether pending or accepted.</summary>
        public static void Remove(FriendListRecord record, string accountId)
        {
            if (record?.Entries == null)
                return;

            for (int i = 0; i < record.Entries.Count; i++)
            {
                if (record.Entries[i].AccountId == accountId)
                {
                    record.Entries.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Reconcile predicate: I already record Accepted towards them, but their record does not
        /// record Accepted back — the mirror write of an accept failed or a notify was lost. Both
        /// sides are storage truth; repair theirs so the pair converges.
        /// </summary>
        public static bool NeedsRepair(
            FriendListRecord mine, FriendListRecord theirs, string myId, string theirId)
        {
            FriendEntry mineTowardsThem = Find(mine, theirId);
            if (mineTowardsThem.AccountId != theirId || mineTowardsThem.State != FriendState.Accepted)
                return false;   // this side has not accepted — nothing to mirror

            FriendEntry theirsTowardsMe = Find(theirs, myId);
            return theirsTowardsMe.AccountId != myId || theirsTowardsMe.State != FriendState.Accepted;
        }

        /// <summary>Returns default(FriendEntry) — AccountId null — when absent or record null.</summary>
        public static FriendEntry Find(FriendListRecord record, string accountId)
        {
            if (record?.Entries == null || string.IsNullOrEmpty(accountId))
                return default;

            for (int i = 0; i < record.Entries.Count; i++)
            {
                if (record.Entries[i].AccountId == accountId)
                    return record.Entries[i];
            }

            return default;
        }

        private static void Upsert(FriendListRecord record, FriendEntry entry)
        {
            if (record == null)
                return;

            record.Entries ??= new System.Collections.Generic.List<FriendEntry>();

            for (int i = 0; i < record.Entries.Count; i++)
            {
                if (record.Entries[i].AccountId == entry.AccountId)
                {
                    record.Entries[i] = entry;
                    return;
                }
            }

            record.Entries.Add(entry);
        }
    }
}
