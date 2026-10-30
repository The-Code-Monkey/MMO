using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The prewarm rule is the whole promise of pooling: the size is decided before combat,
    /// not during it.
    /// </summary>
    public sealed class PoolBudgetTests
    {
        [Test]
        public void Prewarm_AddsHeadroomAndRoundsUp()
        {
            Assert.AreEqual(15, PoolBudget.Prewarm(expectedConcurrent: 10));
        }

        [Test]
        public void Prewarm_ClampsAtMaximum()
        {
            Assert.AreEqual(PoolBudget.DefaultMax, PoolBudget.Prewarm(expectedConcurrent: 1000));
        }

        [Test]
        public void Prewarm_ZeroExpected_StillBuildsOne()
        {
            Assert.AreEqual(1, PoolBudget.Prewarm(expectedConcurrent: 0));
        }

        [Test]
        public void Prewarm_NegativeExpected_StillBuildsOne()
        {
            Assert.AreEqual(1, PoolBudget.Prewarm(expectedConcurrent: -10));
        }

        [Test]
        public void Prewarm_HeadroomBelowOne_DoesNotShrinkBelowExpected()
        {
            Assert.AreEqual(10, PoolBudget.Prewarm(expectedConcurrent: 10, headroom: 0.5f));
        }

        [Test]
        public void Prewarm_CustomMaximum_IsRespected()
        {
            Assert.AreEqual(4, PoolBudget.Prewarm(expectedConcurrent: 100, max: 4));
        }
    }
}
