using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The heading's arithmetic: a fixed turn rate in both directions and an angle that stays in one
    /// turn's worth of degrees.
    /// </summary>
    public sealed class HeadingMathTests
    {
        [Test]
        public void NextYaw_TurnsRightAtTheFixedRate()
        {
            Assert.AreEqual(HeadingMath.DegreesPerSecond, HeadingMath.NextYaw(0f, 1f, 1f), 0.0001f);
            Assert.AreEqual(120f, HeadingMath.DegreesPerSecond, 0.0001f);
        }

        [Test]
        public void NextYaw_TurnsLeftAtTheFixedRate()
        {
            Assert.AreEqual(250f, HeadingMath.NextYaw(10f, -1f, 1f), 0.0001f);
        }

        [Test]
        public void NextYaw_ScalesWithDelta()
        {
            Assert.AreEqual(60f, HeadingMath.NextYaw(0f, 1f, 0.5f), 0.0001f);
        }

        [Test]
        public void NextYaw_DoesNotTurnWithoutInputOrTime()
        {
            Assert.AreEqual(42f, HeadingMath.NextYaw(42f, 0f, 1f), 0.0001f);
            Assert.AreEqual(42f, HeadingMath.NextYaw(42f, 1f, 0f), 0.0001f);
        }

        [Test]
        public void NextYaw_ClampsTheTurnAxis()
        {
            Assert.AreEqual(120f, HeadingMath.NextYaw(0f, 7f, 1f), 0.0001f,
                "A doctored client must not turn faster than one axis allows.");
        }

        [Test]
        public void Normalize_KeepsTheAngleInOneTurn()
        {
            Assert.AreEqual(350f, HeadingMath.Normalize(-10f), 0.0001f);
            Assert.AreEqual(10f, HeadingMath.Normalize(370f), 0.0001f);
        }
    }
}
