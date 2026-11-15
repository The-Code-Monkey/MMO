using System.Threading;
using System.Threading.Tasks;

namespace MMO.Moderation
{
    /// <summary>
    /// Where reports and notices live. A repository interface keeps the moderation service off
    /// Nakama directly (the same rule every other system follows) and lets an EditMode test run the
    /// throttle against an in-memory fake.
    /// </summary>
    public interface IModerationRepository
    {
        /// <summary>The reporter's history, or null when they have never reported anyone.</summary>
        Task<ModerationReportRecord> LoadReportsAsync(string accountId, CancellationToken ct = default);

        /// <summary>Writes the reporter's history; a stale <c>StorageVersion</c> throws a conflict.</summary>
        Task SaveReportsAsync(ModerationReportRecord record, CancellationToken ct = default);

        /// <summary>An account's notices, or null when they have none.</summary>
        Task<ModerationNoticeRecord> LoadNoticesAsync(string accountId, CancellationToken ct = default);

        /// <summary>Writes an account's notices; a stale <c>StorageVersion</c> throws a conflict.</summary>
        Task SaveNoticesAsync(ModerationNoticeRecord record, CancellationToken ct = default);
    }
}
