using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Moderation
{
    /// <summary>
    /// The server-side moderation surface: file a throttled report against an account, issue a
    /// player-visible notice, and read back the notices still in force.
    ///
    /// <para>
    /// Every mutation is a load → rule → version-checked write. A losing write retries with the
    /// freshly loaded record, which is how the hourly throttle stays honest when two reports for
    /// the same account race; a backend failure is reported to the caller, never silently treated
    /// as success. Notices are append-only; there is deliberately no unban/withdraw path here
    /// (appeals are governance, out of scope for the notification system).
    /// </para>
    /// </summary>
    public sealed class ModerationService
    {
        private readonly IModerationRepository _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly int _casRetries;

        public ModerationService(
            IModerationRepository repository,
            Func<Task<bool>> ensureConnected,
            int casRetries = 5)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _ensureConnected = ensureConnected;
            _casRetries = Mathf.Max(1, casRetries);
        }

        /// <summary>
        /// Files a report from <paramref name="reporterAccountId"/> about
        /// <paramref name="targetAccountId"/>, subject to the hourly cap and same-target dedupe.
        /// A report is stored, not acted on: a human decides, and the target is never told who
        /// reported them.
        /// </summary>
        public async Task<ReportRule> ReportAsync(
            string reporterAccountId,
            string targetAccountId,
            ModerationCategory category,
            string note,
            long nowTicks,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(reporterAccountId)
                || string.IsNullOrEmpty(targetAccountId)
                || reporterAccountId == targetAccountId)
            {
                return ReportRule.InvalidTarget;
            }

            if (_ensureConnected != null && !await _ensureConnected())
                return ReportRule.BackendUnavailable;

            for (int attempt = 0; attempt < _casRetries; attempt++)
            {
                try
                {
                    ModerationReportRecord record = await _repository.LoadReportsAsync(reporterAccountId, ct)
                        ?? new ModerationReportRecord { AccountId = reporterAccountId };

                    int recent = ModerationRules.CountRecent(record.Reports, nowTicks);
                    bool duplicate = ModerationRules.HasRecentReportTo(record.Reports, targetAccountId, nowTicks);

                    if (!ModerationRules.CanReport(recent, duplicate))
                        return duplicate ? ReportRule.DuplicateTarget : ReportRule.RateLimited;

                    record.Reports.Add(new ModerationReportEntry
                    {
                        TargetAccountId = targetAccountId,
                        Category = category,
                        Note = ModerationRules.SanitizeNote(note),
                        CreatedAtTicks = nowTicks,
                    });

                    await _repository.SaveReportsAsync(record, ct);
                    return ReportRule.Ok;
                }
                catch (StorageVersionConflictException)
                {
                    await Task.Delay(20 * (attempt + 1), ct);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogError($"[Moderation] Report from {reporterAccountId} failed: {e.Message}");
                    return ReportRule.BackendUnavailable;
                }
            }

            return ReportRule.Conflict;
        }

        /// <summary>
        /// Stores a notice for <paramref name="accountId"/> and returns it, or null when the backend
        /// refused the write. Callers present the returned notice through the transport (a
        /// TargetRpc at kick time, or the join-gate refusal on the next connection).
        /// </summary>
        public async Task<ModerationNotice> NotifyAsync(
            string accountId,
            ModerationAction action,
            ModerationCategory category,
            long expiresAtTicks,
            long nowTicks,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accountId))
                return null;

            if (_ensureConnected != null && !await _ensureConnected())
                return null;

            ModerationNotice notice = ModerationNotice.Create(action, category, nowTicks, expiresAtTicks);

            for (int attempt = 0; attempt < _casRetries; attempt++)
            {
                try
                {
                    ModerationNoticeRecord record = await _repository.LoadNoticesAsync(accountId, ct)
                        ?? new ModerationNoticeRecord { AccountId = accountId };

                    Prune(record, nowTicks);
                    record.Notices.Add(notice);

                    await _repository.SaveNoticesAsync(record, ct);
                    return notice;
                }
                catch (StorageVersionConflictException)
                {
                    await Task.Delay(20 * (attempt + 1), ct);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogError($"[Moderation] Notice for {accountId} failed: {e.Message}");
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// The notices still in force for an account, oldest first. Expired entries are dropped as a
        /// side effect of the read so a reconnect does not replay an old suspension forever; a
        /// failed cleanup is logged and the filtered view is still returned.
        /// </summary>
        public async Task<List<ModerationNotice>> LoadActiveNoticesAsync(
            string accountId,
            long nowTicks,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accountId))
                return new List<ModerationNotice>();

            ModerationNoticeRecord record = await _repository.LoadNoticesAsync(accountId, ct);
            if (record == null)
                return new List<ModerationNotice>();

            if (Prune(record, nowTicks))
            {
                try
                {
                    await _repository.SaveNoticesAsync(record, ct);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogWarning($"[Moderation] Could not prune notices for {accountId}: {e.Message}");
                }
            }

            return record.Notices;
        }

        private static bool Prune(ModerationNoticeRecord record, long nowTicks)
        {
            int removed = record.Notices.RemoveAll(
                notice => notice == null || !ModerationRules.IsNoticeActive(notice.ExpiresAtTicks, nowTicks));
            return removed > 0;
        }
    }
}
