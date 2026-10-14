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
    /// <summary>
    /// Multi-party raid rosters (#54): a raid groups several parties pointing at one shared
    /// <see cref="RaidInstanceRecord"/>. These drive <see cref="RaidInstanceService"/> against an
    /// in-memory repository with injectable CAS conflicts, proving the join/leave/promote/partial-
    /// teardown rules and the ordered cross-object writes without a backend.
    /// </summary>
    public sealed class MultiPartyRaidServiceTests
    {
        private FakeSocialRepository _social;
        private FakeRaidRepo _raids;
        private FakeZoneRegistryRepository _zones;
        private RaidInstanceService _svc;

        [SetUp]
        public void SetUp()
        {
            _social = new FakeSocialRepository();
            _raids = new FakeRaidRepo();
            _zones = new FakeZoneRegistryRepository();
            _zones.Zones["z1"] = new ZoneRegistryRecord
            {
                ServerId = "z1",
                Host = "10.0.0.1",
                Port = 7770,
                ZoneTemplateId = "raid",
                MaxInstances = 10,
                ActiveInstances = 0,
                HeartbeatAtTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks,
            };
            _svc = new RaidInstanceService(_social, _raids, _zones, casRetries: 5);
        }

        #region Helpers

        private static string[] Accounts(string prefix, int count)
        {
            var accounts = new string[count];
            for (int i = 0; i < count; i++)
                accounts[i] = prefix + i;
            return accounts;
        }

        private static PartyRecord MemberParty(string partyId, string leaderId, params string[] accounts)
        {
            var party = new PartyRecord { PartyId = partyId, LeaderId = leaderId };
            foreach (string account in accounts)
                party.Members.Add(new PartyMemberRecord { AccountId = account, PlayerName = account });
            return party;
        }

        private void SeedParty(PartyRecord party)
        {
            _social.Parties[party.PartyId] = party;
            foreach (PartyMemberRecord member in party.Members)
                _social.Indexes[member.AccountId] = new SocialIndexRecord { AccountId = member.AccountId, PartyId = party.PartyId };
        }

        private async Task<RaidInstanceRecord> SeedRaid()
        {
            SeedParty(MemberParty("p1", "a0", Accounts("a", 20)));
            var (rule, instance) = await _svc.AllocateAsync("a0");
            Assert.AreEqual(RaidRule.Ok, rule);
            return instance;
        }

        #endregion

        #region Join

        [Test]
        public async Task JoinAsync_AddsPartyToTheRosterAndPointsItAtTheRaid()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));

            RaidRule rule = await _svc.JoinAsync("b0", raid.InstanceId);

            Assert.AreEqual(RaidRule.Ok, rule);
            RaidInstanceRecord stored = _raids.Instances[raid.InstanceId];
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, stored.MemberPartyIds);
            Assert.AreEqual(raid.InstanceId, _social.Parties["p2"].RaidInstanceId);
            Assert.AreEqual(25, stored.Members.Count, "The derived snapshot must span both parties.");
        }

        [Test]
        public async Task JoinAsync_NotPartyLeader_ReturnsNotLeader()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));

            RaidRule rule = await _svc.JoinAsync("b1", raid.InstanceId);

            Assert.AreEqual(RaidRule.NotLeader, rule);
            CollectionAssert.AreEqual(new[] { "p1" }, _raids.Instances[raid.InstanceId].MemberPartyIds);
        }

        [Test]
        public async Task JoinAsync_WouldExceedMaxRaidSize_ReturnsRaidFull()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 6)));

            RaidRule rule = await _svc.JoinAsync("b0", raid.InstanceId);

            Assert.AreEqual(RaidRule.RaidFull, rule);
            CollectionAssert.AreEqual(new[] { "p1" }, _raids.Instances[raid.InstanceId].MemberPartyIds);
            Assert.IsNull(_social.Parties["p2"].RaidInstanceId);
        }

        [Test]
        public async Task JoinAsync_RaidMissing_ReturnsNoRaid()
        {
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));

            Assert.AreEqual(RaidRule.NoRaid, await _svc.JoinAsync("b0", "ghost"));
        }

        [Test]
        public async Task JoinAsync_PointerSaveConflicts_RetryRepushesPointerWithoutDuplicating()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            _social.ConflictOnNextPartySave = true;

            RaidRule rule = await _svc.JoinAsync("b0", raid.InstanceId);

            Assert.AreEqual(RaidRule.Ok, rule);
            RaidInstanceRecord stored = _raids.Instances[raid.InstanceId];
            Assert.AreEqual(1, stored.MemberPartyIds.FindAll(id => id == "p2").Count);
            Assert.AreEqual(raid.InstanceId, _social.Parties["p2"].RaidInstanceId);
        }

        [Test]
        public async Task JoinAsync_LosingRaceForTheLastSeat_ReportsRaidFull()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            SeedParty(MemberParty("p3", "c0", Accounts("c", 5)));

            // The stored raid loses our CAS because a racing join appended p3 first (20 + 5 = 25).
            _raids.ConflictOnNextSave = true;
            _raids.BeforeConflict = _ => _raids.Instances[raid.InstanceId].MemberPartyIds.Add("p3");

            RaidRule rule = await _svc.JoinAsync("b0", raid.InstanceId);

            Assert.AreEqual(RaidRule.RaidFull, rule);
            Assert.IsNull(_social.Parties["p2"].RaidInstanceId, "The loser must not point at the raid.");
        }

        #endregion

        #region Leave / partial teardown

        [Test]
        public async Task LeaveAsync_OneOfTwoParties_KeepsTheRaidAlive()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);

            RaidRule rule = await _svc.LeaveAsync("b0");

            Assert.AreEqual(RaidRule.Ok, rule);
            RaidInstanceRecord stored = _raids.Instances[raid.InstanceId];
            CollectionAssert.AreEqual(new[] { "p1" }, stored.MemberPartyIds);
            Assert.IsNull(_social.Parties["p2"].RaidInstanceId);
        }

        [Test]
        public async Task LeaveAsync_SeedParty_ReseedsTheRaidAndLeadership()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);

            RaidRule rule = await _svc.LeaveAsync("a0");

            Assert.AreEqual(RaidRule.Ok, rule);
            RaidInstanceRecord stored = _raids.Instances[raid.InstanceId];
            Assert.AreEqual("p2", stored.SeedPartyId);
            Assert.AreEqual("b0", stored.LeaderId);
            CollectionAssert.AreEqual(new[] { "p2" }, stored.MemberPartyIds);
        }

        [Test]
        public async Task LeaveAsync_LastParty_DisbandsAndClearsEveryPointer()
        {
            RaidInstanceRecord raid = await SeedRaid();

            RaidRule rule = await _svc.LeaveAsync("a0");

            Assert.AreEqual(RaidRule.Ok, rule);
            Assert.IsFalse(_raids.Instances.ContainsKey(raid.InstanceId));
            Assert.IsNull(_social.Parties["p1"].RaidInstanceId);
        }

        [Test]
        public async Task LeaveAsync_NonLeader_ReturnsNotLeader()
        {
            RaidInstanceRecord raid = await SeedRaid();

            Assert.AreEqual(RaidRule.NotLeader, await _svc.LeaveAsync("a1"));
        }

        [Test]
        public async Task RemovePartyFromRaidAsync_SiblingRemains_PartialTeardown()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);

            // p2's last member left, so the hub is about to delete p2; the raid must survive.
            await _svc.RemovePartyFromRaidAsync("p2");

            RaidInstanceRecord stored = _raids.Instances[raid.InstanceId];
            Assert.IsNotNull(stored);
            CollectionAssert.AreEqual(new[] { "p1" }, stored.MemberPartyIds);
            Assert.IsNull(_social.Parties["p2"].RaidInstanceId);
        }

        #endregion

        #region Promote

        [Test]
        public async Task PromoteLeaderAsync_LeaderHandsOver_Succeeds()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);

            RaidRule rule = await _svc.PromoteLeaderAsync("a0", "b0");

            Assert.AreEqual(RaidRule.Ok, rule);
            Assert.AreEqual("b0", _raids.Instances[raid.InstanceId].LeaderId);
        }

        [Test]
        public async Task PromoteLeaderAsync_NonLeader_ReturnsNotLeader()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);

            Assert.AreEqual(RaidRule.NotLeader, await _svc.PromoteLeaderAsync("b0", "a0"));
        }

        [Test]
        public async Task CompleteAsync_TransferredLeader_CompletesAndTearsDown()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);
            await _svc.PromoteLeaderAsync("a0", "b0");

            RaidRule rule = await _svc.CompleteAsync("b0");

            Assert.AreEqual(RaidRule.Ok, rule);
            Assert.IsFalse(_raids.Instances.ContainsKey(raid.InstanceId));
            Assert.IsNull(_social.Parties["p1"].RaidInstanceId);
            Assert.IsNull(_social.Parties["p2"].RaidInstanceId);
        }

        [Test]
        public async Task CompleteAsync_NonRaidLeaderAfterTransfer_ReturnsNotLeader()
        {
            RaidInstanceRecord raid = await SeedRaid();
            SeedParty(MemberParty("p2", "b0", Accounts("b", 5)));
            await _svc.JoinAsync("b0", raid.InstanceId);
            await _svc.PromoteLeaderAsync("a0", "b0");

            // The seed party's leader is no longer the raid leader.
            Assert.AreEqual(RaidRule.NotLeader, await _svc.CompleteAsync("a0"));
        }

        #endregion

        #region Fakes

        private sealed class FakeSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly List<SocialNotification> Notifications = new List<SocialNotification>();
            public bool ConflictOnNextPartySave;
            public int PartySaveCalls;
            private int _v;

            public Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
            {
                if (Parties.TryGetValue(partyId, out PartyRecord p))
                {
                    var clone = JsonUtility.FromJson<PartyRecord>(JsonUtility.ToJson(p));
                    clone.StorageVersion = p.StorageVersion;
                    return Task.FromResult(clone);
                }

                return Task.FromResult<PartyRecord>(null);
            }

            public Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
            {
                PartySaveCalls++;
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

            public Task DeletePartyAsync(string partyId, CancellationToken ct = default)
            {
                Parties.Remove(partyId);
                return Task.CompletedTask;
            }

            public Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Indexes.TryGetValue(accountId, out SocialIndexRecord i) ? i : null);

            public Task<List<SocialIndexRecord>> LoadIndexesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default)
            {
                var list = new List<SocialIndexRecord>();
                foreach (string accountId in accountIds)
                    if (Indexes.TryGetValue(accountId, out SocialIndexRecord i))
                        list.Add(i);
                return Task.FromResult(list);
            }

            public Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default) => Task.CompletedTask;
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
                Notifications.Add(notification);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeRaidRepo : IRaidInstanceRepository
        {
            public readonly Dictionary<string, RaidInstanceRecord> Instances = new Dictionary<string, RaidInstanceRecord>();
            public bool ConflictOnNextSave;
            public Action<RaidInstanceRecord> BeforeConflict;
            private int _v;

            public Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            {
                if (Instances.TryGetValue(instanceId, out RaidInstanceRecord r))
                {
                    var clone = JsonUtility.FromJson<RaidInstanceRecord>(JsonUtility.ToJson(r));
                    clone.StorageVersion = r.StorageVersion;
                    return Task.FromResult(clone);
                }

                return Task.FromResult<RaidInstanceRecord>(null);
            }

            public Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default)
            {
                if (ConflictOnNextSave)
                {
                    ConflictOnNextSave = false;
                    BeforeConflict?.Invoke(record);
                    BeforeConflict = null;
                    return Task.FromException(new StorageVersionConflictException("x"));
                }

                var clone = JsonUtility.FromJson<RaidInstanceRecord>(JsonUtility.ToJson(record));
                clone.StorageVersion = (_v++).ToString();
                Instances[record.InstanceId] = clone;
                return Task.CompletedTask;
            }

            public Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            {
                Instances.Remove(instanceId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeZoneRegistryRepository : IZoneRegistryRepository
        {
            public readonly Dictionary<string, ZoneRegistryRecord> Zones = new Dictionary<string, ZoneRegistryRecord>();
            private int _v;

            public Task<List<ZoneRegistryRecord>> LoadZonesAsync(CancellationToken ct = default)
            {
                var list = new List<ZoneRegistryRecord>(Zones.Count);
                foreach (ZoneRegistryRecord z in Zones.Values)
                {
                    var clone = JsonUtility.FromJson<ZoneRegistryRecord>(JsonUtility.ToJson(z));
                    clone.StorageVersion = z.StorageVersion;
                    list.Add(clone);
                }
                return Task.FromResult(list);
            }

            public Task SaveZoneAsync(ZoneRegistryRecord record, CancellationToken ct = default)
            {
                var clone = JsonUtility.FromJson<ZoneRegistryRecord>(JsonUtility.ToJson(record));
                clone.StorageVersion = (_v++).ToString();
                Zones[record.ServerId] = clone;
                return Task.CompletedTask;
            }

            public Task DeleteZoneAsync(string serverId, CancellationToken ct = default)
            {
                Zones.Remove(serverId);
                return Task.CompletedTask;
            }
        }

        #endregion
    }
}
