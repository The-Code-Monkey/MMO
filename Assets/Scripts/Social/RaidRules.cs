using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>The outcome of a raid decision. <see cref="Ok"/> is the only success.</summary>
    public enum RaidRule
    {
        Ok,
        NoParty,
        NotLeader,
        PartyTooSmall,
        AlreadyInRaid,
        RaidMissing,
        NotMember,
        CasExhausted,
        BackendUnavailable,

        /// <summary>The raid record itself is absent (it may have disbanded).</summary>
        NoRaid,

        /// <summary>Joining this party would push the raid roster past <see cref="RaidRules.MaxRaidSize"/>.</summary>
        RaidFull,

        /// <summary>The target is not on the raid roster.</summary>
        NotInRaid,

        /// <summary>No healthy, ready zone with spare capacity was available to host the instance.</summary>
        NoHostAvailable,
    }

    /// <summary>
    /// Every raid decision as a pure function. Runs on the zone server before the backend write
    /// and inside the reconcile repair pass. Issue #51 defined a raid as 1:1 with a party; #54 lifts
    /// that to a set of member parties all pointing at one shared <see cref="RaidInstanceRecord"/>,
    /// so capacity, leadership and teardown are decisions over that set — pure, network-free, and
    /// therefore EditMode-testable per AGENTS.md.
    /// </summary>
    public static class RaidRules
    {
        public const int MinRaidSize = 5;
        public const int MaxRaidSize = 25;

        public static RaidRule CanAllocate(PartyRecord party, string requesterId)
        {
            if (party == null || party.Members == null)
                return RaidRule.NoParty;
            if (party.LeaderId != requesterId)
                return RaidRule.NotLeader;
            if (!string.IsNullOrEmpty(party.RaidInstanceId))
                return RaidRule.AlreadyInRaid;
            if (party.Members.Count < MinRaidSize)
                return RaidRule.PartyTooSmall;
            return RaidRule.Ok;
        }

        public static RaidRule CanComplete(PartyRecord party, string requesterId)
        {
            if (party == null)
                return RaidRule.NoParty;
            if (string.IsNullOrEmpty(party.RaidInstanceId))
                return RaidRule.RaidMissing;
            if (party.LeaderId != requesterId)
                return RaidRule.NotLeader;
            return RaidRule.Ok;
        }

        /// <summary>
        /// The flat, de-duplicated account roster across every member party. Computed, never stored
        /// as the source of truth, so it cannot drift from <see cref="RaidInstanceRecord.MemberPartyIds"/>.
        /// </summary>
        public static List<string> Roster(RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties)
        {
            var roster = new List<string>();
            if (raid == null || memberParties == null)
                return roster;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string partyId in MemberPartyIdsOf(raid))
            {
                PartyRecord party = FindParty(memberParties, partyId);
                if (party?.Members == null)
                    continue;

                foreach (PartyMemberRecord member in party.Members)
                {
                    if (!string.IsNullOrEmpty(member.AccountId) && seen.Add(member.AccountId))
                        roster.Add(member.AccountId);
                }
            }

            return roster;
        }

        /// <summary>
        /// Whether <paramref name="party"/> may join <paramref name="raid"/>. The parties already in
        /// the raid are passed in so capacity is checked against the real roster before the write.
        /// </summary>
        public static RaidRule CanJoin(
            RaidInstanceRecord raid, PartyRecord party, IReadOnlyList<PartyRecord> currentMembers)
        {
            if (raid == null)
                return RaidRule.NoRaid;
            if (party == null || party.Members == null)
                return RaidRule.NoParty;
            if (!string.IsNullOrEmpty(party.RaidInstanceId))
                return RaidRule.AlreadyInRaid;
            if (IncludesParty(raid, party.PartyId))
                return RaidRule.AlreadyInRaid;

            int after = Roster(raid, currentMembers).Count + party.Members.Count;
            return after > MaxRaidSize ? RaidRule.RaidFull : RaidRule.Ok;
        }

        /// <summary>
        /// Whether <paramref name="partyId"/> is a member party of the raid. Legacy #51 records carry
        /// only the single seed <see cref="RaidInstanceRecord.PartyId"/>, so an empty member list
        /// falls back to it — a pre-#54 raid must still report its seed as a member.
        /// </summary>
        public static bool IncludesParty(RaidInstanceRecord raid, string partyId)
        {
            if (raid == null || string.IsNullOrEmpty(partyId))
                return false;

            foreach (string id in MemberPartyIdsOf(raid))
            {
                if (id == partyId)
                    return true;
            }

            return false;
        }

        /// <summary>Only the current raid leader may hand leadership to a member.</summary>
        public static RaidRule CanPromote(
            RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties, string actorId, string targetId)
        {
            if (raid == null)
                return RaidRule.NoRaid;
            if (raid.LeaderId != actorId)
                return RaidRule.NotLeader;
            if (string.IsNullOrEmpty(targetId) || !Roster(raid, memberParties).Contains(targetId))
                return RaidRule.NotInRaid;
            return RaidRule.Ok;
        }

        /// <summary>
        /// The account that should lead the raid. The recorded leader wins while they are still on the
        /// roster; otherwise leadership falls to the seed party's leader, then to the first member
        /// party's leader. Never returns an account that is not in a member party.
        /// </summary>
        public static string ResolveLeader(RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties)
        {
            if (raid == null)
                return null;

            List<string> roster = Roster(raid, memberParties);
            if (!string.IsNullOrEmpty(raid.LeaderId) && roster.Contains(raid.LeaderId))
                return raid.LeaderId;

            PartyRecord next = FindParty(memberParties, raid.SeedPartyId) ?? FirstParty(raid, memberParties);
            return next?.LeaderId;
        }

        /// <summary>
        /// Removes a member party. Returns the surviving raid, or null when the last party leaves and
        /// the raid should disband. When the seed party leaves the raid rolls its seed — and clears
        /// the now-orphaned leader, which <see cref="ResolveLeader"/> then reseeds.
        /// </summary>
        public static RaidInstanceRecord RemoveParty(RaidInstanceRecord raid, string partyId)
        {
            if (raid == null || string.IsNullOrEmpty(partyId))
                return raid;

            // Legacy #51 record: the single seed party is the whole raid, so it disbands alone.
            if (raid.MemberPartyIds == null || raid.MemberPartyIds.Count == 0)
                return raid.PartyId == partyId ? null : raid;

            if (!raid.MemberPartyIds.Remove(partyId))
                return raid;

            if (raid.MemberPartyIds.Count == 0)
                return null;

            if (partyId == raid.SeedPartyId)
            {
                raid.SeedPartyId = raid.MemberPartyIds[0];
                raid.LeaderId = null;
            }

            return raid;
        }

        public static bool NeedsTransfer(string raidInstanceId, string localServerId, string hostServerId)
            => !string.IsNullOrEmpty(raidInstanceId)
               && !string.Equals(localServerId, hostServerId, StringComparison.Ordinal);

        private static PartyRecord FindParty(IReadOnlyList<PartyRecord> parties, string partyId)
        {
            if (parties == null || string.IsNullOrEmpty(partyId))
                return null;

            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] != null && parties[i].PartyId == partyId)
                    return parties[i];
            }

            return null;
        }

        private static PartyRecord FirstParty(RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties)
        {
            foreach (string partyId in MemberPartyIdsOf(raid))
            {
                PartyRecord party = FindParty(memberParties, partyId);
                if (party != null)
                    return party;
            }

            return null;
        }

        /// <summary>
        /// The member party ids, treating a legacy #51 record (no <see cref="RaidInstanceRecord.MemberPartyIds"/>)
        /// as a one-party raid keyed on <see cref="RaidInstanceRecord.PartyId"/>.
        /// </summary>
        private static IEnumerable<string> MemberPartyIdsOf(RaidInstanceRecord raid)
        {
            if (raid?.MemberPartyIds != null && raid.MemberPartyIds.Count > 0)
                return raid.MemberPartyIds;
            if (!string.IsNullOrEmpty(raid?.PartyId))
                return new[] { raid.PartyId };
            return Array.Empty<string>();
        }
    }
}
