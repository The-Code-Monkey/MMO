using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Object;
using MMO.Net;
using MMO.Social;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The social surface end to end over a live host connection: the player prefab carries the
    /// party and guild views, an intent crosses a real <c>[ServerRpc]</c> and comes back through a
    /// <c>[TargetRpc]</c>, and the authoritative roster replicates through the SyncVar.
    ///
    /// <para>
    /// The backend is an in-memory <see cref="ISocialRepository"/> swapped in through
    /// <see cref="SocialHubService.Configure"/>, so these tests need no running Nakama — the point
    /// is the wiring and the RPC path, not the transport.
    /// </para>
    /// </summary>
    public sealed class PlayerPartyPlayModeTests
    {
        private NetworkManager _manager;
        private SocialHubService _hub;
        private FakeSocialRepository _repo;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            _hub = _manager.GetComponent<SocialHubService>();
            Assert.IsNotNull(_hub, "The factory must place the social hub to wire the seam.");

            _repo = new FakeSocialRepository();
            _hub.Configure(_repo);

            // Let NetworkManager.Awake and the gate's Start run before anything connects.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);

            _hub.Configure(new MMO.Persistence.NakamaSocialRepository(
                new MMO.Persistence.NakamaRestClient(new MMO.Persistence.BackendSettings())));

            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnedPlayer_CarriesThePartyAndGuildViews()
        {
            yield return StartHostAndGetPlayer();

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player.GetComponent<PlayerParty>(), "The player prefab must carry PlayerParty.");
            Assert.IsNotNull(player.GetComponent<PlayerGuild>(), "The player prefab must carry PlayerGuild.");
        }

        [UnityTest]
        public IEnumerator RequestInvite_UnknownTarget_ReportsTheRuleFailureToTheClient()
        {
            yield return StartHostAndGetPlayer();

            PlayerParty party = NetworkManagerTestHelper.FindPlayerObject(_manager).GetComponent<PlayerParty>();
            PartyRule reported = PartyRule.Ok;
            bool raised = false;
            party.RuleFailed += rule => { raised = true; reported = rule; };

            party.RequestInvite("nobody-on-any-zone");

            int frames = 0;
            while (!raised && frames++ < 300)
                yield return null;

            Assert.IsTrue(raised, "The server must answer a refused invite with a TargetRpc.");
            Assert.AreEqual(PartyRule.TargetNotFound, reported);
        }

        [UnityTest]
        public IEnumerator ApplyRoster_ReplicatesToTheClientView()
        {
            yield return StartHostAndGetPlayer();

            PlayerParty party = NetworkManagerTestHelper.FindPlayerObject(_manager).GetComponent<PlayerParty>();
            bool changed = false;
            party.RosterChanged += () => changed = true;

            var record = new PartyRecord { PartyId = "p1", LeaderId = "a" };
            record.Members.Add(new PartyMemberRecord { AccountId = "a", PlayerName = "Alice" });
            party.ApplyRoster(record);

            yield return null;

            Assert.IsTrue(changed, "A roster write must raise RosterChanged through the SyncVar.");
            StringAssert.Contains("p1", party.RosterJson);
            Assert.AreEqual("p1", party.PartyId);
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
        }

        private sealed class FakeSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, GuildRecord> Guilds = new Dictionary<string, GuildRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly Dictionary<string, PresenceRecord> Presence = new Dictionary<string, PresenceRecord>();
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
        }
    }
}
