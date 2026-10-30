using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class PlayerLocomotionMathTests
    {
        [Test]
        public void NormalizeSpeed_ClampsToUnitRange()
        {
            Assert.AreEqual(0f, PlayerLocomotionMath.NormalizeSpeed(-1f, 6f));
            Assert.AreEqual(0f, PlayerLocomotionMath.NormalizeSpeed(6f, 0f));
            Assert.AreEqual(0.5f, PlayerLocomotionMath.NormalizeSpeed(3f, 6f), 0.0001f);
            Assert.AreEqual(1f, PlayerLocomotionMath.NormalizeSpeed(12f, 6f));
        }

        [Test]
        public void Resolve_StandingStillIsIdle()
        {
            Assert.AreEqual(
                PlayerLocomotionState.Idle,
                PlayerLocomotionMath.Resolve(0f, 6f));
        }

        [Test]
        public void Resolve_SlowMovementIsWalk()
        {
            Assert.AreEqual(
                PlayerLocomotionState.Walk,
                PlayerLocomotionMath.Resolve(3f, 6f));
        }

        [Test]
        public void Resolve_BaseTopSpeedIsRun()
        {
            Assert.AreEqual(
                PlayerLocomotionState.Run,
                PlayerLocomotionMath.Resolve(6f, 6f));
        }
    }
}
