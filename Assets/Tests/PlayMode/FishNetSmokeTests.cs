using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    public sealed class FishNetSmokeTests
    {
        [UnityTest]
        public IEnumerator NetworkManager_CanStartAndStopAHost()
        {
            FishNet.Managing.NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            NetworkManagerTestHelper.StartHost(manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(manager);

            Assert.IsTrue(manager.IsServerStarted, "Expected server started after hosting.");
            Assert.IsTrue(manager.IsHostStarted, "Expected host after hosting.");
            Assert.IsNotNull(manager.ClientManager.Connection, "Local client not connected.");
            Assert.IsTrue(manager.ClientManager.Connection.IsActive, "Local client not connected.");

            NetworkManagerTestHelper.StopIfListening(manager);
            yield return null;

            Assert.IsFalse(manager.IsServerStarted || manager.IsClientStarted, "Expected shutdown.");
            Assert.IsTrue(manager.IsOffline);
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