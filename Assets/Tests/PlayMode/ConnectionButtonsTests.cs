using System.Collections;
using MMO.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class ConnectionButtonsTests
    {
        private GameObject _go;
        private FishNet.NetworkManager _manager;
        private ConnectionButtons _buttons;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ConnectionButtonsTest");
            _manager = _go.AddComponent<FishNet.NetworkManager>();
            var transport = _go.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();
            _manager.TransportManager.Transport = transport;
            _buttons = _go.AddComponent<ConnectionButtons>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_manager != null && (_manager.IsServerStarted || _manager.IsClientStarted))
            {
                _manager.ServerManager.StopConnection(true);
                _manager.ClientManager.StopConnection();
            }
            if (_go != null)
                Object.Destroy(_go);
        }

        [UnityTest]
        public IEnumerator OnHostClicked_StartsHost_AndOnStopClicked_Stops()
        {
            SetNetworkManagerField();

            _buttons.OnHostClicked();
            yield return null;

            Assert.IsTrue(_manager.IsServerStarted || _manager.IsClientStarted, "Expected host to be listening.");

            _buttons.OnStopClicked();
            yield return null;

            Assert.IsFalse(_manager.IsServerStarted || _manager.IsClientStarted, "Expected shutdown.");
        }

        [UnityTest]
        public IEnumerator OnClientClicked_DoesNotStartSecondTransport()
        {
            SetNetworkManagerField();

            _buttons.OnHostClicked();
            yield return null;
            Assert.IsTrue(_manager.IsServerStarted || _manager.IsClientStarted);

            var clientState = _manager.IsClientStarted;
            _buttons.OnClientClicked();
            yield return null;

            Assert.AreEqual(clientState, _manager.IsClientStarted,
                "OnClientClicked should be a no-op while already listening.");
        }

        private void SetNetworkManagerField()
        {
            var field = typeof(ConnectionButtons).GetField("_networkManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(_buttons, _manager);
        }
    }
}
