using FishNet.Connection;
using FishNet.Managing;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Exercises the gate component itself, with no server running. Every verdict comes
    /// out of the real component and the real settings, so these tests cannot drift from
    /// the rules the server actually enforces. <see cref="NetworkManager.EmptyConnection"/>
    /// stands in for a socket-less connection: it is a real NetworkConnection whose reads
    /// are all safe (ClientId -1, never authenticated, never active).
    /// </summary>
    public sealed class MMOConnectionGateTests
    {
        private GameObject _go;
        private MMOConnectionGate _gate;

        private static NetworkConnection Connection => NetworkManager.EmptyConnection;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GateUnderTest");
            _gate = _go.AddComponent<MMOConnectionGate>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private void Configure(bool maintenance = false, int maxClients = 10, float timeout = 5f,
            string[] allow = null, string[] block = null)
            => _gate.SetSettings(new MMOConnectionSettings(7770, maxClients, timeout, maintenance, allow, block));

        [Test]
        public void NewGate_HasDefaultSettings()
        {
            Assert.AreEqual(MMOConnectionSettings.DefaultMaxClients, _gate.Settings.MaxClients);
        }

        [Test]
        public void NewGate_HasNoPendingHandshakes()
        {
            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [Test]
        public void SetSettings_Null_FallsBackToDefaults()
        {
            _gate.SetSettings(null);

            Assert.IsNotNull(_gate.Settings);
            Assert.AreEqual(MMOConnectionSettings.DefaultPort, _gate.Settings.Port);
        }

        [Test]
        public void SetSettings_StoresTheGivenRules()
        {
            Configure(maintenance: true, maxClients: 3, block: new[] { "Bob" });

            Assert.IsTrue(_gate.Settings.MaintenanceMode);
            Assert.AreEqual(3, _gate.Settings.MaxClients);
            CollectionAssert.AreEqual(new[] { "Bob" }, _gate.Settings.BlockedPlayers);
        }

        [Test]
        public void OnRemoteConnection_Null_IsIgnored()
        {
            _gate.OnRemoteConnection(null);

            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [Test]
        public void OnRemoteConnection_ParksTheConnectionWithADeadline()
        {
            _gate.OnRemoteConnection(Connection);

            Assert.AreEqual(1, _gate.PendingHandshakes);
        }

        [Test]
        public void EvaluateJoin_NullConnection_IsIgnored()
        {
            Configure(maintenance: true);
            bool fired = false;
            _gate.OnAuthenticationResult += (_, _) => fired = true;

            _gate.EvaluateJoin(null, "Bob");

            Assert.IsFalse(fired, "A null connection has no verdict to report.");
        }

        [Test]
        public void Decide_Approves_AValidPlayer()
        {
            Configure();

            Assert.IsTrue(_gate.Decide("Bob", 0, 0f).Approved);
        }

        [Test]
        public void Decide_Rejects_MaintenanceMode()
        {
            Configure(maintenance: true);

            Assert.AreEqual(JoinPolicy.ReasonMaintenance, _gate.Decide("Bob", 0, 0f).Reason);
        }

        [Test]
        public void Decide_Rejects_BlockedPlayer()
        {
            Configure(block: new[] { "Bob" });

            Assert.AreEqual(JoinPolicy.ReasonBlocked, _gate.Decide("Bob", 0, 0f).Reason);
        }

        [Test]
        public void Decide_Rejects_PlayerNotOnAllowList()
        {
            Configure(allow: new[] { "Alice" });

            Assert.AreEqual(JoinPolicy.ReasonNotAllowed, _gate.Decide("Bob", 0, 0f).Reason);
        }

        [Test]
        public void Decide_Rejects_WhenServerIsFull()
        {
            Configure(maxClients: 1);

            Assert.AreEqual(JoinPolicy.ReasonFull, _gate.Decide("Bob", 1, 0f).Reason);
        }

        [Test]
        public void Decide_Rejects_MissingName()
        {
            Configure();

            Assert.AreEqual(JoinPolicy.ReasonMissingName, _gate.Decide("  ", 0, 0f).Reason);
        }

        [Test]
        public void Decide_Rejects_HandshakePastTimeout()
        {
            Configure(timeout: 5f);

            Assert.AreEqual(JoinPolicy.ReasonTimedOut, _gate.Decide("Bob", 0, 5.1f).Reason);
        }

        [Test]
        public void Decide_UsesConfiguredMaxClientsNotTheCallers()
        {
            Configure(maxClients: 4);

            Assert.IsTrue(_gate.Decide("Bob", 3, 0f).Approved);
            Assert.AreEqual(JoinPolicy.ReasonFull, _gate.Decide("Bob", 4, 0f).Reason);
        }

        [Test]
        public void Apply_Approve_ReportsApproval()
        {
            bool? approved = null;
            NetworkConnection reported = null;
            _gate.OnAuthenticationResult += (conn, ok) =>
            {
                reported = conn;
                approved = ok;
            };

            _gate.Apply(Connection, _gate.Decide("Bob", 0, 0f));

            Assert.AreEqual(true, approved);
            Assert.AreSame(Connection, reported);
        }

        [Test]
        public void Apply_Reject_ReportsRejection()
        {
            bool? approved = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;

            Configure(maintenance: true);
            _gate.Apply(Connection, _gate.Decide("Bob", 0, 0f));

            Assert.IsFalse(approved);
        }

        [Test]
        public void Apply_NullConnection_ReportsNothing()
        {
            bool fired = false;
            _gate.OnAuthenticationResult += (_, _) => fired = true;

            _gate.Apply(null, _gate.Decide("Bob", 0, 0f));

            Assert.IsFalse(fired);
        }

        [Test]
        public void Apply_ClearsThePendingHandshake()
        {
            _gate.OnRemoteConnection(Connection);
            Assert.AreEqual(1, _gate.PendingHandshakes);

            _gate.Apply(Connection, _gate.Decide("Bob", 0, 0f));

            Assert.AreEqual(0, _gate.PendingHandshakes, "A resolved join must not be reaped later.");
        }

        [Test]
        public void HandleJoinRequest_ApprovesAnUnauthenticatedClient()
        {
            bool? approved = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;

            _gate.HandleJoinRequest(Connection, "Bob");

            Assert.AreEqual(true, approved);
        }

        [Test]
        public void HandleJoinRequest_NullConnection_IsIgnored()
        {
            Configure(maintenance: true);
            bool fired = false;
            _gate.OnAuthenticationResult += (_, _) => fired = true;

            _gate.HandleJoinRequest(null, "Bob");

            Assert.IsFalse(fired);
        }

        [Test]
        public void ReapTimedOutConnections_NothingPending_ReapsNothing()
        {
            Configure(timeout: 5f);

            Assert.AreEqual(0, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble + 100d));
        }

        [Test]
        public void ReapTimedOutConnections_StillInsideTimeout_ReapsNothing()
        {
            _gate.OnRemoteConnection(Connection);
            Configure(timeout: 5f);

            Assert.AreEqual(0, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble));
        }

        [Test]
        public void ReapTimedOutConnections_PastTimeout_ReportsTheExpiredHandshake()
        {
            _gate.OnRemoteConnection(Connection);
            Configure(timeout: 5f);

            Assert.AreEqual(1, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble + 6d));
        }

        [Test]
        public void ReapTimedOutConnections_IsAQuery_NotAConsume()
        {
            _gate.OnRemoteConnection(Connection);
            Configure(timeout: 5f);

            Assert.AreEqual(1, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble + 6d));
            Assert.AreEqual(1, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble + 6d),
                "An unmappable handshake keeps being reported until something clears it.");
        }
    }
}