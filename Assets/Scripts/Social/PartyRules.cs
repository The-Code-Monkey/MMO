using System;

namespace MMO.Social
{
    /// <summary>The outcome of a party decision. <see cref="Ok"/> is the only success.</summary>
    public enum PartyRule
    {
        Ok,
        SelfInvite,
        TargetOffline,
        TargetNotFound,
        TargetAlreadyInParty,
        AlreadyInParty,
        PartyFull,
        NotLeader,
        NoParty,
        NotInParty,
        InviteExpired,
        BackendUnavailable,
        Conflict,
    }

    /// <summary>
    /// Every party decision as a pure function. Runs on the zone server before the backend write
    /// <em>and</em> inside <see cref="SocialHubService"/>'s reconcile repair pass, so it lives where
    /// the EditMode tests can reach it (AGENTS.md: server validation gets isolated unit tests).
    /// Nothing here touches the network, storage, or Unity.
    /// </summary>
    public static class PartyRules
    {
        /// <summary>The normal party cap; the raid cap is a separate, larger number.</summary>
        public const int MaxMembers = 5;

        /// <summary>How long an unanswered invite stays valid.</summary>
        public static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Whether <paramref name="inviterId"/> may invite the player behind <paramref name="target"/>.
        /// The inviter's own party (or null when they have none) is passed in so a full party is
        /// refused before the backend write, not after.
        /// </summary>
        public static PartyRule CanInvite(
            string inviterId,
            PartyRecord inviterParty,
            SocialIndexRecord target,
            bool targetOnline)
        {
            if (string.IsNullOrEmpty(inviterId) || target == null || string.IsNullOrEmpty(target.AccountId))
                return PartyRule.TargetNotFound;

            if (target.AccountId == inviterId)
                return PartyRule.SelfInvite;

            if (!targetOnline)
                return PartyRule.TargetOffline;

            if (!string.IsNullOrEmpty(target.PartyId))
                return PartyRule.TargetAlreadyInParty;

            // Inviter is already a party's member: it must exist and have room.
            if (inviterParty != null)
            {
                if (FindMember(inviterParty, inviterId).AccountId == null)
                    return PartyRule.NotInParty;

                if (inviterParty.Members.Count >= MaxMembers)
                    return PartyRule.PartyFull;
            }

            return PartyRule.Ok;
        }

        /// <summary>Whether a pending invite is still inside its TTL.</summary>
        public static bool IsInviteExpired(PendingPartyInvite invite, DateTimeOffset now)
            => invite.ExpiresAtTicks <= 0 || now.UtcDateTime.Ticks > invite.ExpiresAtTicks;

        /// <summary>
        /// Whether <paramref name="accountId"/> may accept the invite they hold. Checks expiry,
        /// capacity, and that they are not already a member.
        /// </summary>
        public static PartyRule CanAccept(PartyRecord party, SocialIndexRecord index, DateTimeOffset now)
        {
            if (index == null || index.PendingPartyInvite.ExpiresAtTicks <= 0)
                return PartyRule.NoParty;

            if (IsInviteExpired(index.PendingPartyInvite, now))
                return PartyRule.InviteExpired;

            if (party == null)
                return PartyRule.NoParty;

            if (FindMember(party, index.AccountId).AccountId != null)
                return PartyRule.AlreadyInParty;

            if (party.Members.Count >= MaxMembers)
                return PartyRule.PartyFull;

            return PartyRule.Ok;
        }

        /// <summary>Adds a member. Returns the same record for convenient chaining.</summary>
        public static PartyRecord Accept(PartyRecord party, PartyMemberRecord member)
        {
            if (party == null)
                return null;

            if (FindMember(party, member.AccountId).AccountId == null)
                party.Members.Add(member);

            return party;
        }

        /// <summary>
        /// Removes a member. Returns null when the party should disband (its last member left);
        /// otherwise the same record, with the leader promoted to the earliest-joined remaining
        /// member when the leaver was the leader.
        /// </summary>
        public static PartyRecord Remove(PartyRecord party, string accountId)
        {
            if (party == null)
                return null;

            PartyMemberRecord member = FindMember(party, accountId);
            if (member.AccountId == null)
                return party;

            party.Members.Remove(member);

            if (party.Members.Count == 0)
                return null;

            if (party.LeaderId == accountId)
                party.LeaderId = EarliestMember(party).AccountId;

            return party;
        }

        /// <summary>Whether <paramref name="actorId"/> (must be the leader) may kick the target.</summary>
        public static PartyRule CanKick(PartyRecord party, string actorId, string targetId)
        {
            if (party == null)
                return PartyRule.NoParty;

            if (FindMember(party, actorId).AccountId == null)
                return PartyRule.NotInParty;

            if (party.LeaderId != actorId)
                return PartyRule.NotLeader;

            if (targetId == actorId)
                return PartyRule.NotInParty;

            if (FindMember(party, targetId).AccountId == null)
                return PartyRule.TargetNotFound;

            return PartyRule.Ok;
        }

        /// <summary>The party member for <paramref name="accountId"/>, or its default when absent.</summary>
        public static PartyMemberRecord FindMember(PartyRecord party, string accountId)
        {
            if (party?.Members == null || string.IsNullOrEmpty(accountId))
                return default;

            for (int i = 0; i < party.Members.Count; i++)
            {
                if (party.Members[i].AccountId == accountId)
                    return party.Members[i];
            }

            return default;
        }

        public static bool IsMember(PartyRecord party, string accountId)
            => FindMember(party, accountId).AccountId != null;

        private static PartyMemberRecord EarliestMember(PartyRecord party)
        {
            PartyMemberRecord next = party.Members[0];
            for (int i = 1; i < party.Members.Count; i++)
            {
                if (party.Members[i].JoinedAtTicks < next.JoinedAtTicks)
                    next = party.Members[i];
            }

            return next;
        }
    }
}
