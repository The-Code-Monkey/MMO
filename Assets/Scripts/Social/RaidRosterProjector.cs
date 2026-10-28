using System;
using System.Collections.Generic;

namespace MMO.Social
{
    /// <summary>Normalizes raid records and rebuilds their flat member snapshot.</summary>
    internal static class RaidRosterProjector
    {
        /// <summary>
        /// Normalizes a legacy #51 record, which carried only a single
        /// <see cref="RaidInstanceRecord.PartyId"/>, into the multi-party shape. Idempotent.
        /// </summary>
        public static void Normalize(RaidInstanceRecord raid)
        {
            if (raid.MemberPartyIds == null)
                raid.MemberPartyIds = new List<string>();
            if (raid.MemberPartyIds.Count == 0 && !string.IsNullOrEmpty(raid.PartyId))
                raid.MemberPartyIds.Add(raid.PartyId);
            if (string.IsNullOrEmpty(raid.SeedPartyId))
                raid.SeedPartyId = raid.PartyId;
        }

        public static void Refresh(RaidInstanceRecord raid, IReadOnlyList<PartyRecord> memberParties)
        {
            if (raid.Members == null)
                raid.Members = new List<RaidInstanceMember>();
            raid.Members.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (raid.MemberPartyIds == null || memberParties == null)
                return;

            foreach (string partyId in raid.MemberPartyIds)
            {
                PartyRecord party = FindParty(memberParties, partyId);
                if (party?.Members == null)
                    continue;

                foreach (PartyMemberRecord member in party.Members)
                {
                    if (!string.IsNullOrEmpty(member.AccountId) && seen.Add(member.AccountId))
                        raid.Members.Add(new RaidInstanceMember
                        {
                            AccountId = member.AccountId,
                            PlayerName = member.PlayerName,
                        });
                }
            }
        }

        public static PartyRecord FindParty(IReadOnlyList<PartyRecord> parties, string partyId)
        {
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] != null && parties[i].PartyId == partyId)
                    return parties[i];
            }

            return null;
        }
    }
}
