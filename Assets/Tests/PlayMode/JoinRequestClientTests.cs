using System.Collections;
using FishNet.Managing;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The client half of the handshake. The gate turns away everyone who stays silent, so
    /// this component is load bearing rather than a convenience.
    /// </summary>
    public sealed class JoinRequestClientTests
    {
        private GameObject _go;
        private JoinRequestClient _detached;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("JoinRequestClientUnderTest");
            _detached = _go.AddComponent<JoinRequestClient>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        /// <summary>The instance the factory put on the NetworkManager object.</summary>
        private static JoinRequestClient Attached(NetworkManager manager)
            => manager.gameObject.GetComponent<JoinRequestClient>();

        [Test]
        public void Defaults_ToPlayer()
        {
            Assert.AreEqual("Player", _detached.PlayerName);
        }

        [Test]
        public void SetPlayerName_StoresTheName()
        {
            _detached.SetPlayerName("Bob");

            Assert.AreEqual("Bob", _detached.PlayerName);
        }

        [Test]
        public void SetPlayerName_Null_BecomesEmpty()
        {
            _detached.SetPlayerName(null);

            Assert.AreEqual(string.Empty, _detached.PlayerName);
        }

        [Test]
        public void SendJoinRequest_WithoutANetworkManager_DoesNothing()
        {
            Assert.IsFalse(_detached.SendJoinRequest());
        }

        [UnityTest]
        public IEnumerator Factory_AttachesOneToTheNetworkManagerObject()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsNotNull(Attached(manager));
            Assert.AreNotSame(_detached, Attached(manager));
        }

        [UnityTest]
        public IEnumerator SendJoinRequest_WhileOffline_DoesNothing()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsFalse(Attached(manager).SendJoinRequest(), "There is no socket to announce over.");
            Assert.IsTrue(manager.IsOffline);
        }

        [UnityTest]
        public IEnumerator SendJoinRequest_OnceConnected_Succeeds()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            NetworkManagerTestHelper.StartHost(manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(manager);

            Assert.IsTrue(manager.IsClientStarted);
            Assert.IsTrue(Attached(manager).SendJoinRequest(),
                "An active client connection can always announce itself.");

            NetworkManagerTestHelper.StopIfListening(manager);
        }

        [UnityTest]
        public IEnumerator Host_AnnouncesItself_AndGetsAdmitted()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            NetworkManagerTestHelper.StartHost(manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(manager);

            Assert.IsTrue(manager.IsHostStarted,
                "The factory's own JoinRequestClient is what gets the host past the gate.");

            NetworkManagerTestHelper.StopIfListening(manager);
        }
    }
}