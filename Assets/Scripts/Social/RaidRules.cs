using System;

namespace MMO.Social
{
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
    }

    /// <summary>
    /// Every raid decision as a pure function. Runs on the zone server before the backend write
    /// and inside the reconcile repair pass.
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

        public static bool NeedsTransfer(string raidInstanceId, string localServerId, string hostServerId)
            => !string.IsNullOrEmpty(raidInstanceId)
               && !string.Equals(localServerId, hostServerId, StringComparison.Ordinal);
    }
}
