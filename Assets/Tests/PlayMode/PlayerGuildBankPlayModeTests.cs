using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Object;
using MMO.Inventory;
using MMO.Net;
using MMO.Social;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The guild bank end to end over a live host connection: an intent crosses a real
    /// <c>[ServerRpc]</c>, <see cref="SocialHubService"/> writes the versioned bank, and the
    /// authoritative tab replicates back through <c>PlayerGuild.Bank</c>. The backend is an
    /// in-memory <see cref="ISocialRepository"/>, so no Nakama is needed — the point is the
    /// wiring, the deposit/withdraw item movement, and the permission edit path.
    /// </summary>
    public sealed class PlayerGuildBankPlayModeTests
    {
        private const string GuildId = "g1";

        private NetworkManager _manager;
        private SocialHubService _hub;
        private FakeSocialRepository _repo;
        private PlayerGuild _guild;
        private string _accountId;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            _hub = _manager.GetComponent<SocialHubService>();
            Assert.IsNotNull(_hub, "The factory must place the social hub to wire the seam.");

            _repo = new FakeSocialRepository();
            _hub.Configure(_repo);

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
        public IEnumerator SyncAsync_SeededBank_ReplicatesToThePlayerView()
        {
            yield return StartHostAndGetPlayer();

            SeedGuild(_accountId, MakeBankWithItem());
            yield return Await(_hub.SyncAsync());

            yield return WaitUntil(() => _guild.Bank != null);

            Assert.IsNotNull(_guild.Bank, "The reconcile pass must push the seeded bank to the player.");
            Assert.AreEqual(GuildId, _guild.Bank.GuildId);
            Assert.AreEqual("test_sword", GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 2, 1).ItemName);
        }

        [UnityTest]
        public IEnumerator RequestWithdraw_MovesTheTabItemIntoTheBag()
        {
            yield return StartHostAndGetPlayer();

            SeedGuild(_accountId, MakeBankWithItem());
            yield return Await(_hub.SyncAsync());
            yield return WaitUntil(() => _guild.Bank != null);

            PlayerInventory bag = _guild.GetComponent<PlayerInventory>();
            Assert.IsTrue(GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 2, 1).ItemName == "test_sword");

            _guild.RequestWithdraw(0, 2, 1);

            yield return WaitUntil(() => bag.GetItemAt(0, 0).ItemName == "test_sword");

            Assert.AreEqual("test_sword", bag.GetItemAt(0, 0).ItemName, "The withdrawn item must land in the bag.");
            yield return WaitUntil(() => GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 2, 1).IsEmpty);
            Assert.IsTrue(GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 2, 1).IsEmpty,
                "The withdrawn item must leave the bank tab.");
        }

        [UnityTest]
        public IEnumerator RequestDeposit_MovesTheBagItemIntoTheTab()
        {
            yield return StartHostAndGetPlayer();

            SeedGuild(_accountId, GuildBankRules.CreateDefault(GuildId));
            yield return Await(_hub.SyncAsync());
            yield return WaitUntil(() => _guild.Bank != null);

            PlayerInventory bag = _guild.GetComponent<PlayerInventory>();
            Assert.IsTrue(bag.TryAddExistingItem(
                new ItemInstance { ItemName = "test_potion", Width = 1, Height = 1 }, 0, 0));

            _guild.RequestDeposit(0, 0, 0);

            yield return WaitUntil(() => GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 0, 0).ItemName == "test_potion");

            Assert.AreEqual("test_potion", GuildBankRules.ItemAt(GuildBankRules.FindTab(_guild.Bank, 0), 0, 0).ItemName,
                "The deposited item must appear in the bank tab.");
            Assert.IsTrue(bag.GetItemAt(0, 0).IsEmpty, "The deposited item must leave the bag.");
        }

        [UnityTest]
        public IEnumerator RequestSetPermission_ReplicatesToThePlayerView()
        {
            yield return StartHostAndGetPlayer();

            SeedGuild(_accountId, GuildBankRules.CreateDefault(GuildId));
            yield return Await(_hub.SyncAsync());
            yield return WaitUntil(() => _guild.Bank != null);

            _guild.RequestSetPermission((int)GuildRank.Member, allowDeposit: true, allowWithdraw: false, withdrawLimit: 5);

            yield return WaitUntil(() =>
            {
                GuildBankPermission permission = GuildBankRules.PermissionFor(_guild.Bank, GuildRank.Member);
                return !permission.AllowWithdraw && permission.WithdrawLimit == 5;
            });

            GuildBankPermission member = GuildBankRules.PermissionFor(_guild.Bank, GuildRank.Member);
            Assert.IsFalse(member.AllowWithdraw);
            Assert.AreEqual(5, member.WithdrawLimit);
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

            _guild = player.GetComponent<PlayerGuild>();
            Assert.IsNotNull(_guild, "The player prefab must carry PlayerGuild.");

            _accountId = player.GetComponent<PlayerParty>().AccountId;
            Assert.IsFalse(string.IsNullOrEmpty(_accountId), "The server must resolve the player's account id.");
        }

        private void SeedGuild(string accountId, GuildBankRecord bank)
        {
            _repo.Indexes[accountId] = new SocialIndexRecord { AccountId = accountId, GuildId = GuildId };

            var guild = new GuildRecord { GuildId = GuildId, Name = "Test Guild", LeaderId = accountId };
            guild.Members.Add(new GuildMemberRecord
            {
                AccountId = accountId,
                PlayerName = "Tester",
                Rank = (int)GuildRank.Leader,
            });
            _repo.Guilds[GuildId] = guild;
            _repo.GuildBanks[GuildId] = bank;
            _repo.Names["tester"] = accountId;
        }

        private static GuildBankRecord MakeBankWithItem()
        {
            GuildBankRecord bank = GuildBankRules.CreateDefault(GuildId);
            bank.Tabs[0].Items.Add(new ItemInstance
            {
                ItemName = "test_sword",
                GridX = 2,
                GridY = 1,
                Width = 1,
                Height = 1,
            });
            return bank;
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
                throw task.Exception;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames = 600)
        {
            int frames = 0;
            while (!condition() && frames++ < maxFrames)
                yield return null;
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