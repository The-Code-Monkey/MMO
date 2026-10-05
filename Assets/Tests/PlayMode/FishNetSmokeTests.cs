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
            var transport = go.AddComponent<FishNet.Transport.Tugboat.Tugboat>();

            manager.NetworkConfig ??= new();
            manager.NetworkConfig.Transport = transport;
            go.SetActive(true);

            Assert.IsTrue(manager.StartHost(), "Host failed to start.");
            Assert.IsTrue(manager.IsServer, "Expected IsServer after hosting.");
            Assert.IsTrue(manager.IsHost, "Expected IsHost after hosting.");
            Assert.IsTrue(manager.LocalClient.IsConnected, "Local client not connected.");

            manager.StopAll();
            yield return null;

            Assert.IsFalse(manager.IsListening, "Expected shutdown.");

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
