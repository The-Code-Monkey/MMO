using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class FishNetSmokeTests
    {
        [UnityTest]
        public IEnumerator NetworkManager_CanStartAndStopAHost()
        {
            var go = new GameObject("TestNetworkManager");
            var manager = go.AddComponent<FishNet.NetworkManager>();
            var transport = go.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();

            manager.TransportManager.Transport = transport;
            go.SetActive(true);

            Assert.IsTrue(manager.ServerManager.StartConnection(), "Server failed to start.");
            Assert.IsTrue(manager.ClientManager.StartConnection(), "Client failed to start.");
            yield return null;

            Assert.IsTrue(manager.IsServerStarted, "Expected server started after hosting.");
            Assert.IsTrue(manager.IsHostStarted, "Expected host after hosting.");
            Assert.IsNotNull(manager.ClientManager.Connection, "Local client not connected.");
            Assert.IsTrue(manager.ClientManager.Connection.IsActive, "Local client not connected.");

            manager.ServerManager.StopConnection(true);
            manager.ClientManager.StopConnection();
            yield return null;

            Assert.IsFalse(manager.IsServerStarted || manager.IsClientStarted, "Expected shutdown.");

            Object.Destroy(go);
        }

        [Test]
        public void Manifest_DeclaresFishNet()
        {
            // Guards against someone removing the git URL dependency.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(FishNet.NetworkManager).Assembly);

            Assert.IsNotNull(info, "FishNet package not resolved.");
            StringAssert.StartsWith("4.7.3", info.version,
                $"Expected FishNet 4.7.3, found {info.version}.");
        }
    }
}
