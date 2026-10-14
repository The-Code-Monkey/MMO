using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class RaidInstanceServiceTests
    {
        private FakeSocialRepository _social;
        private FakeRaidRepo _raids;
        private RaidInstanceService _svc;

        [SetUp]
        public void SetUp()
        {
            _social = new FakeSocialRepository();
            _raids = new FakeRaidRepo();
            _svc = new RaidInstanceService(_social, _raids, casRetries: 5);
        }

        private static void SeedParty(FakeSocialRepository repo, string leader, int count)
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = leader };
            for (int i = 0; i < count; i++)
                party.Members.Add(new PartyMemberRecord { AccountId = i == 0 ? leader : ("m" + i), PlayerName = "p" + i });
            repo.Parties["p1"] = party;
            repo.Indexes[leader] = new SocialIndexRecord { AccountId = leader, PartyId = "p1" };
        }

        [Test]
        public async Task AllocateAsync_NotLeader_ReturnsNotLeader()
        {
            SeedParty(_social, "a", 5);
            _social.Indexes["b"] = new SocialIndexRecord { AccountId = "b", PartyId = "p1" };
            var res = await _svc.AllocateAsync("b", "z1");
            Assert.AreEqual(RaidRule.NotLeader, res.Rule);
        }

        [Test]
        public async Task AllocateAsync_TooSmall_ReturnsPartyTooSmall()
        {
            SeedParty(_social, "a", 4);
            var res = await _svc.AllocateAsync("a", "z1");
            Assert.AreEqual(RaidRule.PartyTooSmall, res.Rule);
        }

        [Test]
        public async Task AllocateAsync_Ok_CreatesInstanceAndSetsPointer()
        {
            SeedParty(_social, "a", 5);
            var res = await _svc.AllocateAsync("a", "z1", "10.0.0.4", 7770);
            Assert.AreEqual(RaidRule.Ok, res.Rule);
            Assert.IsNotNull(res.Instance);
            Assert.AreEqual("z1", res.Instance.HostZoneId);
            Assert.AreEqual("10.0.0.4", res.Instance.HostAddress);
            Assert.AreEqual((ushort)7770, res.Instance.HostPort);
            Assert.IsFalse(string.IsNullOrEmpty(_social.Parties["p1"].RaidInstanceId));
            Assert.AreEqual(1, _raids.Instances.Count);
        }

        [Test]
        public async Task AllocateAsync_RetriesAfterAVersionConflict()
        {
            SeedParty(_social, "a", 5);
            _social.ConflictOnNextPartySave = true;

            var res = await _svc.AllocateAsync("a", "z1");

            Assert.AreEqual(RaidRule.Ok, res.Rule);
            Assert.GreaterOrEqual(_social.PartySaveCalls, 2, "The lost CAS hint must be retried.");
            Assert.AreEqual(1, _raids.Instances.Count, "A retry must not leak a second instance record.");
        }

        [Test]
        public async Task CompleteAsync_ClearsPointerAndDeletesInstance()
        {
            SeedParty(_social, "a", 5);
            var alloc = await _svc.AllocateAsync("a", "z1");
            string instanceId = alloc.Instance.InstanceId;

            RaidRule rule = await _svc.CompleteAsync("a");

            Assert.AreEqual(RaidRule.Ok, rule);
            Assert.IsTrue(string.IsNullOrEmpty(_social.Parties["p1"].RaidInstanceId));
            Assert.IsFalse(_raids.Instances.ContainsKey(instanceId));
        }

        [Test]
        public async Task CompleteAsync_NoRaid_ReturnsRaidMissing()
        {
            SeedParty(_social, "a", 5);

            RaidRule rule = await _svc.CompleteAsync("a");

            Assert.AreEqual(RaidRule.RaidMissing, rule);
        }

        [Test]
        public async Task CompleteAsync_NonLeader_ReturnsNotLeader()
        {
            SeedParty(_social, "a", 5);
            await _svc.AllocateAsync("a", "z1");
            _social.Indexes["b"] = new SocialIndexRecord { AccountId = "b", PartyId = "p1" };

            RaidRule rule = await _svc.CompleteAsync("b");

            Assert.AreEqual(RaidRule.NotLeader, rule);
        }

        [Test]
        public async Task DisbandAndClearRaidAsync_DeletesTheInstance()
        {
            SeedParty(_social, "a", 5);
            var alloc = await _svc.AllocateAsync("a", "z1");

            await _svc.DisbandAndClearRaidAsync("p1");

            Assert.IsFalse(_raids.Instances.ContainsKey(alloc.Instance.InstanceId));
            Assert.IsTrue(string.IsNullOrEmpty(_social.Parties["p1"].RaidInstanceId));
        }

        [Test]
        public async Task AllocateAsync_WinningCas_EmitsRaidAllocatedNotification()
        {
            SeedParty(_social, "a", 5);

            var res = await _svc.AllocateAsync("a", "z1", "10.0.0.4", 7770);

            Assert.AreEqual(RaidRule.Ok, res.Rule);
            Assert.AreEqual(1, _social.Notifications.Count);
            SocialNotification notification = _social.Notifications[0];
            Assert.AreEqual(SocialNotificationKind.RaidAllocated, notification.Kind);
            Assert.AreEqual(res.Instance.InstanceId, notification.RaidId);
            Assert.AreEqual("p1", notification.PartyId);
            Assert.AreEqual("z1", notification.HostZoneId);
            Assert.AreEqual(5, notification.AccountIds.Count);
        }

        [Test]
        public async Task AllocateAsync_RuleFailure_EmitsNothing()
        {
            SeedParty(_social, "a", 4);

            var res = await _svc.AllocateAsync("a", "z1");

            Assert.AreEqual(RaidRule.PartyTooSmall, res.Rule);
            Assert.IsEmpty(_social.Notifications, "A rule failure must never reach the push.");
        }

        [Test]
        public async Task AllocateAsync_LostCasRetries_EmitsExactlyOneNotification()
        {
            SeedParty(_social, "a", 5);
            _social.ConflictOnNextPartySave = true;

            var res = await _svc.AllocateAsync("a", "z1");

            Assert.AreEqual(RaidRule.Ok, res.Rule);
            Assert.AreEqual(
                1,
                _social.Notifications.FindAll(n => n.Kind == SocialNotificationKind.RaidAllocated).Count,
                "Only the winning CAS may fire the push; a lost attempt must be silent.");
        }

        [Test]
        public async Task AllocateAsync_NotifyFailure_DoesNotFailTheAllocation()
        {
            SeedParty(_social, "a", 5);
            _social.ThrowOnNotify = true;

            var res = await _svc.AllocateAsync("a", "z1");

            Assert.AreEqual(RaidRule.Ok, res.Rule);
            Assert.AreEqual(1, _raids.Instances.Count);
        }

        [Test]
        public async Task AllocateAsync_CasExhausted_EmitsNothing()
        {
            SeedParty(_social, "a", 5);
            _social.ConflictEveryPartySave = true;

            var res = await _svc.AllocateAsync("a", "z1");

            Assert.AreEqual(RaidRule.CasExhausted, res.Rule);
            Assert.IsEmpty(_social.Notifications, "A never-winning CAS must never push.");
            Assert.AreEqual(0, _raids.Instances.Count, "The orphan instance must be cleaned up.");
        }

        [Test]
        public async Task CompleteAsync_WinningCas_EmitsRaidCompletedNotification()
        {
            SeedParty(_social, "a", 5);
            var alloc = await _svc.AllocateAsync("a", "z1");
            _social.Notifications.Clear();

            RaidRule rule = await _svc.CompleteAsync("a");

            Assert.AreEqual(RaidRule.Ok, rule);
            Assert.AreEqual(1, _social.Notifications.Count);
            Assert.AreEqual(SocialNotificationKind.RaidCompleted, _social.Notifications[0].Kind);
            Assert.AreEqual(alloc.Instance.InstanceId, _social.Notifications[0].RaidId);
        }

        [Test]
        public async Task CompleteAsync_RuleFailure_EmitsNothing()
        {
            SeedParty(_social, "a", 5);

            RaidRule rule = await _svc.CompleteAsync("a");

            Assert.AreEqual(RaidRule.RaidMissing, rule);
            Assert.IsEmpty(_social.Notifications);
        }

        private sealed class FakeSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly List<SocialNotification> Notifications = new List<SocialNotification>();
            public bool ConflictOnNextPartySave;
            public bool ConflictEveryPartySave;
            public bool ThrowOnNotify;
            public int PartySaveCalls;
            private int _v;

            public Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
            {
                if (Parties.TryGetValue(partyId, out var p))
                {
                    var c = JsonUtility.FromJson<PartyRecord>(JsonUtility.ToJson(p));
                    c.StorageVersion = p.StorageVersion;
                    return Task.FromResult(c);
                }
                return Task.FromResult<PartyRecord>(null);
            }
            public Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
            {
                PartySaveCalls++;
                if (ConflictEveryPartySave)
                    return Task.FromException(new StorageVersionConflictException("x"));

                if (ConflictOnNextPartySave)
                {
                    ConflictOnNextPartySave = false;
                    return Task.FromException(new StorageVersionConflictException("x"));
                }
                var clone = JsonUtility.FromJson<PartyRecord>(JsonUtility.ToJson(record));
                clone.StorageVersion = (_v++).ToString();
                Parties[record.PartyId] = clone;
                return Task.CompletedTask;
            }
            public Task DeletePartyAsync(string partyId, CancellationToken ct = default) { Parties.Remove(partyId); return Task.CompletedTask; }
            public Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
            {
                if (Indexes.TryGetValue(accountId, out var i)) { var c = JsonUtility.FromJson<SocialIndexRecord>(JsonUtility.ToJson(i)); c.StorageVersion = i.StorageVersion; return Task.FromResult(c); }
                return Task.FromResult<SocialIndexRecord>(null);
            }
            public Task<List<SocialIndexRecord>> LoadIndexesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default) { var list = new List<SocialIndexRecord>(); foreach (var a in accountIds) if (Indexes.TryGetValue(a, out var x)) { var c = JsonUtility.FromJson<SocialIndexRecord>(JsonUtility.ToJson(x)); c.StorageVersion = x.StorageVersion; list.Add(c); } return Task.FromResult(list); }
            public Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default) { return Task.CompletedTask; }
            public Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default) => Task.FromResult<PresenceRecord>(null);
            public Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default) => Task.CompletedTask;
            public Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default) => Task.FromResult<string>(null);
            public Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default) => Task.CompletedTask;
            public Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default) => Task.FromResult<GuildRecord>(null);
            public Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default) => Task.CompletedTask;
            public Task DeleteGuildAsync(string guildId, CancellationToken ct = default) => Task.CompletedTask;
            public Task<FriendListRecord> LoadFriendsAsync(string accountId, CancellationToken ct = default) => Task.FromResult<FriendListRecord>(null);
            public Task SaveFriendsAsync(FriendListRecord record, CancellationToken ct = default) => Task.CompletedTask;
            public Task<GuildBankRecord> LoadGuildBankAsync(string guildId, CancellationToken ct = default) => Task.FromResult<GuildBankRecord>(null);
            public Task SaveGuildBankAsync(GuildBankRecord record, CancellationToken ct = default) => Task.CompletedTask;
            public Task<GuildBankLedgerRecord> LoadGuildBankLedgerAsync(string guildId, CancellationToken ct = default) => Task.FromResult<GuildBankLedgerRecord>(null);
            public Task SaveGuildBankLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default) => Task.CompletedTask;
            public Task<List<PresenceRecord>> LoadPresencesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default) => Task.FromResult(new List<PresenceRecord>());
            public Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default)
            {
                if (ThrowOnNotify)
                    return Task.FromException(new InvalidOperationException("notify down"));

                Notifications.Add(notification);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeRaidRepo : IRaidInstanceRepository
        {
            public readonly Dictionary<string, RaidInstanceRecord> Instances = new Dictionary<string, RaidInstanceRecord>();
            private int _v;
            public Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            {
                if (Instances.TryGetValue(instanceId, out var r)) { var c = JsonUtility.FromJson<RaidInstanceRecord>(JsonUtility.ToJson(r)); c.StorageVersion = r.StorageVersion; return Task.FromResult(c); }
                return Task.FromResult<RaidInstanceRecord>(null);
            }
            public Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default)
            {
                var clone = JsonUtility.FromJson<RaidInstanceRecord>(JsonUtility.ToJson(record));
                clone.StorageVersion = (_v++).ToString();
                Instances[record.InstanceId] = clone;
                return Task.CompletedTask;
            }
            public Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default) { Instances.Remove(instanceId); return Task.CompletedTask; }
        }
    }
}
