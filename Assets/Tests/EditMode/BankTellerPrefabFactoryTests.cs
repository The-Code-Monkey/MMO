using FishNet.Object;
using MMO.Banking;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Tests
{
    /// <summary>
    /// The bank teller placeholder: a capsule carrying a network object and the interaction
    /// component, with a solid collider for clicks and a carved obstacle so players path around it.
    /// </summary>
    public sealed class BankTellerPrefabFactoryTests
    {
        [Test]
        public void Create_BuildsANetworkedTellerWithASolidCollider()
        {
            NetworkObject teller = BankTellerPrefabFactory.Create();
            try
            {
                Assert.IsNotNull(teller.GetComponent<NetworkObject>());
                Assert.IsNotNull(teller.GetComponent<BankTeller>());

                CapsuleCollider collider = teller.GetComponent<CapsuleCollider>();
                Assert.IsNotNull(collider);
                Assert.IsFalse(collider.isTrigger, "OnMouseDown and the click raycast need a solid collider.");

                NavMeshObstacle obstacle = teller.GetComponent<NavMeshObstacle>();
                Assert.IsNotNull(obstacle);
                Assert.IsTrue(obstacle.carving);
            }
            finally
            {
                Object.DestroyImmediate(teller.gameObject);
            }
        }
    }
}
