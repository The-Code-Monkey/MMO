using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The join gate's persistence seam, tested without a server: once a <see cref="IPlayerLoader"/>
    /// is wired, the verdict must wait for the loaded record, a missing account id must be refused,
    /// and a load that lands after the timeout reaper must not admit a ghost. A hand-set
    /// <c>TaskCompletionSource</c> stub lets every one of those be driven to completion on
    /// demand rather than timed.
    /// </summary>
    public sealed class MMOConnectionGatePersistenceTests
    {
        private sealed class DelayedPlayerLoader : IPlayerLoader
        {
            public string AccountId;
            private readonly TaskCompletionSource<PlayerSaveRecord> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct)
            {
                AccountId = accountId;
                return _source.Task;
            }

            public void Complete(PlayerSaveRecord record)
                => _source.SetResult(record);
        }

        private GameObject _go;
        private MMOConnectionGate _gate;
        private DelayedPlayerLoader _loader;

        private static NetworkConnection Connection => NetworkManager.EmptyConnection;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GateUnderTest");
            _gate = _go.AddComponent<MMOConnectionGate>();
            _loader = new DelayedPlayerLoader();
            _gate.PlayerLoader = _loader;
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [Test]
        public void HandleJoinRequest_WithLoader_RejectsAMissingAccountId()
        {
            bool? approved = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;

            _gate.HandleJoinRequest(Connection, "Bob", null);

            Assert.AreEqual(false, approved);
        }

        [Test]
        public void HandleJoinRequest_WithLoader_RejectsABlankAccountId()
        {
            bool? approved = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;

            _gate.HandleJoinRequest(Connection, "Bob", "   ");

            Assert.AreEqual(false, approved);
        }

        [Test]
        public async Task HandleJoinRequest_WithLoader_ParksTheJoinUntilTheRecordLoads()
        {
            bool? approved = null;
            string loadedAccount = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;
            _gate.PlayerJoinLoaded += (_, accountId, _) => loadedAccount = accountId;

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1");

            Assert.AreEqual(1, _gate.PendingHandshakes, "The handshake stays parked while the load is in flight.");
            Assert.IsNull(approved, "No verdict is possible until the record has been read.");
            Assert.AreEqual("acc-1", _loader.AccountId, "The loader is asked for the player's own record.");

            _loader.Complete(new PlayerSaveRecord { UserId = "acc-1", Level = 8 });
            await Task.Delay(50);

            Assert.AreEqual(true, approved);
            Assert.AreEqual(0, _gate.PendingHandshakes, "A settled join must be cleared so the reaper never turns it back.");
            Assert.AreEqual("acc-1", loadedAccount);
        }

        [Test]
        public async Task PlayerJoinLoaded_CarriesTheRecordAndClientIdBeforeAdmission()
        {
            int? loadedClient = null;
            string loadedAccount = null;
            PlayerSaveRecord loadedRecord = null;
            _gate.PlayerJoinLoaded += (clientId, accountId, record) =>
            {
                loadedClient = clientId;
                loadedAccount = accountId;
                loadedRecord = record;
            };

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-2");
            _loader.Complete(new PlayerSaveRecord { UserId = "acc-2", Level = 12 });
            await Task.Delay(50);

            Assert.AreEqual(Connection.ClientId, loadedClient);
            Assert.AreEqual("acc-2", loadedAccount);
            Assert.IsNotNull(loadedRecord);
            Assert.AreEqual(12, loadedRecord.Level);
        }

        [Test]
        public async Task ReapedHandshake_InFlightLoad_NeverDecidesTwice()
        {
            int verdicts = 0;
            int joinLoads = 0;
            _gate.OnAuthenticationResult += (_, _) => verdicts++;
            _gate.PlayerJoinLoaded += (_, _, _) => joinLoads++;

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-3");
            Assert.AreEqual(1, _gate.PendingHandshakes);

            _gate.Apply(Connection, JoinDecision.Reject(JoinPolicy.ReasonTimedOut));

            Assert.AreEqual(1, verdicts, "The reaper's rejection is the only verdict so far.");
            Assert.AreEqual(0, _gate.PendingHandshakes);

            _loader.Complete(new PlayerSaveRecord { UserId = "acc-3" });
            await Task.Delay(50);

            Assert.AreEqual(1, verdicts, "A load landing after the reaper must not admit a ghost.");
            Assert.AreEqual(0, joinLoads, "No record hand-off for a handshake that is already gone.");
        }

        [Test]
        public async Task HandleJoinRequest_WithoutAPendingHandshake_NeverDecides()
        {
            int verdicts = 0;
            _gate.OnAuthenticationResult += (_, _) => verdicts++;

            _gate.HandleJoinRequest(Connection, "Bob", "acc-4");
            _loader.Complete(new PlayerSaveRecord { UserId = "acc-4" });
            await Task.Delay(50);

            Assert.AreEqual(0, verdicts, "The gate only decides joins it parked in OnRemoteConnection.");
        }
    }
}