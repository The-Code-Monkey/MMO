using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The social hub in isolation: a service on the scene and an in-memory repository, with
    /// <see cref="SocialHubService.Configure"/> proving a fake needs no backend. Mutations, the CAS
    /// retry and one reconcile pass are all driven directly rather than through FishNet, which is
    /// the half of the issue that can be proven without a socket (AGENTS.md: test the server logic
    /// with mock state before the full RPC flow).
    /// </summary>
    public sealed class SocialHubServiceTests
    {
        private GameObject _go;
        private SocialHubService _hub;
        private FakeSocialRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SocialHubUnderTest");
            _hub = _go.AddComponent<SocialHubService>();
            _repo = new FakeSocialRepository();
            _hub.Configure(_repo);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private static void SeedOnline(FakeSocialRepository repo, string accountId, string name, string zoneId = "z1")
        {
            repo.Presence[accountId] = PresenceRecord.CreateOnline(accountId, name, zoneId, DateTimeOffset.UtcNow);
            repo.Names[name.ToLowerInvariant()] = accountId;
        }

        #region Party mutations

        [Test]
        public async Task InviteAsync_UnknownName_ReportsTargetNotFound()
        {
            PartyRule result = await _hub.InviteAsync("inviter", "ghost");

            Assert.AreEqual(PartyRule.TargetNotFound, result);
        }

        [Test]
        public async Task InviteAsync_OfflineTarget_ReportsTargetOffline()
        {
            _repo.Names["bob"] = "b";

            PartyRule result = await _hub.InviteAsync("inviter", "bob");

            Assert.AreEqual(PartyRule.TargetOffline, result);
        }

        [Test]
        public async Task InviteAsync_CreatesAParty_AndStoresThePendingInvite()
        {
            SeedOnline(_repo, "a", "alice");
            SeedOnline(_repo, "b", "bob");

            PartyRule result = await _hub.InviteAsync("a", "bob");

            Assert.AreEqual(PartyRule.Ok, result);
            Assert.AreEqual(1, _repo.Parties.Count, "Inviting the first member creates exactly one party.");
            Assert.IsTrue(_repo.Indexes.ContainsKey("a"), "The inviter's index points at the new party.");
            Assert.IsFalse(string.IsNullOrEmpty(_repo.Indexes["a"].PartyId));
            Assert.AreEqual("alice", _repo.Indexes["b"].PendingPartyInvite.FromPlayerName);
            Assert.AreEqual(_repo.Indexes["a"].PartyId, _repo.Indexes["b"].PendingPartyInvite.PartyId);
            Assert.IsNull(_repo.Indexes["b"].PartyId, "The target is still free until they accept.");
        }

        [Test]
        public async Task InviteAsync_ExistingParty_ReusesItInsteadOfCreatingASecond()
        {
            SeedOnline(_repo, "a", "alice");
            SeedOnline(_repo, "b", "bob");
            SeedOnline(_repo, "c", "cara");
            await _hub.InviteAsync("a", "bob");
            await _hub.AcceptAsync("b");

            PartyRule result = await _hub.InviteAsync("a", "cara");

            Assert.AreEqual(PartyRule.Ok, result);
            Assert.AreEqual(1, _repo.Parties.Count, "The leader's existing party must be reused.");
        }

        [Test]
        public async Task AcceptAsync_AddsTheMember_AndClearsTheInvite()
        {
            SeedOnline(_repo, "a", "alice");
            SeedOnline(_repo, "b", "bob");
            await _hub.InviteAsync("a", "bob");

            PartyRule result = await _hub.AcceptAsync("b");

            Assert.AreEqual(PartyRule.Ok, result);
            string partyId = _repo.Indexes["a"].PartyId;
            Assert.IsTrue(PartyRules.IsMember(_repo.Parties[partyId], "b"));
            Assert.AreEqual(partyId, _repo.Indexes["b"].PartyId);
            Assert.IsTrue(_repo.Indexes["b"].PendingPartyInvite.PartyId == null, "The invite is consumed.");
        }

        [Test]
        public async Task AcceptAsync_OnAFullParty_ReportsPartyFull()
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = "a" };
            for (int i = 0; i < PartyRules.MaxMembers; i++)
                party.Members.Add(new PartyMemberRecord { AccountId = "m" + i, JoinedAtTicks = i });

            _repo.Parties["p1"] = party;
            _repo.Indexes["newcomer"] = new SocialIndexRecord
            {
                AccountId = "newcomer",
                PendingPartyInvite = new PendingPartyInvite
                {
                    PartyId = "p1",
                    ExpiresAtTicks = DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.Ticks,
                },
            };

            PartyRule result = await _hub.AcceptAsync("newcomer");

            Assert.AreEqual(PartyRule.PartyFull, result);
            Assert.AreEqual(PartyRules.MaxMembers, _repo.Parties["p1"].Members.Count);
        }

        [Test]
        public async Task LeaveAsync_LastMember_DisbandsTheParty()
        {
            _repo.Parties["p1"] = new PartyRecord
            {
                PartyId = "p1",
                LeaderId = "a",
                Members = new List<PartyMemberRecord> { new PartyMemberRecord { AccountId = "a" } },
            };
            _repo.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };

            PartyRule result = await _hub.LeaveAsync("a");

            Assert.AreEqual(PartyRule.Ok, result);
            Assert.IsFalse(_repo.Parties.ContainsKey("p1"), "The last member leaving disbands the party.");
            Assert.IsNull(_repo.Indexes["a"].PartyId);
        }

        [Test]
        public async Task KickAsync_LeaderRemovesTheTarget_AndClearsTheirIndex()
        {
            _repo.Parties["p1"] = new PartyRecord
            {
                PartyId = "p1",
                LeaderId = "a",
                Members = new List<PartyMemberRecord>
                {
                    new PartyMemberRecord { AccountId = "a" },
                    new PartyMemberRecord { AccountId = "b" },
                },
            };
            _repo.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _repo.Indexes["b"] = new SocialIndexRecord { AccountId = "b", PartyId = "p1" };

            PartyRule result = await _hub.KickAsync("a", "b");

            Assert.AreEqual(PartyRule.Ok, result);
            Assert.IsFalse(PartyRules.IsMember(_repo.Parties["p1"], "b"));
            Assert.IsNull(_repo.Indexes["b"].PartyId);
        }

        [Test]
        public async Task InviteAsync_RetriesOnceAfterAVersionConflict()
        {
            SeedOnline(_repo, "a", "alice");
            SeedOnline(_repo, "b", "bob");
            _repo.ConflictOnNextIndexSave = true;

            PartyRule result = await _hub.InviteAsync("a", "bob");

            Assert.AreEqual(PartyRule.Ok, result);
            Assert.AreEqual(1, _repo.Parties.Count, "A retry must reuse the party the first attempt created.");
            Assert.GreaterOrEqual(_repo.IndexSaveCalls, 3);
        }

        #endregion

        #region Guild mutations

        [Test]
        public async Task CreateGuildAsync_CreatesTheGuild_AndIndexesTheLeader()
        {
            GuildRule result = await _hub.CreateGuildAsync("a", "Raiders");

            Assert.AreEqual(GuildRule.Ok, result);
            Assert.AreEqual(1, _repo.Guilds.Count);
            string guildId = _repo.Indexes["a"].GuildId;
            Assert.IsFalse(string.IsNullOrEmpty(guildId));
            Assert.IsTrue(GuildRules.IsMember(_repo.Guilds[guildId], "a"));
            Assert.AreEqual(GuildRank.Leader, GuildRules.RankOf(GuildRules.FindMember(_repo.Guilds[guildId], "a")));
        }

        [Test]
        public async Task CreateGuildAsync_WhenAlreadyInAGuild_IsRefused()
        {
            await _hub.CreateGuildAsync("a", "Raiders");

            GuildRule result = await _hub.CreateGuildAsync("a", "Second");

            Assert.AreEqual(GuildRule.AlreadyInGuild, result);
            Assert.AreEqual(1, _repo.Guilds.Count);
        }

        [Test]
        public async Task InviteToGuildAsync_LeaderInvites_StoresThePendingInvite()
        {
            SeedOnline(_repo, "a", "alice");
            SeedOnline(_repo, "b", "bob");
            await _hub.CreateGuildAsync("a", "Raiders");

            GuildRule result = await _hub.InviteToGuildAsync("a", "bob");

            Assert.AreEqual(GuildRule.Ok, result);
            Assert.AreEqual("alice", _repo.Indexes["b"].PendingGuildInvite.FromPlayerName);
            Assert.AreEqual(_repo.Indexes["a"].GuildId, _repo.Indexes["b"].PendingGuildInvite.GuildId);
        }

        [Test]
        public async Task AcceptGuildInviteAsync_AddsTheMember()
        {
            SeedOnline(_repo, "b", "bob");
            await _hub.CreateGuildAsync("a", "Raiders");
            await _hub.InviteToGuildAsync("a", "bob");

            GuildRule result = await _hub.AcceptGuildInviteAsync("b");

            Assert.AreEqual(GuildRule.Ok, result);
            string guildId = _repo.Indexes["a"].GuildId;
            Assert.IsTrue(GuildRules.IsMember(_repo.Guilds[guildId], "b"));
            Assert.AreEqual(guildId, _repo.Indexes["b"].GuildId);
        }

        [Test]
        public async Task LeaveGuildAsync_LastMember_Disbands()
        {
            await _hub.CreateGuildAsync("a", "Raiders");
            string guildId = _repo.Indexes["a"].GuildId;

            GuildRule result = await _hub.LeaveGuildAsync("a");

            Assert.AreEqual(GuildRule.Ok, result);
            Assert.IsFalse(_repo.Guilds.ContainsKey(guildId));
            Assert.IsNull(_repo.Indexes["a"].GuildId);
        }

        #endregion

        #region Reconcile

        [Test]
        public async Task SyncAsync_RaisesPartyChangedForAHostedMember()
        {
            _repo.Parties["p1"] = new PartyRecord
            {
                PartyId = "p1",
                LeaderId = "a",
                Members = new List<PartyMemberRecord> { new PartyMemberRecord { AccountId = "a" } },
            };
            _repo.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };

            PartyRecord received = null;
            _hub.PartyChanged += record => received = record;
            _hub.RegisterLocal("a", "alice");

            await _hub.SyncAsync();

            Assert.IsNotNull(received, "The reconcile pass must push the hosted party to the local roster.");
            Assert.AreEqual("p1", received.PartyId);
        }

        [Test]
        public async Task SyncAsync_RaisesThePendingInviteForAnInvitedPlayer()
        {
            _repo.Indexes["b"] = new SocialIndexRecord
            {
                AccountId = "b",
                PendingPartyInvite = new PendingPartyInvite
                {
                    FromPlayerName = "alice",
                    PartyId = "p1",
                    ExpiresAtTicks = DateTimeOffset.UtcNow.AddMinutes(1).UtcDateTime.Ticks,
                },
            };

            string receivedAccount = null;
            _hub.PartyInviteReceived += (accountId, _) => receivedAccount = accountId;
            _hub.RegisterLocal("b", "bob");

            await _hub.SyncAsync();

            Assert.AreEqual("b", receivedAccount, "An invite stored on this zone must reach its recipient.");
        }

        [Test]
        public async Task SyncAsync_ClearsTheRosterWhenMembershipDisappears()
        {
            _repo.Parties["p1"] = new PartyRecord
            {
                PartyId = "p1",
                LeaderId = "a",
                Members = new List<PartyMemberRecord> { new PartyMemberRecord { AccountId = "a" } },
            };
            _repo.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };

            _hub.RegisterLocal("a", "alice");
            await _hub.SyncAsync();

            // The player is kicked elsewhere: the index loses its party pointer.
            _repo.Indexes["a"].PartyId = null;

            bool cleared = false;
            _hub.PartyChanged += record => cleared |= record == null;

            await _hub.SyncAsync();

            Assert.IsTrue(cleared, "A vanished membership has to clear the local roster view.");
        }

        #endregion

        /// <summary>
        /// An in-memory <see cref="ISocialRepository"/> that mirrors the record versions and can
        /// inject a single optimistic-concurrency failure, so the retry loop is exercised offline.
        /// </summary>
        private sealed class FakeSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, GuildRecord> Guilds = new Dictionary<string, GuildRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly Dictionary<string, PresenceRecord> Presence = new Dictionary<string, PresenceRecord>();
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

            public bool ConflictOnNextIndexSave;
            public int IndexSaveCalls;

            private int _versions;

            public Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
                => Task.FromResult(Clone(Get(Parties, partyId)));

            public Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
                => Save(record, Parties, record.PartyId, record.StorageVersion, () => record.StorageVersion = NextVersion());

            public Task DeletePartyAsync(string partyId, CancellationToken ct = default)
            {
                Parties.Remove(partyId);
                return Task.CompletedTask;
            }

            public Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Clone(Get(Indexes, accountId)));

            public Task<List<SocialIndexRecord>> LoadIndexesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default)
            {
                var result = new List<SocialIndexRecord>();
                foreach (string accountId in accountIds)
                    result.Add(Clone(Get(Indexes, accountId)));

                return Task.FromResult(result);
            }

            public Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default)
            {
                IndexSaveCalls++;

                if (ConflictOnNextIndexSave)
                {
                    ConflictOnNextIndexSave = false;
                    return Task.FromException(new StorageVersionConflictException("injected index conflict"));
                }

                return Save(record, Indexes, record.AccountId, record.StorageVersion, () => record.StorageVersion = NextVersion());
            }

            public Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Clone(Get(Presence, accountId)));

            public Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default)
                => Save(record, Presence, record.AccountId, record.StorageVersion, () => record.StorageVersion = NextVersion());

            public Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default)
                => Task.FromResult(Names.TryGetValue(lowercasedName ?? string.Empty, out string accountId) ? accountId : null);

            public Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default)
            {
                if (!string.IsNullOrEmpty(lowercasedName))
                    Names[lowercasedName] = accountId;

                return Task.CompletedTask;
            }

            public Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult(Clone(Get(Guilds, guildId)));

            public Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default)
                => Save(record, Guilds, record.GuildId, record.StorageVersion, () => record.StorageVersion = NextVersion());

            public Task DeleteGuildAsync(string guildId, CancellationToken ct = default)
            {
                Guilds.Remove(guildId);
                return Task.CompletedTask;
            }

            private static TValue Get<TValue>(Dictionary<string, TValue> store, string key)
                => store.TryGetValue(key, out TValue value) ? value : default;

            private Task Save<TValue>(
                TValue record, Dictionary<string, TValue> store, string key, string version, Action assignVersion)
                where TValue : class
            {
                if (store.TryGetValue(key, out TValue existing))
                {
                    string current = VersionOf(existing);
                    if (version != current)
                        return Task.FromException(new StorageVersionConflictException("version mismatch"));
                }
                else if (!string.IsNullOrEmpty(version))
                {
                    return Task.FromException(new StorageVersionConflictException("create conflict"));
                }

                assignVersion();
                store[key] = Clone(record);
                return Task.CompletedTask;
            }

            private static string VersionOf(object record)
                => record switch
                {
                    PartyRecord party => party.StorageVersion,
                    GuildRecord guild => guild.StorageVersion,
                    SocialIndexRecord index => index.StorageVersion,
                    PresenceRecord presence => presence.StorageVersion,
                    _ => null,
                };

            private string NextVersion() => "v" + (++_versions);

            private static T Clone<T>(T value) where T : class
                => value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
