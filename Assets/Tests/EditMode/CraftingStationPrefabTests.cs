using FishNet.Object;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Tests
{
    /// <summary>
    /// The crafting station's NavMeshObstacle is what stops players from walking through it:
    /// the mover routes across the NavMesh, and a carved hole in that mesh is the only thing
    /// that makes the route bend around the station.
    ///
    /// <para>
    /// The trap this guards against: <c>NavMeshObstacle.size</c> is measured in the object's
    /// local space and is scaled by the transform, and the station's root is scaled to a 2x1x2
    /// body — so a size of <c>DefaultScale</c> carves a 4x4 hole around a 2x1x2 station.
    /// </para>
    /// </summary>
    public sealed class CraftingStationPrefabTests
    {
        [Test]
        public void CraftingStation_CarvesTheBodyFootprint()
        {
            NetworkObject station = CraftingStationPrefabFactory.Create();
            try
            {
                NavMeshObstacle obstacle = station.GetComponent<NavMeshObstacle>();
                Assert.IsNotNull(obstacle,
                    "The station must carry a NavMeshObstacle, or the NavMesh keeps a walkable patch under it.");
                Assert.IsTrue(obstacle.carving,
                    "Without carving the obstacle only nudges agents aside; the route still runs through the station.");
                Assert.AreEqual(Vector3.zero, obstacle.center,
                    "The obstacle must be centred on the body, or it floats above the floor and carves nothing.");

                Vector3 carved = Vector3.Scale(obstacle.size, station.transform.lossyScale);
                Assert.AreEqual(CraftingStationPrefabFactory.DefaultScale, carved,
                    "NavMeshObstacle.size is scaled by the transform: the carve must match the body's footprint.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }
    }
}
