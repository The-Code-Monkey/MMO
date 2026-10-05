using System.Collections.Generic;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The deadline bookkeeping the join gate parks connections in. Pure C# so the
    /// timing rules can be pinned exactly instead of waited on.
    /// </summary>
    public sealed class JoinHandshakeTrackerTests
    {
        [Test]
        public void NewTracker_HasNoPendingConnections()
        {
            Assert.AreEqual(0, new JoinHandshakeTracker().Count);
        }

        [Test]
        public void Begin_IncreasesCount()
        {
            var tracker = new JoinHandshakeTracker();

            tracker.Begin(1, 100d);

            Assert.AreEqual(1, tracker.Count);
            Assert.IsTrue(tracker.IsPending(1));
        }

        [Test]
        public void Begin_TwiceForSameId_KeepsOneEntry()
        {
            var tracker = new JoinHandshakeTracker();

            tracker.Begin(1, 100d);
            tracker.Begin(1, 200d);

            Assert.AreEqual(1, tracker.Count);
            Assert.AreEqual(100f, tracker.Elapsed(1, 300d), 0.0001f, "The later Begin must win.");
        }

        [Test]
        public void Begin_TracksMultipleConnections()
        {
            var tracker = new JoinHandshakeTracker();

            tracker.Begin(1, 0d);
            tracker.Begin(2, 0d);

            Assert.AreEqual(2, tracker.Count);
        }

        [Test]
        public void End_RemovesEntry()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 0d);

            Assert.IsTrue(tracker.End(1));
            Assert.IsFalse(tracker.IsPending(1));
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void End_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(new JoinHandshakeTracker().End(42));
        }

        [Test]
        public void IsPending_UnknownId_IsFalse()
        {
            Assert.IsFalse(new JoinHandshakeTracker().IsPending(42));
        }

        [Test]
        public void Elapsed_ReportsSecondsSinceBegin()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 100d);

            Assert.AreEqual(2.5f, tracker.Elapsed(1, 102.5d), 0.0001f);
        }

        [Test]
        public void Elapsed_UnknownId_IsZero()
        {
            Assert.AreEqual(0f, new JoinHandshakeTracker().Elapsed(42, 100d));
        }

        [Test]
        public void Expired_ReturnsOnlyConnectionsPastTheTimeout()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 0d);
            tracker.Begin(2, 4d);

            List<int> expired = tracker.Expired(5d, 1d);

            Assert.AreEqual(1, expired.Count);
            Assert.Contains(1, expired);
        }

        [Test]
        public void Expired_ExcludesConnectionExactlyAtTimeout()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 0d);

            Assert.IsEmpty(tracker.Expired(5d, 5d));
        }

        [Test]
        public void Expired_NoPendingConnections_ReturnsEmptyList()
        {
            List<int> expired = new JoinHandshakeTracker().Expired(100d, 5d);

            Assert.IsNotNull(expired);
            Assert.IsEmpty(expired);
        }

        [Test]
        public void Expired_ReturnsEveryExpiredConnection()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 0d);
            tracker.Begin(2, 0d);
            tracker.Begin(3, 199d);

            List<int> expired = tracker.Expired(200d, 5d);

            Assert.AreEqual(2, expired.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, expired);
        }

        [Test]
        public void Expired_DoesNotRemoveEntries()
        {
            var tracker = new JoinHandshakeTracker();
            tracker.Begin(1, 0d);

            tracker.Expired(100d, 5d);

            Assert.AreEqual(1, tracker.Count, "Expiry is a query; the gate decides when to drop.");
        }
    }
}