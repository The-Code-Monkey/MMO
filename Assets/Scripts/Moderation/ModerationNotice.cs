using System;
using System.Collections.Generic;

namespace MMO.Moderation
{
    /// <summary>
    /// One player-visible moderation outcome. It carries a stable code, an action, and an optional
    /// expiry — never the incident that triggered it. Instances are serializable so the same shape
    /// travels over the TargetRpc JSON and lives in the notice record.
    /// </summary>
    [Serializable]
    public sealed class ModerationNotice
    {
        public string NoticeId;
        public string MessageCode;
        public ModerationAction Action;
        public long IssuedAtTicks;
        public long ExpiresAtTicks;

        public static ModerationNotice Create(
            ModerationAction action,
            ModerationCategory category,
            long issuedAtTicks,
            long expiresAtTicks)
        {
            return new ModerationNotice
            {
                NoticeId = Guid.NewGuid().ToString("N"),
                MessageCode = ModerationRules.MessageCode(action, category),
                Action = action,
                IssuedAtTicks = issuedAtTicks,
                ExpiresAtTicks = expiresAtTicks,
            };
        }
    }

    /// <summary>One account's stored notices, the object a banned/warned client reads on join.</summary>
    [Serializable]
    public sealed class ModerationNoticeRecord
    {
        public const string Collection = "moderation_notices";

        public string AccountId;
        public List<ModerationNotice> Notices = new List<ModerationNotice>();
        public string StorageVersion;
    }

    /// <summary>One filed report. Notes are context for a human reviewer, never broadcast.</summary>
    [Serializable]
    public sealed class ModerationReportEntry
    {
        public string TargetAccountId;
        public ModerationCategory Category;
        public string Note;
        public long CreatedAtTicks;
    }

    /// <summary>
    /// One reporter's report history. The hourly throttle reads this record, which is why the
    /// reporter (not the target) owns it.
    /// </summary>
    [Serializable]
    public sealed class ModerationReportRecord
    {
        public const string Collection = "moderation_reports";

        public string AccountId;
        public List<ModerationReportEntry> Reports = new List<ModerationReportEntry>();
        public string StorageVersion;
    }
}
