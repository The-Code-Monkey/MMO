using System.Collections;
using System.Threading;
using FishNet.Managing;
using MMO.Net;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The zone-transfer wiring the factory does, verified over a real NetworkManager object:
    /// the gate consumes the same store the trigger issues through, the client handler rides the
    /// object, and a token minted through the service validates through the gate — the cross-server
    /// handoff in one process.
    /// </summary>
    public sealed class ZoneTransferWiringTests
    {
        [UnityTest]
        public IEnumerator Create_WiresTheGateToTheTokenStore()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            var service = manager.gameObject.GetComponent<ZoneTransferTokenService>();
            MMOConnectionGate gate = NetworkManagerTestHelper.Gate(manager);

            Assert.IsNotNull(service);
            Assert.AreSame(service.Store, gate.TransferTokenStore,
                "The gate must consume the same store the trigger issues through.");
        }

        [UnityTest]
        public IEnumerator Create_AddsTheClientTransferHandler()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            Assert.IsNotNull(manager.gameObject.GetComponent<ZoneTransferClient>());
        }

        [UnityTest]
        public IEnumerator TokenIssuedByTheService_IsConsumableByTheGate()
        {
            NetworkManager manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            var service = manager.gameObject.GetComponent<ZoneTransferTokenService>();
            MMOConnectionGate gate = NetworkManagerTestHelper.Gate(manager);
            string issued = service.Store
                .IssueAsync(new ZoneTransferToken { AccountId = "wired-acc" }, CancellationToken.None)
                .GetAwaiter().GetResult();

            ZoneTransferToken consumed = gate.TransferTokenStore
                .ConsumeAsync(issued, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsNotNull(consumed);
            Assert.AreEqual("wired-acc", consumed.AccountId);
        }
    }
}