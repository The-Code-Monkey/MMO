using System;

namespace MMO.UI
{
    /// <summary>
    /// The words and numbers the PvP toggle shows, as pure functions. The view renders what this
    /// returns and nothing else, so the wording — including the forced-zone state and the opt-out
    /// countdown — is pinned by an EditMode test instead of by a screenshot.
    /// </summary>
    public static class PvPFlagUiFormat
    {
        /// <summary>Shown while the flag is on and no cooldown is running.</summary>
        public const string FlaggedLabel = "PvP: On";

        /// <summary>Shown while the flag is off.</summary>
        public const string UnflaggedLabel = "PvP: Off";

        /// <summary>Shown in a tier that forces PvP, where the flag has no meaning.</summary>
        public const string ForcedLabel = "PvP: forced here";

        /// <summary>Shown while the opt-out window is still counting down.</summary>
        public const string CooldownFormat = "PvP: On ({0}s)";

        /// <summary>
        /// The toggle's caption: forced beats flagged, and a running cooldown is worth showing
        /// because it is the reason a click currently does nothing.
        /// </summary>
        public static string Describe(bool forced, bool flagged, float cooldownSeconds)
        {
            if (forced)
                return ForcedLabel;

            if (!flagged)
                return UnflaggedLabel;

            int seconds = SecondsRemaining(cooldownSeconds);
            return seconds > 0 ? string.Format(CooldownFormat, seconds) : FlaggedLabel;
        }

        /// <summary>
        /// Whole seconds left, rounded up so a countdown never shows 0 while still locked out.
        /// A negative or non-finite input reads as ready.
        /// </summary>
        public static int SecondsRemaining(float cooldownSeconds)
        {
            if (float.IsNaN(cooldownSeconds) || cooldownSeconds <= 0f)
                return 0;

            double whole = Math.Ceiling(cooldownSeconds);
            return whole >= int.MaxValue ? int.MaxValue : (int)whole;
        }

        /// <summary>False in a forced zone: the control is disabled and the caption says why.</summary>
        public static bool CanToggle(bool forced) => !forced;
    }
}
