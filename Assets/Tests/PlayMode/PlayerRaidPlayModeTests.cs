using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using MMO.Net;
using MMO.Social;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The raid surface end to end over a live host connection: an intent crosses a real
    /// <c>[ServerRpc]</c>, <see cref="SocialHubService"/> allocates a CAS-guarded instance, and a
    /// party whose instance is hosted on another zone is handed off through the zone-transfer
    /// pipeline (flush → token → redirect broadcast). The backend is an in-memory
    /// <see cref="ISocialRepository"/> and <see cref="IRaidInstanceRepository"/>, so no Nakama is
    /// needed — the point is the wiring and the RPC/handoff path, not the transport.
    /// </summary>
    public sealed class PlayerRaidPlayModeTests
    {
        private const string PartyId = "p1";
        private const string RaidInstanceId = "r1";
        private const string RemoteZone = "remote-zone";
        private const string RemoteHost = "10.9.9.9";
        private const ushort RemotePort = 7999;

        private NetworkManager _manager;
        private SocialHubService _hub;
        private FakeSocialRepository _repo;
        private FakeRaidRepository _raids;
        private PlayerRaid _raid;
        private string _accountId;

        private ZoneTransferTokenService _tokenService;
        private ZoneTransferClient _zoneClient;
        private IZoneTransferTokenStore _previousStore;
        private Action<ZoneTransferBroadcast, Channel> _broadcastSpy;
        private bool _clientHandlerRemoved;

        private readonly List<ZoneTransferBroadcast> _received = new List<ZoneTransferBroadcast>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            _hub = _manager.GetComponent<SocialHubService>();
            Assert.IsNotNull(_hub, "The factory must place the social hub to wire the seam.");

            _repo = new FakeSocialRepository();
            _raids = new FakeRaidRepository();
            _hub.Configure(_repo, _raids);

            _tokenService = _manager.gameObject.GetComponent<ZoneTransferTokenService>();
            _zoneClient = _manager.gameObject.GetComponent<ZoneTransferClient>();
            _previousStore = _tokenService != null ? _tokenService.Store : null;

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            if (_broadcastSpy != null && _manager.ClientManager != null)
                _manager.ClientManager.UnregisterBroadcast<ZoneTransferBroadcast>(_broadcastSpy);

            if (_clientHandlerRemoved && _zoneClient != null && _manager.ClientManager != null)
                _manager.ClientManager.RegisterBroadcast<ZoneTransferBroadcast>(_zoneClient.OnTransferBroadcast);

            if (_tokenService != null && _previousStore != null)
                _tokenService.Configure(_previousStore);

            _hub.Configure(new MMO.Persistence.NakamaSocialRepository(
                new MMO.Persistence.NakamaRestClient(new MMO.Persistence.BackendSettings())));

            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_CarriesTheRaidView()
        {
            yield return StartHostAndGetPlayer();

            Assert.IsNotNull(_raid, "The player prefab must carry PlayerRaid.");
        }

        [UnityTest]
        public IEnumerator RequestAllocate_WhenNotTheLeader_ReportsNotLeaderToTheClient()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(leaderId: "someone-else", memberCount: 5);

            RaidRule reported = RaidRule.Ok;
            bool raised = false;
            _raid.RuleFailed += rule => { raised = true; reported = rule; };

            _raid.RequestAllocateRaid();

            int frames = 0;
            while (!raised && frames++ < 300)
                yield return null;

            Assert.IsTrue(raised, "The server must answer a refused raid intent with a TargetRpc.");
            Assert.AreEqual(RaidRule.NotLeader, reported);
            Assert.IsNull(_repo.Parties[PartyId].RaidInstanceId, "A non-leader must not allocate a raid.");
            Assert.AreEqual(0, _raids.Instances.Count);
        }

        [UnityTest]
        public IEnumerator RequestAllocate_AsLeaderOfEnoughPlayers_AllocatesTheInstance()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize);

            bool failed = false;
            _raid.RuleFailed += _ => failed = true;

            _raid.RequestAllocateRaid();

            int frames = 0;
            while (_repo.Parties[PartyId].RaidInstanceId == null && frames++ < 300)
                yield return null;

            Assert.IsFalse(failed, "A valid leader intent must not be refused.");
            Assert.IsNotNull(_repo.Parties[PartyId].RaidInstanceId, "The leader's party must gain a raid instance.");
            Assert.AreEqual(1, _raids.Instances.Count);
            Assert.AreEqual(PartyId, _raids.Instances[_repo.Parties[PartyId].RaidInstanceId].PartyId);
        }

        [UnityTest]
        public IEnumerator RaidHostedOnAnotherZone_HandsTheMemberOffWithARedirect()
        {
            yield return StartHostAndGetPlayer();

            // Observe the redirect instead of letting ZoneTransferClient act on it, so the host is
            // never disconnected mid-test by a reconnect it cannot complete.
            _broadcastSpy = (message, channel) => _received.Add(message);
            _manager.ClientManager.RegisterBroadcast<ZoneTransferBroadcast>(_broadcastSpy);
            if (_zoneClient != null)
            {
                _manager.ClientManager.UnregisterBroadcast<ZoneTransferBroadcast>(_zoneClient.OnTransferBroadcast);
                _clientHandlerRemoved = true;
            }

            var spyStore = new SpyTokenStore();
            _tokenService.Configure(spyStore);

            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize, raidInstanceId: RaidInstanceId);
            _raids.Instances[RaidInstanceId] = new RaidInstanceRecord
            {
                InstanceId = RaidInstanceId,
                PartyId = PartyId,
                HostZoneId = RemoteZone,
                HostAddress = RemoteHost,
                HostPort = RemotePort,
            };

            // The reconcile pass is the trigger; the hub raises RaidTransferRequested for the local
            // member and PlayerRaid runs the handoff on its own.
            yield return Await(_hub.SyncAsync());

            int frames = 0;
            while (_received.Count == 0 && frames++ < 600)
                yield return null;

            Assert.AreEqual(1, _received.Count, "The member must receive exactly one redirect.");
            Assert.AreEqual(RemoteHost, _received[0].TargetHost);
            Assert.AreEqual(RemotePort, _received[0].TargetPort);
            Assert.AreEqual(RemoteZone, _received[0].TargetServerId);
            Assert.AreEqual("Raid instance", _received[0].Reason);
            Assert.AreEqual(1, spyStore.Issued.Count, "Before the redirect the handoff must mint the member's token.");
            Assert.AreEqual(_accountId, spyStore.Issued[0].AccountId);
        }

        [UnityTest]
        public IEnumerator RequestJoinRaid_AsLeaderOfASecondParty_JoinsTheRaid()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize);
            SeedHostedRaid(seedPartyId: "p0", memberPartyIds: new[] { "p0" });

            bool failed = false;
            _raid.RuleFailed += _ => failed = true;

            _raid.RequestJoinRaid(RaidInstanceId);

            int frames = 0;
            while (_repo.Parties[PartyId].RaidInstanceId == null && frames++ < 300)
                yield return null;

            Assert.IsFalse(failed, "A valid leader join must not be refused.");
            Assert.AreEqual(RaidInstanceId, _repo.Parties[PartyId].RaidInstanceId);
            CollectionAssert.Contains(_raids.Instances[RaidInstanceId].MemberPartyIds, PartyId);
        }

        [UnityTest]
        public IEnumerator RequestLeaveRaid_PullsThePartyOutAndKeepsTheRaidAlive()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize, raidInstanceId: RaidInstanceId);
            SeedHostedRaid(seedPartyId: PartyId, memberPartyIds: new[] { PartyId, "p2" });

            _raid.RequestLeaveRaid();

            int frames = 0;
            while (_repo.Parties[PartyId].RaidInstanceId != null && frames++ < 300)
                yield return null;

            Assert.IsNull(_repo.Parties[PartyId].RaidInstanceId);
            Assert.IsTrue(_raids.Instances.ContainsKey(RaidInstanceId), "A sibling party keeps the raid alive.");
            CollectionAssert.DoesNotContain(_raids.Instances[RaidInstanceId].MemberPartyIds, PartyId);
        }

        [UnityTest]
        public IEnumerator RpcJoinRaid_FromANonOwner_IsIgnoredByTheServer()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize);
            SeedHostedRaid(seedPartyId: "p0", memberPartyIds: new[] { "p0" });

            bool failed = false;
            _raid.RuleFailed += _ => failed = true;

            var intruder = new NetworkConnection(_manager, clientId: 987655, transportIndex: 0, asServer: true);
            MethodInfo rpc = typeof(PlayerRaid).GetMethod("RpcJoinRaid", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(rpc, "RpcJoinRaid lost its name or access; the ServerRpc seam moved.");
            rpc.Invoke(_raid, new object[] { RaidInstanceId, intruder });

            for (int i = 0; i < 60; i++)
                yield return null;

            Assert.IsFalse(failed, "A non-owner must not provoke a response.");
            Assert.IsNull(_repo.Parties[PartyId].RaidInstanceId, "A non-owner must not join the raid.");
        }

        private void SeedHostedRaid(string seedPartyId, string[] memberPartyIds)
        {
            var raid = new RaidInstanceRecord
            {
                InstanceId = RaidInstanceId,
                PartyId = seedPartyId,
                SeedPartyId = seedPartyId,
                LeaderId = seedPartyId + "leader",
                HostZoneId = "local",
                HostAddress = null,
            };
            raid.MemberPartyIds.AddRange(memberPartyIds);
            _raids.Instances[RaidInstanceId] = raid;
        }

        [UnityTest]
        public IEnumerator RpcAllocateRaid_FromANonOwner_IsIgnoredByTheServer()
        {
            yield return StartHostAndGetPlayer();
            SeedParty(_accountId, memberCount: RaidRules.MinRaidSize);

            bool failed = false;
            _raid.RuleFailed += _ => failed = true;

            // A host has exactly one owner, so a non-owner request can only be simulated by feeding
            // the ServerRpc body a connection that is not this object's owner.
            var intruder = new NetworkConnection(_manager, clientId: 987654, transportIndex: 0, asServer: true);
            MethodInfo rpc = typeof(PlayerRaid).GetMethod("RpcAllocateRaid", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(rpc, "RpcAllocateRaid lost its name or access; the ServerRpc seam moved.");
            rpc.Invoke(_raid, new object[] { intruder });

            for (int i = 0; i < 60; i++)
                yield return null;

            Assert.IsFalse(failed, "A non-owner must not provoke a response.");
            Assert.IsNull(_repo.Parties[PartyId].RaidInstanceId, "A non-owner must not allocate a raid.");
            Assert.AreEqual(0, _raids.Instances.Count);
        }

        private IEnumerator StartHostAndGetPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            int frames = 0;
            while (!player.IsClientInitialized && frames++ < 300)
                yield return null;

            _raid = player.GetComponent<PlayerRaid>();
            Assert.IsNotNull(_raid, "PlayerPrefabFactory must put PlayerRaid on the player.");

            _accountId = _raid.AccountId;
            Assert.IsFalse(string.IsNullOrEmpty(_accountId), "The server must resolve the player's account id.");
        }

        private void SeedParty(string leaderId, int memberCount, string raidInstanceId = null)
        {
            var party = new PartyRecord { PartyId = PartyId, LeaderId = leaderId, RaidInstanceId = raidInstanceId };
            party.Members.Add(new PartyMemberRecord { AccountId = _accountId, PlayerName = "Tester" });
            for (int i = 1; i < memberCount; i++)
                party.Members.Add(new PartyMemberRecord { AccountId = "member" + i, PlayerName = "Member" + i });

            _repo.Parties[PartyId] = party;
            _repo.Indexes[_accountId] = new SocialIndexRecord { AccountId = _accountId, PartyId = PartyId };
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
                throw task.Exception;
        }

        private sealed class SpyTokenStore : IZoneTransferTokenStore
        {
            public readonly List<ZoneTransferToken> Issued = new List<ZoneTransferToken>();

            public Task<string> IssueAsync(ZoneTransferToken token, CancellationToken ct)
            {
                Issued.Add(token);
                return Task.FromResult("spy-token-" + Issued.Count);
            }

            public Task<ZoneTransferToken> ConsumeAsync(string tokenString, CancellationToken ct)
                => Task.FromResult<ZoneTransferToken>(null);
        }

        private sealed class FakeRaidRepository : IRaidInstanceRepository
        {
            public readonly Dictionary<string, RaidInstanceRecord> Instances = new Dictionary<string, RaidInstanceRecord>();

            public Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default)
                => Task.FromResult(Instances.TryGetValue(instanceId, out RaidInstanceRecord r) ? r : null);

            public Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default)
            {
                Instances[record.InstanceId] = record;
                return Task.CompletedTask;
            }

            public Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            {
                Instances.Remove(instanceId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, GuildRecord> Guilds = new Dictionary<string, GuildRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly Dictionary<string, PresenceRecord> Presence = new Dictionary<string, PresenceRecord>();
            public readonly Dictionary<string, FriendListRecord> Friends = new Dictionary<string, FriendListRecord>();
            public readonly Dictionary<string, GuildBankRecord> GuildBanks = new Dictionary<string, GuildBankRecord>();
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

            public Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
                => Task.FromResult(Parties.TryGetValue(partyId, out PartyRecord p) ? p : null);

            public Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
            {
                Parties[record.PartyId] = record;
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
                var result = new List<SocialIndexRecord>();
                foreach (string accountId in accountIds)
                    result.Add(Indexes.TryGetValue(accountId, out SocialIndexRecord i) ? i : null);

                return Task.FromResult(result);
            }

            public Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default)
            {
                Indexes[record.AccountId] = record;
                return Task.CompletedTask;
            }

            public Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Presence.TryGetValue(accountId, out PresenceRecord p) ? p : null);

            public Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default)
            {
                Presence[record.AccountId] = record;
                return Task.CompletedTask;
            }

            public Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default)
                => Task.FromResult(Names.TryGetValue(lowercasedName ?? string.Empty, out string accountId) ? accountId : null);

            public Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default)
            {
                if (!string.IsNullOrEmpty(lowercasedName))
                    Names[lowercasedName] = accountId;

                return Task.CompletedTask;
            }

            public Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult(Guilds.TryGetValue(guildId, out GuildRecord g) ? g : null);

            public Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default)
            {
                Guilds[record.GuildId] = record;
                return Task.CompletedTask;
            }

            public Task DeleteGuildAsync(string guildId, CancellationToken ct = default)
            {
                Guilds.Remove(guildId);
                return Task.CompletedTask;
            }

            public Task<FriendListRecord> LoadFriendsAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Friends.TryGetValue(accountId, out FriendListRecord f) ? f : null);

            public Task SaveFriendsAsync(FriendListRecord record, CancellationToken ct = default)
            {
                Friends[record.AccountId] = record;
                return Task.CompletedTask;
            }

            public Task<GuildBankRecord> LoadGuildBankAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult(GuildBanks.TryGetValue(guildId, out GuildBankRecord b) ? b : null);

            public Task SaveGuildBankAsync(GuildBankRecord record, CancellationToken ct = default)
            {
                GuildBanks[record.GuildId] = record;
                return Task.CompletedTask;
            }

            public Task<GuildBankLedgerRecord> LoadGuildBankLedgerAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult<GuildBankLedgerRecord>(null);

            public Task SaveGuildBankLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<List<PresenceRecord>> LoadPresencesAsync(
                IReadOnlyList<string> accountIds, CancellationToken ct = default)
            {
                var result = new List<PresenceRecord>();
                foreach (string accountId in accountIds)
                {
                    if (Presence.TryGetValue(accountId, out PresenceRecord p))
                        result.Add(p);
                }

                return Task.FromResult(result);
            }

            public Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default)
                => Task.CompletedTask;
        }
    }
}
