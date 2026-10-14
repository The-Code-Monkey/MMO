using System;

namespace MMO.Social
{
    /// <summary>The outcome of a raid return transfer decision. <see cref="Ok"/> is the only success.</summary>
    public enum RaidTransferRule
    {
        Ok,

        /// <summary>The member account id is null or empty.</summary>
        NoMember,

        /// <summary>The member has already left the host zone (manually zoned out).</summary>
        MemberAlreadyLeft,

        /// <summary>No return anchor was recorded for this member at hand-out time.</summary>
        NoAnchor,

        /// <summary>The anchor's <see cref="RaidReturnAnchor.Returned"/> flag is already true.</summary>
        AlreadyReturned,

        /// <summary>The member is not local to the host zone.</summary>
        NotLocal,
    }

    /// <summary>
    /// Pure decision rules for whether a raid member should be transferred back to their origin zone.
    /// EditMode-testable per agents.md — no network, no Unity, only data.
    /// </summary>
    public static class RaidTransferRules
    {
        /// <summary>
        /// Whether a member should be handed home right now:
        /// - accountId is valid
        /// - member is still local to the host zone (<paramref name="memberStillLocal"/>)
        /// - anchor exists for this account
        /// - anchor's Returned flag is false
        /// </summary>
        public static RaidTransferRule CanTransferBack(
            string accountId,
            string hostZoneId,
            RaidReturnAnchor anchor,
            bool memberStillLocal)
        {
            if (string.IsNullOrEmpty(accountId))
                return RaidTransferRule.NoMember;

            if (!memberStillLocal)
                return RaidTransferRule.MemberAlreadyLeft;

            if (string.IsNullOrEmpty(anchor.AccountId))
                return RaidTransferRule.NoAnchor;

            if (anchor.Returned)
                return RaidTransferRule.AlreadyReturned;

            return RaidTransferRule.Ok;
        }
    }
}