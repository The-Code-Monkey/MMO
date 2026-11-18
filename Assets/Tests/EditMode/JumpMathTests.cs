using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>The jump's arithmetic: the impulse that reaches the declared apex, and its inverse.</summary>
    public sealed class JumpMathTests
    {
        [Test]
        public void ImpulseSpeed_ReachesTheDeclaredApex()
        {
            float impulse = JumpMath.ImpulseSpeed(JumpMath.JumpHeight, JumpMath.Gravity);

            Assert.AreEqual(6.6332f, impulse, 0.001f);
            Assert.AreEqual(JumpMath.JumpHeight, JumpMath.ApexHeight(impulse, JumpMath.Gravity), 0.0001f);
        }

        [Test]
        public void ImpulseSpeed_IsZeroForAMissingOrNegativeHeight()
        {
            Assert.AreEqual(0f, JumpMath.ImpulseSpeed(0f, JumpMath.Gravity), 0.0001f);
            Assert.AreEqual(0f, JumpMath.ImpulseSpeed(-1f, JumpMath.Gravity), 0.0001f);
        }

        [Test]
        public void ApexHeight_SurvivesZeroGravity()
        {
            Assert.AreEqual(0f, JumpMath.ApexHeight(5f, 0f), 0.0001f,
                "A jump under no gravity has no apex to reach.");
        }
    }
}
