using System;

namespace MMO.Gameplay
{
    /// <summary>Why a request to change the PvP flag was accepted or refused.</summary>
    public enum PvPRequestResult
    {
        /// <summary>The server wrote the requested value.</summary>
        Accepted = 0,

        /// <summary>The flag already held the requested value; nothing was written.</summary>
        AlreadySet = 1,

        /// <summary>Opting out inside the cooldown window; the request is refused until it elapses.</summary>
        Cooldown = 2,

        /// <summary>PvP is forced by the zone, so the flag has no meaning here.</summary>
        ForcedZone = 3,
    }

    /// <summary>
    /// The PvP decision surface as pure functions, in the same shape as <see cref="RiskRules"/>:
    /// no engine types, no networked state, every branch reachable from an EditMode test.
    ///
    /// <para>
    /// Two questions are asked per hit and they are deliberately separate: <see cref="CanHarm"/>
    /// decides whether this player may be damaged by another player here, while
    /// <see cref="RiskRules.ResolveDeath"/> decides what dying costs. The flag never changes a
    /// death cost, and the tier never changes on its own because someone is flagged.
    /// </para>
    ///
    /// <para>
    /// A zone tier with no row is a bug, not a default: an undeclared value throws instead of
    /// silently resolving to "allowed", and the tests enumerate every declared tier so a new one
    /// cannot slip in unnoticed. (C# offers no compile-time exhaustiveness check for enums, so a
    /// test is the strongest guarantee available.)
    /// </para>
    /// </summary>
    public static class PvPRules
    {
        /// <summary>
        /// How long a player must go without player-versus-player damage before opting out. The
        /// window exists so opting out cannot be used to escape a fight already underway.
        /// </summary>
        public const double OptOutCooldownSeconds = 300d;

        /// <summary>
        /// True when <paramref name="tier"/> forces PvP, making the flag irrelevant in both
        /// directions.
        /// </summary>
        public static bool IsForced(ZoneRiskTier tier)
        {
            switch (tier)
            {
                case ZoneRiskTier.Safe:
                case ZoneRiskTier.Yellow:
                    return false;
                case ZoneRiskTier.Red:
                case ZoneRiskTier.Black:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Undeclared zone risk tier.");
            }
        }

        /// <summary>
        /// True when <paramref name="attackerFlagged"/> may damage <paramref name="targetFlagged"/>
        /// in a zone of <paramref name="tier"/>.
        ///
        /// <para>
        /// <see cref="ZoneRiskTier.Safe"/> refuses everything; <see cref="ZoneRiskTier.Yellow"/>
        /// requires the target to have opted in, so an unflagged player is untouchable by any
        /// player damage; the forced tiers allow it regardless of either flag.
        /// </para>
        /// </summary>
        public static bool CanHarm(ZoneRiskTier tier, bool attackerFlagged, bool targetFlagged)
        {
            switch (tier)
            {
                case ZoneRiskTier.Safe:
                    return false;
                case ZoneRiskTier.Yellow:
                    return targetFlagged;
                case ZoneRiskTier.Red:
                case ZoneRiskTier.Black:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Undeclared zone risk tier.");
            }
        }

        /// <summary>
        /// True when landing this hit is itself consent: an unflagged attacker hitting a flagged
        /// target in a yellow zone is flagged by the server before the damage is applied.
        /// </summary>
        public static bool AutoFlags(ZoneRiskTier tier, bool attackerFlagged, bool targetFlagged)
        {
            switch (tier)
            {
                case ZoneRiskTier.Safe:
                case ZoneRiskTier.Red:
                case ZoneRiskTier.Black:
                    return false;
                case ZoneRiskTier.Yellow:
                    return targetFlagged && !attackerFlagged;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Undeclared zone risk tier.");
            }
        }

        /// <summary>
        /// True when the cooldown since the last player-versus-player damage has elapsed. A
        /// negative-infinite timestamp (never hit or struck) is always ready.
        /// </summary>
        public static bool IsOptOutReady(double now, double lastPlayerHarmAt)
        {
            double elapsed = now - lastPlayerHarmAt;
            if (double.IsNaN(elapsed))
                return false;

            return elapsed >= OptOutCooldownSeconds;
        }

        /// <summary>
        /// Seconds left on the opt-out cooldown, clamped to 0..<see cref="OptOutCooldownSeconds"/>
        /// so a client that reports a clock in the past cannot extend the window past its length.
        /// </summary>
        public static double RemainingOptOut(double now, double lastPlayerHarmAt)
        {
            double remaining = OptOutCooldownSeconds - (now - lastPlayerHarmAt);
            if (double.IsNaN(remaining))
                return OptOutCooldownSeconds;

            if (remaining <= 0d)
                return 0d;

            return remaining > OptOutCooldownSeconds ? OptOutCooldownSeconds : remaining;
        }

        /// <summary>
        /// The whole opt-in/opt-out verdict: forced zones refuse the toggle entirely, a no-op
        /// request is reported as such, and opting out waits out the cooldown.
        /// </summary>
        public static PvPRequestResult ResolveFlagRequest(
            ZoneRiskTier tier,
            bool currentlyFlagged,
            bool requestedFlagged,
            double now,
            double lastPlayerHarmAt)
        {
            if (IsForced(tier))
                return PvPRequestResult.ForcedZone;

            if (requestedFlagged == currentlyFlagged)
                return PvPRequestResult.AlreadySet;

            if (!requestedFlagged && !IsOptOutReady(now, lastPlayerHarmAt))
                return PvPRequestResult.Cooldown;

            return PvPRequestResult.Accepted;
        }
    }
}
