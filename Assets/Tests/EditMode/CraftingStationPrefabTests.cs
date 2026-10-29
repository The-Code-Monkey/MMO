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
    /// grid cell — so a world-sized value carves a hole twice as large as intended.
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
                Assert.AreEqual(CraftingStationPrefabFactory.BodyLocalCenter, obstacle.center,
                    "The obstacle must use the body's centre so the tall forge starts on the floor.");

                Vector3 carved = Vector3.Scale(obstacle.size, station.transform.lossyScale);
                Vector3 body = Vector3.Scale(CraftingStationPrefabFactory.BodyLocalSize, station.transform.lossyScale);
                Assert.AreEqual(body, carved,
                    "NavMeshObstacle.size is scaled by the transform: the carve must match the body's footprint.");

                BoxCollider collider = station.GetComponent<BoxCollider>();
                Assert.IsNotNull(collider, "The station still needs a solid collider for clicks and movement.");
                Assert.AreEqual(CraftingStationPrefabFactory.BodyLocalSize, collider.size,
                    "The collider must use the same local body size as the carving obstacle.");
                Assert.AreEqual(CraftingStationPrefabFactory.BodyLocalCenter, collider.center,
                    "The collider must use the same local body centre as the carving obstacle.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }

        [Test]
        public void CraftingStation_HasBothVisualVariants()
        {
            NetworkObject station = CraftingStationPrefabFactory.Create();
            try
            {
                Assert.IsNull(station.GetComponent<MeshRenderer>(),
                    "The root is the body; the anvil and furnace models live under Visuals.");

                Assert.IsNotNull(station.transform.Find("Visuals/Furnace"),
                    "A furnace station must have a furnace model to show.");
                Assert.IsNotNull(station.transform.Find("Visuals/Anvil"),
                    "An anvil station must have an anvil model to show.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }
    }
}
