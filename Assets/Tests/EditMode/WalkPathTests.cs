using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The corner-walker that turns a NavMesh route into the points the server actually walks
    /// to. It is pure state and arithmetic over positions, so the whole life of a route is
    /// judged here in microseconds: the route's first corner is the point the walker already
    /// stands on, corners already reached are stepped over, and the last corner is the
    /// destination.
    /// </summary>
    public sealed class WalkPathTests
    {
        [Test]
        public void SetCorners_SkipsTheStartCorner()
        {
            var path = new WalkPath();

            path.SetCorners(new[] { Vector3.zero, new Vector3(5f, 0f, 0f) });

            Assert.AreEqual(new Vector3(5f, 0f, 0f), path.NextWaypoint(Vector3.zero),
                "The first corner is where the route starts — the walker is already standing on it.");
        }

        [Test]
        public void NextWaypoint_StepsPastACornerAlreadyReached()
        {
            var path = new WalkPath();
            path.SetCorners(new[] { Vector3.zero, new Vector3(5f, 0f, 0f), new Vector3(10f, 0f, 0f) });

            Assert.AreEqual(new Vector3(5f, 0f, 0f), path.NextWaypoint(Vector3.zero));
            Assert.AreEqual(new Vector3(10f, 0f, 0f), path.NextWaypoint(new Vector3(5f, 0f, 0f)),
                "A corner the walker has reached must not send it back to the corner before.");
        }

        [Test]
        public void NextWaypoint_MeasuresDistanceOnTheGround()
        {
            var path = new WalkPath();
            path.SetCorners(new[] { Vector3.zero, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 10f) });

            // The player's transform sits at the capsule's middle, well above the mesh, so a
            // corner that is horizontally on top of the player counts as reached.
            Assert.AreEqual(new Vector3(0f, 0f, 10f), path.NextWaypoint(new Vector3(0f, 1f, 5f)),
                "Height must not hide a corner the walker has already reached.");
        }

        [Test]
        public void NextWaypoint_WithoutARoute_ReadsZero()
        {
            var path = new WalkPath();

            Assert.AreEqual(Vector3.zero, path.NextWaypoint(Vector3.one),
                "A walker with no route has nowhere to go; the caller falls back to a straight walk.");
        }

        [Test]
        public void Clear_ForgetsTheRoute()
        {
            var path = new WalkPath();
            path.SetCorners(new[] { Vector3.zero, new Vector3(5f, 0f, 0f) });

            path.Clear();

            Assert.IsTrue(path.IsEmpty, "A cleared route must leave the walker on the straight-line fallback.");
            Assert.AreEqual(Vector3.zero, path.NextWaypoint(Vector3.zero));
        }

        [Test]
        public void IsEmpty_ReflectsWhetherARouteWasSet()
        {
            var path = new WalkPath();

            Assert.IsTrue(path.IsEmpty, "A walker starts with no route.");

            path.SetCorners(new[] { Vector3.zero, new Vector3(5f, 0f, 0f) });
            Assert.IsFalse(path.IsEmpty, "A route was set, so the walker has corners to follow.");
        }
    }
}
