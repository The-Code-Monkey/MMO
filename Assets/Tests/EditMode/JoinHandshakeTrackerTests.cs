using MMO.Networking;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class JoinHandshakeTrackerTests
    {
        [Test]
        public void AddPending_IncreasesCount()
        {
            var tracker = new JoinHandshakeTracker(10f);
            tracker.AddPending(1, 0f);
            Assert.AreEqual(1, tracker.PendingCount);
            Assert.IsTrue(tracker.IsPending(1));
        }

        [Test]
        public void TryRemove_RemovesEntry()
        {
            var tracker = new JoinHandshakeTracker(10f);
            tracker.AddPending(1, 0f);
            Assert.IsTrue(tracker.TryRemove(1));
            Assert.IsFalse(tracker.IsPending(1));
            Assert.AreEqual(0, tracker.PendingCount);
        }

        [Test]
        public void IsExpired_TrueAfterTimeout()
        {
            var tracker = new JoinHandshakeTracker(5f);
            tracker.AddPending(1, 2f);
            Assert.IsFalse(tracker.IsExpired(1, 6.9f));
            Assert.IsTrue(tracker.IsExpired(1, 7f));
        }

        [Test]
        public void CollectExpired_ReturnsOnlyExpired()
        {
            var tracker = new JoinHandshakeTracker(5f);
            tracker.AddPending(1, 0f);
            tracker.AddPending(2, 4f);
            var expired = tracker.CollectExpired(5f);
            Assert.AreEqual(1, expired.Count);
            Assert.Contains(1, (System.Collections.ICollection)expired);
        }

        [Test]
        public void IsExpired_UnknownKeyIsFalse()
        {
            var tracker = new JoinHandshakeTracker(5f);
            Assert.IsFalse(tracker.IsExpired(99, 100f));
        }
    }
}
