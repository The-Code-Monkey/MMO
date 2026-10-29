using FishNet.Object;
using MMO.Crafting;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Where the player has to stand to use a crafting station, and the reach that decides whether
    /// the server answers the request at all.
    ///
    /// <para>
    /// The trap this guards against: the player and the station do not share a pivot height. The
    /// player's transform is the capsule's middle, a metre above the floor, while the station's
    /// transform is the middle of its body, half a metre up. A stand-off measured from the
    /// station's pivot is therefore <em>further</em> from the player's pivot than the number says,
    /// and a stand-off equal to the reach lands outside it — the server refuses to open the
    /// station and no panel ever appears.
    /// </para>
    /// </summary>
    public sealed class CraftingStationReachTests
    {
        /// <summary>
        /// The player's pivot is the capsule's middle, so it stands this far above the floor the
        /// player is standing on.
        /// </summary>
        private static float PivotHeight => PlayerPrefabFactory.DefaultHeight / 2f;

        [Test]
        public void TheStandOffPoint_LeavesThePlayerInsideTheCraftingReach()
        {
            NetworkObject station = CraftingStationPrefabFactory.Create();
            try
            {
                station.transform.position = new Vector3(4f, CraftingStationPrefabFactory.DefaultHeight / 2f, 4f);

                CraftingStation component = station.GetComponent<CraftingStation>();
                Assert.IsNotNull(component, "The station prefab must carry the component the player interacts with.");

                // The player walks onto the stand-off point and its pivot is a metre above that.
                Vector3 playerPosition = component.StandOffPosition + Vector3.up * PivotHeight;

                Assert.LessOrEqual(
                    Vector3.Distance(playerPosition, station.transform.position),
                    PlayerCrafting.MaxCraftDistance,
                    "The walk must end inside the crafting reach, or the server refuses the open and the panel never appears.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }

        [Test]
        public void TheStandOffPoint_StandsInFrontOfTheStation()
        {
            NetworkObject station = CraftingStationPrefabFactory.Create();
            try
            {
                station.transform.position = new Vector3(4f, CraftingStationPrefabFactory.DefaultHeight / 2f, 4f);

                CraftingStation component = station.GetComponent<CraftingStation>();

                Assert.Greater(component.StandOffPosition.z, station.transform.position.z,
                    "The player must stand on the station's working side, not inside it.");
                Assert.GreaterOrEqual(component.StandOffDistance, CraftingStationPrefabFactory.DefaultDepth / 2f,
                    "The stand-off must clear the station's own body.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }

        [Test]
        public void AStandOffBeyondTheReach_IsRefused()
        {
            NetworkObject station = CraftingStationPrefabFactory.Create();
            try
            {
                station.transform.position = new Vector3(4f, CraftingStationPrefabFactory.DefaultHeight / 2f, 4f);

                Vector3 farStandOff = station.transform.position
                    - station.transform.forward * (PlayerCrafting.MaxCraftDistance + 1f);
                Vector3 playerPosition = farStandOff + Vector3.up * PivotHeight;

                Assert.Greater(
                    Vector3.Distance(playerPosition, station.transform.position),
                    PlayerCrafting.MaxCraftDistance,
                    "A stand-off beyond the reach is not one the player can use.");
            }
            finally
            {
                Object.DestroyImmediate(station.gameObject);
            }
        }

        [Test]
        public void IsWithinReach_AcceptsAStationOnTheReach()
        {
            Assert.IsTrue(PlayerCrafting.IsWithinReach(Vector3.zero, Vector3.forward * PlayerCrafting.MaxCraftDistance),
                "The reach is inclusive: a player exactly at it can still use the station.");
        }

        [Test]
        public void IsWithinReach_RefusesAStationBeyondTheReach()
        {
            Assert.IsFalse(PlayerCrafting.IsWithinReach(Vector3.zero, Vector3.forward * (PlayerCrafting.MaxCraftDistance + 0.01f)),
                "A station beyond the reach is not one the player can use.");
        }
    }
}
