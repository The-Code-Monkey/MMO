using System;
using System.Collections.Generic;
using System.Text;

namespace MMO.Moderation
{
    /// <summary>The kind of action a moderator applied. Wire values are append-only.</summary>
    public enum ModerationAction
    {
        Warn = 0,
        Kick = 1,
        TempBan = 2,
        PermanentBan = 3,
    }

    /// <summary>
    /// Why an account was actioned, in the coarsest terms the player is allowed to see. The raw
    /// incident — detector name, evidence, packet contents — is deliberately not part of this
    /// enum, because telling a cheater what tripped the detector teaches the cheat.
    /// </summary>
    public enum ModerationCategory
    {
        MovementAnomaly = 0,
        OwnershipDuplicate = 1,
        CombatAnomaly = 2,
        Harassment = 3,
        Spam = 4,
        Scamming = 5,
        Other = 6,
    }

    /// <summary>The outcome of a player report, mirroring the other rule enums in the project.</summary>
    public enum ReportRule
    {
        Ok = 0,
        InvalidTarget = 1,
        RateLimited = 2,
        DuplicateTarget = 3,
        BackendUnavailable = 4,
        Conflict = 5,
    }

    /// <summary>
    /// Moderation as pure rules: the stable message code a notice carries, the player-facing text
    /// for it, and the report throttle. Nothing here reads a clock or touches the backend, so the
    /// throttle and the no-evidence promise are EditMode-testable.
    ///
    /// <para>
    /// A notice is a <em>code</em> from <see cref="MessageCode"/> plus an optional expiry. The
    /// player UI resolves the code to plain language; a server never travels the raw incident.
    /// </para>
    /// </summary>
    public static class ModerationRules
    {
        /// <summary>Reports one account may file in a rolling hour.</summary>
        public const int MaxReportsPerHour = 5;

        /// <summary>Window the throttle counts over: one hour of UTC ticks.</summary>
        public const long ReportWindowTicks = TimeSpan.TicksPerHour;

        /// <summary>Longest note a report may carry; a note is context for a human, not an essay.</summary>
        public const int MaxNoteLength = 280;

        /// <summary>
        /// The stable code a notice carries. Codes are wire strings: append new ones, never rename
        /// an existing one, because old clients resolve by value.
        /// </summary>
        public static string MessageCode(ModerationAction action, ModerationCategory category)
            => $"{ActionCode(action)}_{CategoryCode(category)}";

        public static string ActionCode(ModerationAction action)
            => action switch
            {
                ModerationAction.Warn => "WARN",
                ModerationAction.Kick => "KICK",
                ModerationAction.TempBan => "TEMP_BAN",
                ModerationAction.PermanentBan => "PERM_BAN",
                _ => "NOTICE",
            };

        public static string CategoryCode(ModerationCategory category)
            => category switch
            {
                ModerationCategory.MovementAnomaly => "MOVEMENT_ANOMALY",
                ModerationCategory.OwnershipDuplicate => "OWNERSHIP_DUPLICATE",
                ModerationCategory.CombatAnomaly => "COMBAT_ANOMALY",
                ModerationCategory.Harassment => "HARASSMENT",
                ModerationCategory.Spam => "SPAM",
                ModerationCategory.Scamming => "SCAMMING",
                _ => "OTHER",
            };

        /// <summary>
        /// The throttle: under the hourly cap and not a repeat of the same target. Both inputs are
        /// computed from the account's stored reports, so the rule itself stays pure.
        /// </summary>
        public static bool CanReport(int reportsLastHour, bool alreadyReportedSameTarget)
            => reportsLastHour < MaxReportsPerHour && !alreadyReportedSameTarget;

        /// <summary>How many reports fall inside the rolling window ending at <paramref name="nowTicks"/>.</summary>
        public static int CountRecent(IReadOnlyList<ModerationReportEntry> reports, long nowTicks)
        {
            if (reports == null)
                return 0;

            int count = 0;
            for (int i = 0; i < reports.Count; i++)
            {
                if (IsWithinWindow(reports[i].CreatedAtTicks, nowTicks))
                    count++;
            }

            return count;
        }

        /// <summary>True when the same account was already reported inside the window.</summary>
        public static bool HasRecentReportTo(
            IReadOnlyList<ModerationReportEntry> reports,
            string targetAccountId,
            long nowTicks)
        {
            if (reports == null || string.IsNullOrEmpty(targetAccountId))
                return false;

            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i].TargetAccountId == targetAccountId
                    && IsWithinWindow(reports[i].CreatedAtTicks, nowTicks))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True while a notice still applies; a non-positive expiry means "no expiry".</summary>
        public static bool IsNoticeActive(long expiresAtTicks, long nowTicks)
            => expiresAtTicks <= 0 || nowTicks < expiresAtTicks;

        /// <summary>
        /// Trims a report note and caps it at <see cref="MaxNoteLength"/>. Truncation happens here so
        /// the wire payload and the stored record are already the final value.
        /// </summary>
        public static string SanitizeNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return string.Empty;

            string trimmed = note.Trim();
            return trimmed.Length <= MaxNoteLength ? trimmed : trimmed.Substring(0, MaxNoteLength);
        }

        /// <summary>
        /// The plain-language rule explanation for a notice. Deliberately generic and
        /// evidence-free: it tells the player which rule their account broke, not how the server
        /// noticed. Unknown codes fall back to a neutral sentence rather than echoing the wire.
        /// </summary>
        public static string Explanation(string messageCode)
        {
            if (string.IsNullOrEmpty(messageCode))
                return "Your account was actioned by a moderator.";

            // The category is the *last* segment: TempBan and PermanentBan codes carry the
            // action in two words ("TEMP_BAN_SCAMMING"), so the first underscore is too early and
            // would leave "BAN_SCAMMING", which matches no category.
            int separator = messageCode.LastIndexOf('_');
            string category = separator >= 0 ? messageCode.Substring(separator + 1) : messageCode;

            switch (category)
            {
                case "MOVEMENT_ANOMALY":
                    return "Movement did not match the server's simulation.";
                case "OWNERSHIP_DUPLICATE":
                    return "An item ownership check failed.";
                case "COMBAT_ANOMALY":
                    return "Combat results did not match the server's calculations.";
                case "HARASSMENT":
                    return "A report about harassment was upheld.";
                case "SPAM":
                    return "A report about spam was upheld.";
                case "SCAMMING":
                    return "A report about scamming was upheld.";
                default:
                    return "Your account was actioned by a moderator.";
            }
        }

        /// <summary>A notice as one line, with its expiry when it has one.</summary>
        public static string Describe(ModerationNotice notice)
        {
            if (notice == null)
                return string.Empty;

            string action = notice.Action switch
            {
                ModerationAction.Warn => "Warning",
                ModerationAction.Kick => "Disconnected",
                ModerationAction.TempBan => "Suspended",
                ModerationAction.PermanentBan => "Banned",
                _ => "Notice",
            };

            string explanation = Explanation(notice.MessageCode);
            if (notice.ExpiresAtTicks <= 0)
                return $"{action}: {explanation}";

            DateTime until = new DateTime(notice.ExpiresAtTicks, DateTimeKind.Utc).ToLocalTime();
            return $"{action} until {until:yyyy-MM-dd HH:mm}: {explanation}";
        }

        private static bool IsWithinWindow(long createdAtTicks, long nowTicks)
        {
            long age = nowTicks - createdAtTicks;
            return age < ReportWindowTicks;
        }
    }
}
