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
            var manager = go.AddComponent<FishNet.Managing.NetworkManager>();
            go.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();

            Assert.IsTrue(manager.ServerManager.StartConnection(), "Server failed to start.");
            yield return null;
            Assert.IsTrue(manager.ClientManager.StartConnection(), "Client failed to start.");

            float timeout = 5f;
            while (!manager.IsHostStarted && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(manager.IsHostStarted, "Host did not start within timeout.");
            Assert.IsTrue(manager.IsServerStarted, "Expected IsServerStarted after hosting.");
            Assert.IsTrue(manager.IsClientStarted, "Expected IsClientStarted after hosting.");
            Assert.IsTrue(manager.ClientManager.Connection.IsActive, "Local client not connected.");

            manager.ServerManager.StopConnection(true);
            manager.ClientManager.StopConnection();
            yield return null;
            yield return null;

            Assert.IsFalse(manager.IsServerStarted, "Expected server shutdown.");
            Assert.IsFalse(manager.IsClientStarted, "Expected client shutdown.");

            Object.Destroy(go);
        }

        [Test]
        public void Manifest_DeclaresFishNet()
        {
            // Guards against someone removing the git URL dependency.
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(FishNet.Managing.NetworkManager).Assembly);

            Assert.IsNotNull(info, "FishNet package not resolved.");
            StringAssert.StartsWith("4.7.3", info.version,
                $"Expected FishNet 4.7.3, found {info.version}.");
        }
    }
}
