using System.Collections;
using MMO.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class NetworkManagerFactoryTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.Destroy(_go);
        }

        [UnityTest]
        public IEnumerator Create_NamesObjectNetworkManager_AndWiresComponents()
        {
            var manager = NetworkManagerFactory.Create();
            _go = manager.gameObject;
            yield return null;

            Assert.AreEqual("NetworkManager", manager.gameObject.name);
            Assert.IsNotNull(manager.gameObject.GetComponent<FishNet.Transporting.Tugboat.Tugboat>());
            Assert.IsNotNull(manager.gameObject.GetComponent<JoinGateAuthenticator>());
            Assert.IsNotNull(manager.gameObject.GetComponent<JoinRequestClient>());
            Assert.IsNotNull(manager.SpawnablePrefabs);
            Assert.IsTrue(manager.Initialized);
            Assert.IsNotNull(manager.TransportManager);
            Assert.IsNotNull(manager.TransportManager.Transport);
        }
    }
}
