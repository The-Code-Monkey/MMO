using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The walking intent's arithmetic: the backward/strafe penalties, the diagonal clamp, and the
    /// rotation from local axes into the world for a camera yaw.
    /// </summary>
    public sealed class MoveIntentMathTests
    {
        [Test]
        public void ApplyMultipliers_LeavesForwardAtFullSpeed()
        {
            Assert.AreEqual(new Vector2(0f, 1f), MoveIntentMath.ApplyMultipliers(new Vector2(0f, 1f)));
        }

        [Test]
        public void ApplyMultipliers_SlowsBackward()
        {
            Vector2 result = MoveIntentMath.ApplyMultipliers(new Vector2(0f, -1f));

            Assert.AreEqual(-MoveIntentMath.BackwardMultiplier, result.y, 0.0001f);
            Assert.AreEqual(0.55f, MoveIntentMath.BackwardMultiplier, 0.0001f);
        }

        [Test]
        public void ApplyMultipliers_SlowsStrafe()
        {
            Vector2 result = MoveIntentMath.ApplyMultipliers(new Vector2(1f, 0f));

            Assert.AreEqual(MoveIntentMath.StrafeMultiplier, result.x, 0.0001f);
            Assert.AreEqual(0.8f, MoveIntentMath.StrafeMultiplier, 0.0001f);
        }

        [Test]
        public void ApplyMultipliers_NeverExceedsOneUnitForADiagonal()
        {
            Vector2 result = MoveIntentMath.ApplyMultipliers(new Vector2(1f, 1f));

            Assert.LessOrEqual(result.magnitude, 1.0001f,
                "A diagonal must not outrun a straight line.");
        }

        [Test]
        public void ApplyMultipliers_CancelsOpposingKeys()
        {
            Assert.AreEqual(Vector2.zero, MoveIntentMath.ApplyMultipliers(new Vector2(1f, 0f) + new Vector2(-1f, 0f)));
        }

        [Test]
        public void ToWorld_AtZeroYaw_KeepsLocalAxes()
        {
            Assert.AreEqual(new Vector2(0f, 1f), MoveIntentMath.ToWorld(new Vector2(0f, 1f), 0f));
            Assert.AreEqual(new Vector2(1f, 0f), MoveIntentMath.ToWorld(new Vector2(1f, 0f), 0f));
        }

        [Test]
        public void ToWorld_AtRightAngleYaw_TurnsForwardIntoPositiveX()
        {
            Vector2 forward = MoveIntentMath.ToWorld(new Vector2(0f, 1f), 90f);
            Vector2 right = MoveIntentMath.ToWorld(new Vector2(1f, 0f), 90f);

            Assert.AreEqual(1f, forward.x, 0.0001f);
            Assert.AreEqual(0f, forward.y, 0.0001f);
            Assert.AreEqual(0f, right.x, 0.0001f);
            Assert.AreEqual(-1f, right.y, 0.0001f);
        }
    }
}
