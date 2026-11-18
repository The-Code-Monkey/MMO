using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Moderation;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The player-visible moderation surface: stable message codes and their evidence-free text,
    /// the rolling report throttle, and the service that persists reports/notices through the
    /// version-checked repository.
    /// </summary>
    public sealed class ModerationTests
    {
        private const long Now = 10_000_000_000L;

        [Test]
        public void MessageCode_IsStableForEachActionAndCategory()
        {
            Assert.AreEqual("WARN_MOVEMENT_ANOMALY",
                ModerationRules.MessageCode(ModerationAction.Warn, ModerationCategory.MovementAnomaly));
            Assert.AreEqual("TEMP_BAN_OWNERSHIP_DUPLICATE",
                ModerationRules.MessageCode(ModerationAction.TempBan, ModerationCategory.OwnershipDuplicate));
            Assert.AreEqual("PERM_BAN_OTHER",
                ModerationRules.MessageCode(ModerationAction.PermanentBan, ModerationCategory.Other));
        }

        [Test]
        public void Explanation_ExplainsTheRuleWithoutEvidence()
        {
            string text = ModerationRules.Explanation("KICK_COMBAT_ANOMALY");

            StringAssert.Contains("Combat", text);
            StringAssert.DoesNotContain("detector", text.ToLowerInvariant());
        }

        [Test]
        public void Explanation_HandlesTwoWordActionCodes()
        {
            // The bug this pins: splitting on the last underscore makes the category "ANOMALY",
            // which matches no case and falls back to the neutral sentence.
            string text = ModerationRules.Explanation("TEMP_BAN_COMBAT_ANOMALY");

            StringAssert.Contains("Combat", text);
        }

        [Test]
        public void Explain_UnknownCodeFallsBackNeutrally()
        {
            Assert.AreEqual("Your account was actioned by a moderator.",
                ModerationRules.Explanation("MADE_UP"));
            Assert.AreEqual("Your account was actioned by a moderator.",
                ModerationRules.Explanation(null));
        }

        [Test]
        public void CanReport_ThrottlesAtTheHourlyCap()
        {
            Assert.IsTrue(ModerationRules.CanReport(0, false));
            Assert.IsTrue(ModerationRules.CanReport(ModerationRules.MaxReportsPerHour - 1, false));
            Assert.IsFalse(ModerationRules.CanReport(ModerationRules.MaxReportsPerHour, false));
        }

        [Test]
        public void CanReport_RejectsADuplicateTarget()
        {
            Assert.IsFalse(ModerationRules.CanReport(0, true));
        }

        [Test]
        public void CountRecent_IgnoresReportsOutsideTheWindow()
        {
            var reports = new List<ModerationReportEntry>
            {
                new ModerationReportEntry { CreatedAtTicks = Now - 10 },
                new ModerationReportEntry { CreatedAtTicks = Now - ModerationRules.ReportWindowTicks - 1 },
            };

            Assert.AreEqual(1, ModerationRules.CountRecent(reports, Now));
        }

        [Test]
        public void HasRecentReportTo_MatchesOnlyTheSameTargetInWindow()
        {
            var reports = new List<ModerationReportEntry>
            {
                new ModerationReportEntry { TargetAccountId = "a", CreatedAtTicks = Now - 10 },
                new ModerationReportEntry
                {
                    TargetAccountId = "b",
                    CreatedAtTicks = Now - ModerationRules.ReportWindowTicks - 1,
                },
            };

            Assert.IsTrue(ModerationRules.HasRecentReportTo(reports, "a", Now));
            Assert.IsFalse(ModerationRules.HasRecentReportTo(reports, "b", Now));
            Assert.IsFalse(ModerationRules.HasRecentReportTo(reports, "c", Now));
        }

        [Test]
        public void SanitizeNote_TrimsAndTruncates()
        {
            Assert.AreEqual("hello", ModerationRules.SanitizeNote("  hello  "));
            Assert.AreEqual(string.Empty, ModerationRules.SanitizeNote("   "));
            Assert.AreEqual(ModerationRules.MaxNoteLength,
                ModerationRules.SanitizeNote(new string('x', ModerationRules.MaxNoteLength + 20)).Length);
        }

        [Test]
        public void IsNoticeActive_HonoursExpiry()
        {
            Assert.IsTrue(ModerationRules.IsNoticeActive(0, Now));
            Assert.IsTrue(ModerationRules.IsNoticeActive(Now + 1, Now));
            Assert.IsFalse(ModerationRules.IsNoticeActive(Now, Now));
        }

        [Test]
        public void Describe_IncludesTheExpiryWhenItHasOne()
        {
            ModerationNotice notice = ModerationNotice.Create(
                ModerationAction.TempBan, ModerationCategory.Scamming, Now, Now + TimeSpan.TicksPerDay);

            StringAssert.StartsWith("Suspended until", ModerationRules.Describe(notice));
            StringAssert.Contains("scamming", ModerationRules.Describe(notice).ToLowerInvariant());
        }

        [Test]
        public async Task ReportAsync_StoresANormalisedReport()
        {
            var repository = new FakeRepository();
            var service = new ModerationService(repository, () => Task.FromResult(true));

            ReportRule rule = await service.ReportAsync("me", "them", ModerationCategory.Spam, "  noisy  ", Now);

            Assert.AreEqual(ReportRule.Ok, rule);
            ModerationReportRecord stored = repository.Reports["me"];
            Assert.AreEqual(1, stored.Reports.Count);
            Assert.AreEqual("noisy", stored.Reports[0].Note);
        }

        [Test]
        public async Task ReportAsync_RefusesSelfTargets()
        {
            var service = new ModerationService(new FakeRepository(), () => Task.FromResult(true));

            Assert.AreEqual(ReportRule.InvalidTarget,
                await service.ReportAsync("me", "me", ModerationCategory.Other, null, Now));
            Assert.AreEqual(ReportRule.InvalidTarget,
                await service.ReportAsync("me", null, ModerationCategory.Other, null, Now));
        }

        [Test]
        public async Task ReportAsync_ThrottlesAfterTheCap()
        {
            var repository = new FakeRepository();
            var service = new ModerationService(repository, () => Task.FromResult(true));

            for (int i = 0; i < ModerationRules.MaxReportsPerHour; i++)
            {
                ReportRule rule = await service.ReportAsync(
                    "me", "target-" + i, ModerationCategory.Other, null, Now + i);
                Assert.AreEqual(ReportRule.Ok, rule);
            }

            Assert.AreEqual(ReportRule.RateLimited,
                await service.ReportAsync("me", "target-extra", ModerationCategory.Other, null, Now + 10));
        }

        [Test]
        public async Task ReportAsync_RefusesARepeatTarget()
        {
            var repository = new FakeRepository();
            var service = new ModerationService(repository, () => Task.FromResult(true));

            await service.ReportAsync("me", "them", ModerationCategory.Spam, null, Now);

            Assert.AreEqual(ReportRule.DuplicateTarget,
                await service.ReportAsync("me", "them", ModerationCategory.Spam, null, Now + 1));
        }

        [Test]
        public async Task ReportAsync_RetriesAfterAVersionConflict()
        {
            var repository = new FakeRepository { ConflictsOnNextReportSave = 1 };
            var service = new ModerationService(repository, () => Task.FromResult(true));

            Assert.AreEqual(ReportRule.Ok,
                await service.ReportAsync("me", "them", ModerationCategory.Other, null, Now));
        }

        [Test]
        public async Task ReportAsync_FailsWhenTheBackendIsDown()
        {
            var service = new ModerationService(new FakeRepository(), () => Task.FromResult(false));

            Assert.AreEqual(ReportRule.BackendUnavailable,
                await service.ReportAsync("me", "them", ModerationCategory.Other, null, Now));
        }

        [Test]
        public async Task NotifyAsync_StoresAndPrunesExpiredNotices()
        {
            var repository = new FakeRepository();
            var service = new ModerationService(repository, () => Task.FromResult(true));

            repository.Notices["them"] = new ModerationNoticeRecord
            {
                AccountId = "them",
                Notices = new List<ModerationNotice>
                {
                    ModerationNotice.Create(ModerationAction.Warn, ModerationCategory.Spam, Now - 100, Now - 1),
                },
            };

            ModerationNotice notice = await service.NotifyAsync(
                "them", ModerationAction.TempBan, ModerationCategory.Scamming, Now + 1000, Now);

            Assert.IsNotNull(notice);
            Assert.AreEqual(1, repository.Notices["them"].Notices.Count,
                "The expired warning is pruned when the new notice lands.");
            Assert.AreEqual(ModerationAction.TempBan, repository.Notices["them"].Notices[0].Action);
        }

        [Test]
        public async Task LoadActiveNoticesAsync_ReturnsOnlyLiveNotices()
        {
            var repository = new FakeRepository();
            var service = new ModerationService(repository, () => Task.FromResult(true));

            repository.Notices["me"] = new ModerationNoticeRecord
            {
                AccountId = "me",
                Notices = new List<ModerationNotice>
                {
                    ModerationNotice.Create(ModerationAction.Warn, ModerationCategory.Spam, Now, 0),
                    ModerationNotice.Create(ModerationAction.Kick, ModerationCategory.Spam, Now, Now - 1),
                },
            };

            List<ModerationNotice> active = await service.LoadActiveNoticesAsync("me", Now);

            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(ModerationAction.Warn, active[0].Action);
        }

        private sealed class FakeRepository : IModerationRepository
        {
            public readonly Dictionary<string, ModerationReportRecord> Reports = new();
            public readonly Dictionary<string, ModerationNoticeRecord> Notices = new();
            public int ConflictsOnNextReportSave;

            public Task<ModerationReportRecord> LoadReportsAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Reports.TryGetValue(accountId, out ModerationReportRecord record) ? Clone(record) : null);

            public Task SaveReportsAsync(ModerationReportRecord record, CancellationToken ct = default)
            {
                if (ConflictsOnNextReportSave > 0)
                {
                    ConflictsOnNextReportSave--;
                    throw new StorageVersionConflictException("test conflict");
                }

                Reports[record.AccountId] = Clone(record);
                return Task.CompletedTask;
            }

            public Task<ModerationNoticeRecord> LoadNoticesAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Notices.TryGetValue(accountId, out ModerationNoticeRecord record) ? Clone(record) : null);

            public Task SaveNoticesAsync(ModerationNoticeRecord record, CancellationToken ct = default)
            {
                Notices[record.AccountId] = Clone(record);
                return Task.CompletedTask;
            }

            private static T Clone<T>(T value) where T : class
                => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
