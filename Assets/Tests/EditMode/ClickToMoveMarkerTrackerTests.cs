using MMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The life of the click-to-move destination marker: it must appear when a destination is
    /// requested, survive the frames while the click is still travelling to the server, and
    /// disappear once the walk is over — whether the player reached the point or merely stopped.
    /// The last case is the one a distance check alone gets wrong, and the one that left the
    /// yellow box on screen forever.
    /// </summary>
    public sealed class ClickToMoveMarkerTrackerTests
    {
        private static readonly Vector3 Target = new Vector3(10f, 0f, 10f);

        [Test]
        public void Begin_TracksTheDestination()
        {
            var tracker = new ClickToMoveMarkerTracker();

            tracker.Begin(Target);

            Assert.IsTrue(tracker.IsTracking);
            Assert.AreEqual(Target, tracker.Target);
        }

        [Test]
        public void ShouldClear_IsFalseBeforeAnyMovement()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);

            // The click has not reached the server yet, so the player is standing still. The
            // marker must not be cleared before the walk even starts.
            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
            Assert.IsTrue(tracker.IsTracking);
        }

        [Test]
        public void ShouldClear_IsTrueWhenThePlayerArrives()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);

            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
            Assert.IsTrue(tracker.ShouldClear(Target));
            Assert.IsFalse(tracker.IsTracking);
        }

        [Test]
        public void ShouldClear_IsTrueWhenThePlayerArrivesJustOutsideTheRadius()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);
            float justInside = ClickToMoveMarkerTracker.ArrivalRadius - 0.01f;

            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
            Assert.IsTrue(tracker.ShouldClear(Target + new Vector3(justInside, 0f, 0f)));
        }

        [Test]
        public void ShouldClear_IsTrueWhenThePlayerStopsShortOfTheTarget()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);
            Vector3 stoppedShort = Target + new Vector3(3f, 0f, 0f);

            // The walk begins...
            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
            // ...the player moves...
            Assert.IsFalse(tracker.ShouldClear(new Vector3(1f, 0f, 0f)));
            // ...and then stands still short of the clicked point.
            Assert.IsTrue(tracker.ShouldClear(new Vector3(1f, 0f, 0f)));
            Assert.IsFalse(tracker.IsTracking);
        }

        [Test]
        public void ShouldClear_IsFalseWhileThePlayerKeepsMoving()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);

            for (int i = 1; i <= 10; i++)
            {
                Assert.IsFalse(tracker.ShouldClear(new Vector3(i, 0f, 0f)));
            }
        }

        [Test]
        public void ShouldClear_IsFalseAfterCancel()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);
            tracker.Cancel();

            Assert.IsFalse(tracker.ShouldClear(Target));
            Assert.IsFalse(tracker.IsTracking);
        }

        [Test]
        public void Begin_ReplacesAnEarlierDestination()
        {
            var tracker = new ClickToMoveMarkerTracker();
            tracker.Begin(Target);
            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));

            Vector3 second = new Vector3(-5f, 0f, 3f);
            tracker.Begin(second);

            Assert.IsTrue(tracker.IsTracking);
            Assert.AreEqual(second, tracker.Target);
            // The new walk has not moved yet, so the marker stays up.
            Assert.IsFalse(tracker.ShouldClear(Vector3.zero));
        }
    }
}
