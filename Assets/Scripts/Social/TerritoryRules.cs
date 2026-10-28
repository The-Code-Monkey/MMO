using System;

namespace MMO.Social
{
    /// <summary>
    /// A weekly capture window in UTC. Stored as minutes since midnight because
    /// <see cref="TimeSpan"/> is not Unity-serializable.
    /// </summary>
    [Serializable]
    public struct VulnerabilityWindow
    {
        public DayOfWeek[] Days;
        public int StartMinuteUtc;
        public int EndMinuteUtc;
    }

    /// <summary>The outcome of a territory capture attempt.</summary>
    public enum TerritoryRule
    {
        Ok,
        NoGuild,
        AlreadyOwner,
        NotOwner,
        NotAttackable,
        BackendUnavailable,
        Conflict,
    }

    /// <summary>
    /// Pure territory rules: whether a flag is attackable now, and whether this guild may capture
    /// it. Empty or malformed windows fail closed.
    /// </summary>
    public static class TerritoryRules
    {
        public const int MinutesPerDay = 24 * 60;

        /// <summary>
        /// True when <paramref name="nowUtc"/> falls inside the weekly window. A window whose start
        /// is after its end crosses midnight; the listed day is the day the window starts.
        /// </summary>
        public static bool IsAttackable(VulnerabilityWindow window, DateTimeOffset nowUtc)
        {
            if (!IsValid(window))
                return false;

            int minute = nowUtc.Hour * 60 + nowUtc.Minute;

            if (window.StartMinuteUtc < window.EndMinuteUtc)
            {
                return Contains(window.Days, nowUtc.DayOfWeek) &&
                       minute >= window.StartMinuteUtc &&
                       minute < window.EndMinuteUtc;
            }

            if (Contains(window.Days, nowUtc.DayOfWeek) && minute >= window.StartMinuteUtc)
                return true;

            return Contains(window.Days, nowUtc.AddDays(-1).DayOfWeek) &&
                   minute < window.EndMinuteUtc;
        }

        /// <summary>
        /// Whether <paramref name="actorGuildId"/> may take the flag. An unowned flag is capturable
        /// at any time; an owned one only inside its vulnerability window.
        /// </summary>
        public static TerritoryRule CanCapture(
            VulnerabilityWindow window,
            DateTimeOffset nowUtc,
            string currentOwnerGuildId,
            string actorGuildId)
        {
            if (string.IsNullOrEmpty(actorGuildId))
                return TerritoryRule.NoGuild;

            if (!string.IsNullOrEmpty(currentOwnerGuildId) && currentOwnerGuildId == actorGuildId)
                return TerritoryRule.AlreadyOwner;

            if (string.IsNullOrEmpty(currentOwnerGuildId))
                return TerritoryRule.Ok;

            return IsAttackable(window, nowUtc)
                ? TerritoryRule.Ok
                : TerritoryRule.NotAttackable;
        }

        /// <summary>True when the window has at least one day and usable minute bounds.</summary>
        public static bool IsValid(VulnerabilityWindow window)
        {
            if (window.Days == null || window.Days.Length == 0)
                return false;

            if (window.StartMinuteUtc < 0 || window.StartMinuteUtc >= MinutesPerDay)
                return false;

            if (window.EndMinuteUtc < 0 || window.EndMinuteUtc >= MinutesPerDay)
                return false;

            return window.StartMinuteUtc != window.EndMinuteUtc;
        }

        private static bool Contains(DayOfWeek[] days, DayOfWeek day)
        {
            for (int i = 0; i < days.Length; i++)
            {
                if (days[i] == day)
                    return true;
            }

            return false;
        }
    }
}
