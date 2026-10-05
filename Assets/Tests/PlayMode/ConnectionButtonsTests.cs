using System.Collections;
using System.Reflection;
using MMO.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class ConnectionButtonsTests
    {
        private GameObject _go;
        private ConnectionButtons _buttons;
        private FishNet.Managing.NetworkManager _manager;

        [UnitySetUp]
        public IEnumerator EnsureManagerIsListeningReady()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // JoinRequestClient subscribes in Start, so the first test of a session
            // must let a frame pass before any test clicks Host.
            yield return null;
        }

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ConnectionButtonsUnderTest");
            _buttons = _go.AddComponent<ConnectionButtons>();
            typeof(ConnectionButtons)
                .GetField("_networkManager", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(_buttons, _manager);
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            if (_go != null)
                Object.Destroy(_go);
        }

        [UnityTearDown]
        public IEnumerator DrainTeardown()
        {
            // Destroy is deferred and stopping a connection is asynchronous.
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnHostClicked_StartsHost_AndOnStopClicked_Stops()
        {
            _buttons.OnHostClicked();
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            Assert.IsTrue(_manager.IsHostStarted, "Expected host to be listening.");

            _buttons.OnStopClicked();
            yield return null;

            Assert.IsFalse(_manager.IsServerStarted || _manager.IsClientStarted, "Expected shutdown.");
        }

        [UnityTest]
        public IEnumerator OnHostClicked_ReachesTheJoinGate()
        {
            _buttons.OnHostClicked();
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            Assert.IsTrue(_manager.IsClientStarted,
                "Hosting through the UI must still clear the join gate.");
        }

        [UnityTest]
        public IEnumerator OnClientClicked_JoinsAnAlreadyRunningServer()
        {
            Assert.IsTrue(_manager.ServerManager.StartConnection(), "Server failed to start.");
            yield return null;
            Assert.IsTrue(_manager.IsServerStarted);

            _buttons.OnClientClicked();
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            Assert.IsTrue(_manager.IsClientStarted, "Expected the client to clear the join gate.");
            Assert.IsTrue(_manager.IsServerStarted, "OnClientClicked must not stop the server.");
        }

        [UnityTest]
        public IEnumerator OnClientClicked_DoesNotStartASecondConnection()
        {
            _buttons.OnHostClicked();
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            Assert.IsTrue(_manager.IsHostStarted);

            _buttons.OnClientClicked();
            yield return null;

            Assert.IsTrue(_manager.IsHostStarted,
                "OnClientClicked should be a no-op while already listening.");
        }

        [UnityTest]
        public IEnumerator OnClientClicked_WhileOffline_DoesNotStartAServer()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            _buttons.OnClientClicked();
            yield return null;

            Assert.IsFalse(_manager.IsServerStarted, "A client must never bring up a listener.");
        }

        [UnityTest]
        public IEnumerator OnStopClicked_WhileOffline_IsHarmless()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            _buttons.OnStopClicked();
            yield return null;

            Assert.IsTrue(_manager.IsOffline);
        }
    }
}