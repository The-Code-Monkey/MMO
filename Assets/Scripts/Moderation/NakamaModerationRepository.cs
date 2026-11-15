using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Moderation
{
    /// <summary>
    /// The production <see cref="IModerationRepository"/>: two Nakama storage collections under the
    /// shared service account, written with the same optimistic version check every other record
    /// uses. A missing object is null, not an error — a first-time reporter has no history yet.
    /// </summary>
    public sealed class NakamaModerationRepository : IModerationRepository
    {
        private readonly NakamaRestClient _rest;

        public NakamaModerationRepository(NakamaRestClient rest)
            => _rest = rest ?? throw new ArgumentNullException(nameof(rest));

        public async Task<ModerationReportRecord> LoadReportsAsync(string accountId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accountId))
                return null;

            StorageObjectData data = await _rest.ReadObjectAsync(
                ModerationReportRecord.Collection, accountId, ct);
            return Read<ModerationReportRecord>(data, record => record.AccountId = accountId);
        }

        public async Task SaveReportsAsync(ModerationReportRecord record, CancellationToken ct = default)
        {
            if (record == null || string.IsNullOrEmpty(record.AccountId))
                throw new ArgumentException("A report record needs an account id.", nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                ModerationReportRecord.Collection,
                record.AccountId,
                JsonUtility.ToJson(record),
                record.StorageVersion,
                ct);
        }

        public async Task<ModerationNoticeRecord> LoadNoticesAsync(string accountId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(accountId))
                return null;

            StorageObjectData data = await _rest.ReadObjectAsync(
                ModerationNoticeRecord.Collection, accountId, ct);
            return Read<ModerationNoticeRecord>(data, record => record.AccountId = accountId);
        }

        public async Task SaveNoticesAsync(ModerationNoticeRecord record, CancellationToken ct = default)
        {
            if (record == null || string.IsNullOrEmpty(record.AccountId))
                throw new ArgumentException("A notice record needs an account id.", nameof(record));

            record.StorageVersion = await _rest.WriteObjectAsync(
                ModerationNoticeRecord.Collection,
                record.AccountId,
                JsonUtility.ToJson(record),
                record.StorageVersion,
                ct);
        }

        private static T Read<T>(StorageObjectData data, Action<T> backfill) where T : class
        {
            if (data == null || string.IsNullOrEmpty(data.Value))
                return null;

            T record;
            try
            {
                record = JsonUtility.FromJson<T>(data.Value);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (record == null)
                return null;

            backfill(record);
            if (record is ModerationReportRecord reports)
                reports.StorageVersion = data.Version;
            else if (record is ModerationNoticeRecord notices)
                notices.StorageVersion = data.Version;

            return record;
        }
    }
}
