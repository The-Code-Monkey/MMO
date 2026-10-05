using System.Collections;
using FishNet.Managing;
using MMO.Net;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The "NetworkManager" object with its lifecycle: built correct, wired correct, and
    /// able to listen. Awake has actually run here, unlike the EditMode baker tests.
    /// </summary>
    public sealed class NetworkManagerFactoryTests
    {
        [UnityTest]
        public IEnumerator Create_ProducesGameObjectNamed_NetworkManager()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.AreEqual(NetworkManagerFactory.GameObjectName, manager.gameObject.name);
        }

        [UnityTest]
        public IEnumerator Create_CompletesAwake()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsTrue(manager.Initialized, "NetworkManager.Awake must complete.");
            Assert.IsNotNull(manager.SpawnablePrefabs,
                "A null SpawnablePrefabs makes Awake abort, leaving a dead object.");
        }

        [UnityTest]
        public IEnumerator Create_RegistersItselfAsTheOnlyInstance()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.AreEqual(1, NetworkManager.Instances.Count);
            Assert.AreSame(manager, NetworkManager.Instances[0]);
        }

        [UnityTest]
        public IEnumerator Create_StartsOffline()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsTrue(manager.IsOffline);
            Assert.IsFalse(manager.IsServerStarted);
            Assert.IsFalse(manager.IsClientStarted);
        }

        [UnityTest]
        public IEnumerator Create_UsesTugboatOnTheConfiguredPort()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsNotNull(NetworkManagerTestHelper.Transport(manager),
                "TransportManager must adopt the Tugboat the factory added.");
            Assert.AreEqual(NetworkManagerTestHelper.TestPort, NetworkManagerTestHelper.Transport(manager).GetPort());
        }

        [UnityTest]
        public IEnumerator Create_RegistersTheGateAsAuthenticator()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsInstanceOf<MMOConnectionGate>(manager.ServerManager.GetAuthenticator(),
                "Without a registered Authenticator FishNet auto-approves every client.");
        }

        [UnityTest]
        public IEnumerator Create_AddsTheClientHalfOfTheHandshake()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsNotNull(manager.gameObject.GetComponent<JoinRequestClient>());
        }

        [UnityTest]
        public IEnumerator Create_HandsTheSettingsToTheGate()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            MMOConnectionGate gate = NetworkManagerTestHelper.Gate(manager);
            Assert.IsNotNull(gate);
            Assert.AreEqual(NetworkManagerTestHelper.TestPort, gate.Settings.Port);
            Assert.AreEqual(MMOConnectionSettings.DefaultMaxClients, gate.Settings.MaxClients);
        }

        [UnityTest]
        public IEnumerator Create_IsSafeToCallTwice()
        {
            NetworkManager first = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            NetworkManager second = NetworkManagerTestHelper.GetOrCreate();

            Assert.AreSame(first, second, "The helper must reuse the single allowed instance.");
        }

        [UnityTest]
        public IEnumerator ServerManager_CanStartAndStopAListener()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsTrue(manager.ServerManager.StartConnection(), "Server failed to start.");
            yield return null;
            Assert.IsTrue(manager.IsServerStarted);

            manager.ServerManager.StopConnection(true);
            yield return null;
            Assert.IsFalse(manager.IsServerStarted);
            Assert.IsTrue(manager.IsOffline);
        }
    }
}