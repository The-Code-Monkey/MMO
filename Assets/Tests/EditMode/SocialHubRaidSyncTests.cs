using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The hub's raid handoff: the reconcile pass sees a party's <c>RaidInstanceId</c>, resolves the
    /// instance host, and raises <see cref="SocialHubService.RaidTransferRequested"/> only for the
    /// local members whose host is another zone. The repository is an in-memory fake.
    /// </summary>
    public sealed class SocialHubRaidSyncTests
    {
        private GameObject _go;
        private SocialHubService _hub;
        private FakeSocialRepository _social;
        private FakeRaidRepository _raids;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("RaidHubUnderTest");
            _hub = _go.AddComponent<SocialHubService>();
            _hub.ZoneId = "server-a";

            _social = new FakeSocialRepository();
            _raids = new FakeRaidRepository();
            _hub.Configure(_social, _raids);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private void SeedRaid(string partyId, string instanceId, string hostZoneId, params string[] members)
        {
            var party = new PartyRecord { PartyId = partyId, LeaderId = members[0], RaidInstanceId = instanceId };
            foreach (string m in members)
                party.Members.Add(new PartyMemberRecord { AccountId = m });
            _social.Parties[partyId] = party;

            _raids.Instances[instanceId] = new RaidInstanceRecord
            {
                InstanceId = instanceId,
                PartyId = partyId,
                HostZoneId = hostZoneId,
                HostAddress = "10.0.0.9",
                HostPort = 7770,
            };
        }

        [Test]
        public async Task SyncAsync_MemberHostedElsewhere_RaisesTheTransfer()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            string transferredTo = null;
            _hub.RaidTransferRequested += (accountId, instance) => transferredTo = instance.InstanceId;

            await _hub.SyncAsync();

            Assert.AreEqual("r1", transferredTo, "A member whose instance host is another zone must be asked to move.");
        }

        [Test]
        public async Task SyncAsync_LocalHostZone_TransfersNobody()
        {
            SeedRaid("p1", "r1", "server-a", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            bool raised = false;
            _hub.RaidTransferRequested += (_, _) => raised = true;

            await _hub.SyncAsync();

            Assert.IsFalse(raised, "The host zone already has its members; it transfers nobody.");
        }

        [Test]
        public async Task SyncAsync_RepeatsOnce_RaisesTheTransferOnlyOncePerInstance()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            int count = 0;
            _hub.RaidTransferRequested += (_, _) => count++;

            await _hub.SyncAsync();
            await _hub.SyncAsync();

            Assert.AreEqual(1, count, "A repeated sync must not re-ask a member already told to move.");
        }

        [Test]
        public async Task OnSocialNotification_RaidPush_StartsTheTransferBeforeTheNextTick()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            string transferredTo = null;
            _hub.RaidTransferRequested += (_, instance) => transferredTo = instance.InstanceId;

            var notification = new SocialNotification
            {
                Kind = SocialNotificationKind.RaidAllocated,
                RaidId = "r1",
                PartyId = "p1",
                HostZoneId = "server-b",
                AccountIds = { "a" },
            };

            await _hub.OnSocialNotification(SocialNotification.FromEnvelope(
                SocialNotification.ToEnvelope(notification)));

            Assert.AreEqual("r1", transferredTo, "The push must funnel into the same reconcile path.");
        }

        [Test]
        public async Task OnSocialNotification_NonRaidKind_DoesNothing()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            bool raised = false;
            _hub.RaidTransferRequested += (_, _) => raised = true;

            await _hub.OnSocialNotification(null);

            Assert.IsFalse(raised);
        }

        [Test]
        public async Task SyncAsync_PointerWithoutAMembershipEntry_IsClearedByReconcile()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            // The raid is now multi-party and no longer lists p1: the pointer is dangling.
            _raids.Instances["r1"].MemberPartyIds.Add("pOther");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            bool raised = false;
            _hub.RaidTransferRequested += (_, _) => raised = true;

            await _hub.SyncAsync();

            Assert.IsTrue(string.IsNullOrEmpty(_social.Parties["p1"].RaidInstanceId),
                "Reconcile must clear a pointer with no matching membership entry.");
            Assert.IsFalse(raised, "A stale pointer must not trigger a handoff.");
        }

        [Test]
        public async Task SyncAsync_MultiPartyRoster_TransfersOnlyTheHostedPartysMembers()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _raids.Instances["r1"].MemberPartyIds.Add("p1");
            _raids.Instances["r1"].MemberPartyIds.Add("p2");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            string transferredTo = null;
            _hub.RaidTransferRequested += (_, instance) => transferredTo = instance.InstanceId;

            await _hub.SyncAsync();

            Assert.AreEqual("r1", transferredTo, "A member party of a multi-party raid must still be handed off.");
        }

        [Test]
        public async Task MissedNotification_StillConvergesOnTheNextReconcile()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");

            string transferredTo = null;
            _hub.RaidTransferRequested += (_, instance) => transferredTo = instance.InstanceId;

            // No push is ever delivered; the poll path alone must move the member.
            await _hub.SyncAsync();

            Assert.AreEqual("r1", transferredTo, "A dropped push must still converge via the 2 s poll.");
        }

        [Test]
        public async Task SyncAsync_RecordsReturnAnchorBeforeTransfer()
        {
            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            _hub.RegisterLocal("a", "Alice");
            _hub.ZoneId = "server-a"; // source zone

            await _hub.SyncAsync();

            // The anchor should be recorded in the raid record
            RaidInstanceRecord raid = _raids.Instances["r1"];
            Assert.IsNotNull(raid.ReturnAnchors, "ReturnAnchors list should be initialized");
            Assert.AreEqual(1, raid.ReturnAnchors.Count, "One anchor should be recorded");
            Assert.AreEqual("a", raid.ReturnAnchors[0].AccountId);
            Assert.AreEqual("server-a", raid.ReturnAnchors[0].OriginServerId);
            Assert.IsFalse(raid.ReturnAnchors[0].Returned);
        }

        [Test]
        public async Task SyncAsync_NoTransferWhenAnchorWriteFails()
        {
            // Use a raid repo that fails on save
            var failingRaids = new FailingRaidRepository();
            var hub = new GameObject("Hub").AddComponent<SocialHubService>();
            hub.ZoneId = "server-a";
            hub.Configure(_social, failingRaids);

            SeedRaid("p1", "r1", "server-b", "a");
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };
            hub.RegisterLocal("a", "Alice");

            bool transferRaised = false;
            hub.RaidTransferRequested += (_, _) => transferRaised = true;

            await hub.SyncAsync();

            Assert.IsFalse(transferRaised, "Transfer must not be raised when anchor write fails");
        }

        [Test]
        public async Task SyncAsync_RaidCompleted_ReturnsMemberExactlyOnce()
        {
            // This test simulates the host zone (server-b) where the raid is hosted
            var hub = new GameObject("HostHub").AddComponent<SocialHubService>();
            hub.ZoneId = "server-b";
            hub.Configure(_social, _raids);

            // Seed a raid hosted on this zone
            var party = new PartyRecord { PartyId = "p1", LeaderId = "a", RaidInstanceId = "r1" };
            party.Members.Add(new PartyMemberRecord { AccountId = "a" });
            _social.Parties["p1"] = party;
            _social.Indexes["a"] = new SocialIndexRecord { AccountId = "a", PartyId = "p1" };

            var raid = new RaidInstanceRecord
            {
                InstanceId = "r1",
                PartyId = "p1",
                HostZoneId = "server-b",
                HostAddress = "10.0.0.9",
                HostPort = 7770,
            };
            raid.ReturnAnchors.Add(new RaidReturnAnchor
            {
                AccountId = "a",
                OriginHost = "10.0.0.1",
                OriginPort = 7770,
                OriginServerId = "server-a",
                Returned = false,
            });
            _raids.Instances["r1"] = raid;

            hub.RegisterLocal("a", "Alice");

            // First sync: establish lastInstanceId
            await hub.SyncAsync();

            // Clear the raid instance id (simulate completion)
            party.RaidInstanceId = null;
            await _social.SavePartyAsync(party);

            int returnCount = 0;
            RaidReturnAnchor capturedAnchor = default;
            hub.RaidReturnRequested += (accountId, anchor) =>
            {
                returnCount++;
                capturedAnchor = anchor;
            };

            // Second sync: should drive return
            await hub.SyncAsync();

            Assert.AreEqual(1, returnCount, "Return must be issued exactly once");
            Assert.AreEqual("a", capturedAnchor.AccountId);
            Assert.AreEqual("server-a", capturedAnchor.OriginServerId);

            // Third sync: should NOT issue another return (exactly-once)
            await hub.SyncAsync();
            Assert.AreEqual(1, returnCount, "Second pass must not re-issue return");
        }

        private sealed class FailingRaidRepository : IRaidInstanceRepository
        {
            public readonly Dictionary<string, RaidInstanceRecord> Instances = new Dictionary<string, RaidInstanceRecord>();

            public Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default)
                => Task.FromResult(Instances.TryGetValue(instanceId, out RaidInstanceRecord r) ? r : null);

            public Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default)
            {
                // Simulate CAS failure / save failure
                return Task.FromException(new System.Exception("save failed"));
            }

            public Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default)
            {
                Instances.Remove(instanceId);
                return Task.CompletedTask;
            }
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
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly List<SocialNotification> Notifications = new List<SocialNotification>();

            public Task<PartyRecord> LoadPartyAsync(string partyId, CancellationToken ct = default)
                => Task.FromResult(Parties.TryGetValue(partyId, out PartyRecord p) ? Clone(p) : null);

            public Task SavePartyAsync(PartyRecord record, CancellationToken ct = default)
            {
                Parties[record.PartyId] = Clone(record);
                return Task.CompletedTask;
            }

            public Task DeletePartyAsync(string partyId, CancellationToken ct = default)
            {
                Parties.Remove(partyId);
                return Task.CompletedTask;
            }

            public Task<SocialIndexRecord> LoadIndexAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult(Indexes.TryGetValue(accountId, out SocialIndexRecord i) ? Clone(i) : null);

            public Task<List<SocialIndexRecord>> LoadIndexesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default)
            {
                var result = new List<SocialIndexRecord>();
                foreach (string accountId in accountIds)
                {
                    if (Indexes.TryGetValue(accountId, out SocialIndexRecord i))
                        result.Add(Clone(i));
                }

                return Task.FromResult(result);
            }

            public Task SaveIndexAsync(SocialIndexRecord record, CancellationToken ct = default)
            {
                Indexes[record.AccountId] = Clone(record);
                return Task.CompletedTask;
            }

            public Task<PresenceRecord> LoadPresenceAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult<PresenceRecord>(null);

            public Task SavePresenceAsync(PresenceRecord record, CancellationToken ct = default)
                => Task.CompletedTask;


            public Task<string> ResolveNameAsync(string lowercasedName, CancellationToken ct = default)
                => Task.FromResult<string>(null);

            public Task SaveNameAsync(string lowercasedName, string accountId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<GuildRecord> LoadGuildAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult<GuildRecord>(null);

            public Task SaveGuildAsync(GuildRecord record, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task DeleteGuildAsync(string guildId, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<FriendListRecord> LoadFriendsAsync(string accountId, CancellationToken ct = default)
                => Task.FromResult<FriendListRecord>(null);

            public Task SaveFriendsAsync(FriendListRecord record, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<GuildBankRecord> LoadGuildBankAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult<GuildBankRecord>(null);

            public Task SaveGuildBankAsync(GuildBankRecord record, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<GuildBankLedgerRecord> LoadGuildBankLedgerAsync(string guildId, CancellationToken ct = default)
                => Task.FromResult<GuildBankLedgerRecord>(null);

            public Task SaveGuildBankLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default)
                => Task.CompletedTask;

            public Task<List<PresenceRecord>> LoadPresencesAsync(IReadOnlyList<string> accountIds, CancellationToken ct = default)
                => Task.FromResult(new List<PresenceRecord>());

            public Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default)
            {
                Notifications.Add(notification);
                return Task.CompletedTask;
            }

            private static T Clone<T>(T value) where T : class
                => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
