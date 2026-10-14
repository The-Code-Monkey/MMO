using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Two-zone raid handoff over the <c>social_notify</c> push. Zone A allocates a raid; the shared
    /// fake repository stands in for Nakama and, when forwarding is enabled, relays the resulting
    /// notification into zone B's hub exactly as the Lua module + socket would. Zone B must begin its
    /// transfer without its own reconcile tick; with delivery disabled it converges on the poll.
    /// </summary>
    public sealed class SocialNotifyRaidPlayModeTests
    {
        private GameObject _zoneA;
        private GameObject _zoneB;
        private SocialHubService _hubA;
        private SocialHubService _hubB;
        private ForwardingSocialRepository _social;
        private FakeRaidRepository _raids;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _social = new ForwardingSocialRepository();
            _raids = new FakeRaidRepository();

            _zoneA = new GameObject("ZoneA");
            _hubA = _zoneA.AddComponent<SocialHubService>();
            _hubA.ZoneId = "server-a";
            _hubA.Configure(_social, _raids);

            _zoneB = new GameObject("ZoneB");
            _hubB = _zoneB.AddComponent<SocialHubService>();
            _hubB.ZoneId = "server-b";
            _hubB.Configure(_social, _raids);

            // Stands in for the RPC -> Lua module -> socket hop: the "backend" delivers the push.
            _social.ForwardTo = _hubB;

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_zoneA != null)
                Object.DestroyImmediate(_zoneA);
            if (_zoneB != null)
                Object.DestroyImmediate(_zoneB);

            yield return null;
        }

        private void SeedRaidParty()
        {
            var party = new PartyRecord { PartyId = "p1", LeaderId = "leader" };
            foreach (string id in new[] { "leader", "m2", "m3", "m4", "b" })
                party.Members.Add(new PartyMemberRecord { AccountId = id });

            _social.Parties["p1"] = party;
            _social.Indexes["leader"] = new SocialIndexRecord { AccountId = "leader", PartyId = "p1" };
            _social.Indexes["b"] = new SocialIndexRecord { AccountId = "b", PartyId = "p1" };
        }

        [UnityTest]
        public IEnumerator DeliveredNotify_MovesZoneBWithoutAReconcileTick()
        {
            SeedRaidParty();
            _hubB.RegisterLocal("b", "Bob");

            string transferredTo = null;
            _hubB.RaidTransferRequested += (_, instance) => transferredTo = instance.InstanceId;

            yield return Await(_hubA.AllocateRaidAsync("leader"));

            Assert.IsFalse(string.IsNullOrEmpty(_social.Parties["p1"].RaidInstanceId));
            Assert.AreEqual(
                _social.Parties["p1"].RaidInstanceId,
                transferredTo,
                "The delivered push must start zone B's transfer without waiting for its own tick.");
        }

        [UnityTest]
        public IEnumerator DroppedNotify_ConvergesOnTheNextReconcile()
        {
            SeedRaidParty();
            _hubB.RegisterLocal("b", "Bob");
            _social.ForwardTo = null; // delivery disabled: simulate a dropped frame

            string transferredTo = null;
            _hubB.RaidTransferRequested += (_, instance) => transferredTo = instance.InstanceId;

            yield return Await(_hubA.AllocateRaidAsync("leader"));
            Assert.IsNull(transferredTo, "With no push, zone B must not act until it polls.");

            yield return Await(_hubB.SyncAsync());

            Assert.AreEqual(
                _social.Parties["p1"].RaidInstanceId,
                transferredTo,
                "A dropped push must still converge via the reconcile pass.");
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted && task.Exception != null)
                throw task.Exception;
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

        /// <summary>
        /// An in-memory repository whose <see cref="NotifySocialAsync"/> optionally relays the push
        /// into a peer hub — the exact contract the real socket path honours.
        /// </summary>
        private sealed class ForwardingSocialRepository : ISocialRepository
        {
            public readonly Dictionary<string, PartyRecord> Parties = new Dictionary<string, PartyRecord>();
            public readonly Dictionary<string, SocialIndexRecord> Indexes = new Dictionary<string, SocialIndexRecord>();
            public readonly List<SocialNotification> Notifications = new List<SocialNotification>();

            /// <summary>When set, every push is handed to this zone's hub as the socket would.</summary>
            public SocialHubService ForwardTo;

            public Task NotifySocialAsync(SocialNotification notification, CancellationToken ct = default)
            {
                Notifications.Add(notification);
                return ForwardTo != null
                    ? ForwardTo.OnSocialNotification(notification)
                    : Task.CompletedTask;
            }

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

            private static T Clone<T>(T value) where T : class
                => value == null ? null : JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
        }
    }
}
