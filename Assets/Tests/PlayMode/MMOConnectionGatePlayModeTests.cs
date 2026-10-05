using System.Collections;
using FishNet.Connection;
using FishNet.Managing;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The gate against a real, running server. The headline test is the handshake: a host
    /// is not authenticated until it has announced itself and been approved, so a green
    /// <c>IsClientStarted</c> means the whole chain works — authenticator registration, the
    /// unauthenticated broadcast, the policy, and promotion by FishNet.
    /// </summary>
    public sealed class MMOConnectionGatePlayModeTests
    {
        private NetworkManager _manager;
        private MMOConnectionGate _gate;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            _gate = NetworkManagerTestHelper.Gate(_manager);
            Assert.IsNotNull(_gate, "The factory must register the gate as the Authenticator.");

            // JoinRequestClient subscribes in Start, so a connection started in the same
            // frame the manager was created would never announce itself.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            _gate.SetSettings(new MMOConnectionSettings(
                NetworkManagerTestHelper.TestPort,
                MMOConnectionSettings.DefaultMaxClients,
                NetworkManagerTestHelper.TestHandshakeTimeoutSeconds));

            // Stopping a connection is asynchronous; give it a frame so the next test
            // starts from a genuinely offline manager.
            yield return null;
        }

        private IEnumerator StartAuthenticatedHost()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
        }

        [UnityTest]
        public IEnumerator Host_CompletesTheJoinHandshake()
        {
            yield return StartAuthenticatedHost();

            Assert.IsTrue(_manager.IsServerStarted, "Server should be up.");
            Assert.IsTrue(_manager.IsHostStarted,
                "The gate must admit the host; otherwise IsClientStarted never flips.");
            Assert.IsTrue(_manager.ClientManager.Connection.IsAuthenticated);
        }

        [UnityTest]
        public IEnumerator Host_LeavesNoPendingHandshake()
        {
            yield return StartAuthenticatedHost();

            Assert.AreEqual(0, _gate.PendingHandshakes,
                "A resolved handshake must be cleared or the reaper will turn the host away.");
        }

        [UnityTest]
        public IEnumerator Gate_Approves_TheHost_DuringTheHandshake()
        {
            bool? approved = null;
            NetworkConnection reported = null;
            _gate.OnAuthenticationResult += (conn, ok) =>
            {
                reported = conn;
                approved = ok;
            };

            yield return StartAuthenticatedHost();

            Assert.AreEqual(true, approved, "The gate must approve a valid host.");
            Assert.IsNotNull(reported);
            Assert.IsTrue(reported.IsAuthenticated, "FishNet promotes the connection after approval.");
            Assert.AreSame(_manager.ClientManager.Connection, reported);
        }

        [UnityTest]
        public IEnumerator Decide_Approves_AValidPlayer()
        {
            yield return StartAuthenticatedHost();

            Assert.IsTrue(_gate.Decide("Bob", 0, 0f).Approved);
        }

        [UnityTest]
        public IEnumerator Decide_Rejects_BlockedPlayer()
        {
            yield return StartAuthenticatedHost();
            _gate.SetSettings(new MMOConnectionSettings(
                NetworkManagerTestHelper.TestPort,
                MMOConnectionSettings.DefaultMaxClients,
                NetworkManagerTestHelper.TestHandshakeTimeoutSeconds,
                blockedPlayers: new[] { "Bob" }));

            JoinDecision decision = _gate.Decide("Bob", 0, 0f);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(JoinPolicy.ReasonBlocked, decision.Reason);
        }

        [UnityTest]
        public IEnumerator Decide_Rejects_WhileInMaintenanceMode()
        {
            yield return StartAuthenticatedHost();
            _gate.SetSettings(new MMOConnectionSettings(
                NetworkManagerTestHelper.TestPort,
                MMOConnectionSettings.DefaultMaxClients,
                NetworkManagerTestHelper.TestHandshakeTimeoutSeconds,
                maintenanceMode: true));

            Assert.AreEqual(JoinPolicy.ReasonMaintenance, _gate.Decide("Bob", 0, 0f).Reason);
        }

        [UnityTest]
        public IEnumerator Decide_Rejects_WhenServerIsFull()
        {
            yield return StartAuthenticatedHost();

            JoinDecision decision = _gate.Decide("Bob", _gate.Settings.MaxClients, 0f);

            Assert.AreEqual(JoinPolicy.ReasonFull, decision.Reason);
        }

        [UnityTest]
        public IEnumerator Apply_Reject_ReportsTheVerdict_WithoutDisturbingTheHost()
        {
            yield return StartAuthenticatedHost();
            _gate.SetSettings(new MMOConnectionSettings(
                NetworkManagerTestHelper.TestPort,
                MMOConnectionSettings.DefaultMaxClients,
                NetworkManagerTestHelper.TestHandshakeTimeoutSeconds,
                maintenanceMode: true));

            bool? approved = null;
            _gate.OnAuthenticationResult += (_, ok) => approved = ok;

            // EmptyConnection is a real NetworkConnection that sits on no socket, so the
            // rejection path runs end to end without disconnecting the host.
            _gate.Apply(NetworkManager.EmptyConnection, _gate.Decide("Bob", 0, 0f));

            Assert.AreEqual(false, approved);
            Assert.IsTrue(_manager.IsHostStarted, "An unrelated client must be unaffected.");
        }

        [UnityTest]
        public IEnumerator Update_DoesNotReapOnceTheHostIsIn()
        {
            yield return StartAuthenticatedHost();

            yield return null;

            Assert.AreEqual(0, _gate.ReapTimedOutConnections(Time.unscaledTimeAsDouble));
            Assert.IsTrue(_manager.IsHostStarted);
        }
    }
}