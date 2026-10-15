using System;
using System.Collections;
using System.Threading.Tasks;
using FishNet.Managing;
using MMO.Bootstrap;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The login flow against a real (local) socket: the connector must aim the transport at the
    /// zone the backend vouched for, carry the session token into the join handshake, and only ever
    /// record a zone that the server actually admitted a connection to. Refusals — validation,
    /// backend rejection, unreachable zone — must all come back as clean <see cref="LoginResult.Fail"/>
    /// values with the form unlocked.
    /// </summary>
    public sealed class LoginConnectorPlayModeTests
    {
        private NetworkManager _manager;
        private FakeApiGateway _gateway;
        private LoginConnector _connector;

        [UnitySetUp]
        public IEnumerator EnsureManagerReady()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;
        }

        [SetUp]
        public void SetUp()
        {
            _gateway = new FakeApiGateway();
            _connector = new LoginConnector(_manager, _gateway);
        }

        [TearDown]
        public void TearDown()
        {
            // The manager and its join client are shared across the whole PlayMode suite; a stale
            // session token would pollute a later suite's wire traffic.
            _manager?.gameObject.GetComponent<JoinRequestClient>()?.SetAuthToken(null);
            NetworkManagerTestHelper.StopIfListening(_manager);
        }

        [UnityTearDown]
        public IEnumerator DrainTeardown()
        {
            yield return null;
        }

        [UnityTest]
        public IEnumerator Success_AppliesStoredZone_Connects_AndRecordsIt()
        {
            var stored = LastZoneInfo.Create("127.0.0.1", NetworkManagerTestHelper.TestPort);
            Assert.IsTrue(_manager.ServerManager.StartConnection(), "Zone server failed to start.");
            yield return null;

            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", stored);

            string last = null;
            _connector.StatusChanged += s => last = s;

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123");
            yield return new WaitForTask(task);

            LoginResult result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.Success);
            Assert.IsTrue(_manager.IsClientStarted, "The client must be admitted to the local zone.");
            Assert.AreEqual("Bob", _gateway.LastLoginUsername);
            Assert.AreEqual("password123", _gateway.LastLoginPassword);

            Assert.AreEqual(1, _gateway.SaveCalls, "A reached zone is recorded exactly once.");
            Assert.AreEqual("user-1", _gateway.SavedUserId);
            Assert.AreEqual("tok-1", _gateway.SavedToken);
            Assert.AreEqual(stored.Port, _gateway.SavedZone.Port);
            Assert.AreEqual("Connected to 127.0.0.1:" + NetworkManagerTestHelper.TestPort, last);
        }

        [UnityTest]
        public IEnumerator Success_StoredZoneWinsOverTheFallback()
        {
            var stored = LastZoneInfo.Create("127.0.0.1", NetworkManagerTestHelper.TestPort);
            var fallback = LastZoneInfo.Create("10.9.9.9", 5555);
            Assert.IsTrue(_manager.ServerManager.StartConnection());
            yield return null;

            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", stored);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123", fallback);
            yield return new WaitForTask(task);

            Assert.IsTrue(task.GetAwaiter().GetResult().Success);
            Assert.AreEqual(stored.Port, _gateway.SavedZone.Port,
                "The backend's record of where this player stood must beat the menu default.");
        }

        [UnityTest]
        public IEnumerator Success_FirstEverLogin_FallsBackToTheMenuDefaultZone()
        {
            var fallback = LastZoneInfo.Create("127.0.0.1", NetworkManagerTestHelper.TestPort);
            Assert.IsTrue(_manager.ServerManager.StartConnection());
            yield return null;

            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", zone: null);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123", fallback);
            yield return new WaitForTask(task);

            Assert.IsTrue(task.GetAwaiter().GetResult().Success);
            Assert.AreEqual(fallback.Port, _gateway.SavedZone.Port,
                "With nothing stored, the menu default is what gets reached and recorded.");
        }

        [UnityTest]
        public IEnumerator StoredZoneUnreachable_FailsWithoutRecordingAnything()
        {
            var deadZone = LastZoneInfo.Create("127.0.0.1", 1);
            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", deadZone);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123");
            yield return new WaitForTask(task);

            LoginResult result = task.GetAwaiter().GetResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Could not reach the zone server. Try again.", result.Error);
            Assert.AreEqual(0, _gateway.SaveCalls,
                "A zone that was never reached must not be recorded as 'last standing in'.");
            Assert.IsFalse(_manager.IsClientStarted);
        }

        [UnityTest]
        public IEnumerator StaleStoredZone_FallsBackToTheMenuDefaultZone_AndRecordsIt()
        {
            // A stored zone can point at a raid container that has since been torn down; its
            // port is then dead forever. The menu default zone is live.
            var staleRaid = LastZoneInfo.Create("127.0.0.1", 1);
            var fallback = LastZoneInfo.Create("127.0.0.1", NetworkManagerTestHelper.TestPort);
            Assert.IsTrue(_manager.ServerManager.StartConnection(), "Zone server failed to start.");
            yield return null;

            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", staleRaid);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123", fallback);
            yield return new WaitForTask(task);

            LoginResult result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.Success,
                "An unreachable stored zone must fall back to the menu's default zone.");
            Assert.AreEqual(fallback.Port, _gateway.SavedZone.Port,
                "The fallback is what was reached, so the fallback is what gets recorded.");
            Assert.AreEqual(1, _gateway.SaveCalls);
        }

        [UnityTest]
        public IEnumerator StaleStoredZone_AndDeadFallback_FailsWithoutRecordingAnything()
        {
            var staleRaid = LastZoneInfo.Create("127.0.0.1", 1);
            var deadFallback = LastZoneInfo.Create("127.0.0.1", 2);
            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", staleRaid);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123", deadFallback);
            yield return new WaitForTask(task);

            LoginResult result = task.GetAwaiter().GetResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Could not reach the zone server. Try again.", result.Error);
            Assert.AreEqual(0, _gateway.SaveCalls,
                "Neither zone was reached, so nothing may be recorded.");
            Assert.IsFalse(_manager.IsClientStarted);
        }

        [UnityTest]
        public IEnumerator StoredZoneEqualToFallback_Unreachable_FailsWithoutRetry()
        {
            // Retrying against the very zone that just failed is pointless; the connector must
            // not burn a second attempt when stored zone and fallback are the same place.
            var zone = LastZoneInfo.Create("127.0.0.1", 1);
            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Success("tok-1", "user-1", "Bob", zone);

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123", zone);
            yield return new WaitForTask(task);

            LoginResult result = task.GetAwaiter().GetResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, _gateway.LoginCalls);
            Assert.AreEqual(0, _gateway.SaveCalls);
            Assert.IsFalse(_manager.IsClientStarted);
        }

        [UnityTest]
        public IEnumerator InvalidCredentials_FailBeforeAnyConnectionAttempt()
        {
            _gateway.OnLogin = (_, _, _) => FakeApiGateway.Failure("Invalid credentials.");

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123");
            yield return new WaitForTask(task);

            Assert.AreEqual("Invalid credentials.", task.GetAwaiter().GetResult().Error);
            Assert.IsTrue(_manager.IsOffline, "A refused backend must never bring up a connection.");
        }

        [UnityTest]
        public IEnumerator LocalValidationFailure_NeverTouchesTheGateway()
        {
            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob Smith", "password123");
            yield return new WaitForTask(task);

            Assert.IsFalse(task.GetAwaiter().GetResult().Success);
            Assert.AreEqual(0, _gateway.LoginCalls, "A name the client rules reject is not sent anywhere.");
            Assert.IsTrue(_manager.IsOffline);
            Assert.IsFalse(_connector.IsBusy, "A finished login always unlocks the form.");
        }

        [UnityTest]
        public IEnumerator GatewayThrowing_FailsCleanly()
        {
            _gateway.OnLogin = (_, _, _) => throw new InvalidOperationException("backend exploded");

            Task<LoginResult> task = _connector.LoginAndConnectAsync("Bob", "password123");
            yield return new WaitForTask(task);

            Assert.AreEqual(false, task.GetAwaiter().GetResult().Success);
            Assert.IsFalse(_connector.IsBusy);
            Assert.IsTrue(_manager.IsOffline);
        }

        [UnityTest]
        public IEnumerator SecondSubmitWhileBusy_IsRefused()
        {
            _gateway.BlockLogin();

            Task<LoginResult> first = _connector.LoginAndConnectAsync("Bob", "password123");
            Task<LoginResult> second = _connector.LoginAndConnectAsync("Carol", "password456");
            yield return new WaitForTask(second);

            Assert.AreEqual("A login is already in progress.", second.GetAwaiter().GetResult().Error);
            Assert.IsTrue(_connector.IsBusy);
            Assert.AreEqual(1, _gateway.LoginCalls);

            _gateway.ReleaseLogin(FakeApiGateway.Success().GetAwaiter().GetResult());
            yield return new WaitForTask(first);
            Assert.IsFalse(_connector.IsBusy, "Releasing the blocked login re-opens the form.");
        }
    }

    /// <summary>Coroutine wait for a <see cref="Task"/>, since async/await and UnityTest mix poorly.</summary>
    internal sealed class WaitForTask : UnityEngine.CustomYieldInstruction
    {
        private readonly Task _task;

        public WaitForTask(Task task) => _task = task;

        public override bool keepWaiting => !_task.IsCompleted;
    }
}