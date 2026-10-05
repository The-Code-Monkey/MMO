using System.Collections;
using System.Reflection;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using MMO.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class ConnectionButtonsPlayModeTests
    {
        private GameObject _managerGo;
        private NetworkManager _manager;
        private GameObject _buttonsGo;
        private ConnectionButtons _buttons;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _managerGo = new GameObject("ButtonsTestManager");
            _manager = _managerGo.AddComponent<NetworkManager>();
            _managerGo.AddComponent<Tugboat>();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null)
            {
                _manager.ServerManager.StopConnection(true);
                _manager.ClientManager.StopConnection();
            }

            yield return null;
            yield return null;

            if (_buttonsGo != null)
                Object.Destroy(_buttonsGo);
            if (_managerGo != null)
                Object.Destroy(_managerGo);
        }

        [UnityTest]
        public IEnumerator Start_WithoutNetworkManager_LogsError()
        {
            LogAssert.ExpectMessage(LogType.Error, "ConnectionButtons: no NetworkManager assigned.");

            _buttonsGo = new GameObject("Buttons");
            _buttons = _buttonsGo.AddComponent<ConnectionButtons>();

            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OnHostClicked_StartsHost()
        {
            CreateButtons(managerAssigned: true);
            yield return null;

            LogAssert.ExpectMessage(LogType.Log, "Host started.");
            _buttons.OnHostClicked();

            float timeout = 5f;
            while (!_manager.IsHostStarted && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_manager.IsHostStarted, "Host did not start.");
            Assert.IsTrue(_manager.IsServerStarted, "Server not started.");
            Assert.IsTrue(_manager.IsClientStarted, "Client not started.");
        }

        [UnityTest]
        public IEnumerator OnClientClicked_WhenAlreadyStarted_ReturnsEarly()
        {
            CreateButtons(managerAssigned: true);
            yield return null;

            _buttons.OnHostClicked();

            float timeout = 5f;
            while (!_manager.IsHostStarted && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_manager.IsHostStarted, "Host did not start.");

            _buttons.OnClientClicked();
            yield return null;

            Assert.IsTrue(_manager.IsHostStarted, "Host should remain started.");
            Assert.IsTrue(_manager.IsServerStarted, "Server should remain started.");
            Assert.IsTrue(_manager.IsClientStarted, "Client should remain started.");
        }

        [UnityTest]
        public IEnumerator OnStopClicked_StopsServerAndClient()
        {
            CreateButtons(managerAssigned: true);
            yield return null;

            _buttons.OnHostClicked();

            float timeout = 5f;
            while (!_manager.IsHostStarted && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_manager.IsHostStarted, "Host did not start.");

            _buttons.OnStopClicked();
            yield return null;
            yield return null;

            Assert.IsFalse(_manager.IsServerStarted, "Server should be stopped.");
            Assert.IsFalse(_manager.IsClientStarted, "Client should be stopped.");
        }

        private void CreateButtons(bool managerAssigned)
        {
            _buttonsGo = new GameObject("Buttons");
            _buttonsGo.SetActive(false);
            _buttons = _buttonsGo.AddComponent<ConnectionButtons>();

            if (managerAssigned)
            {
                var field = typeof(ConnectionButtons).GetField(
                    "_networkManager",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                field.SetValue(_buttons, _manager);
            }

            _buttonsGo.SetActive(true);
        }
    }
}
