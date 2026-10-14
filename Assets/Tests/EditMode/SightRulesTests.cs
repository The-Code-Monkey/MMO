using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The line-of-sight decision, pinned in EditMode. Range, cone and the raycast verdict are all
    /// plain arguments, so a blocked wall or a target behind the caster is asserted without a
    /// physics scene.
    /// </summary>
    public sealed class SightRulesTests
    {
        [Test]
        public void CanSee_True_WhenAllThreeConditionsHold()
        {
            Assert.IsTrue(SightRules.CanSee(withinRange: true, withinCone: true, raycastClear: true));
        }

        [Test]
        public void CanSee_False_WhenRaycastBlocked()
        {
            Assert.IsFalse(SightRules.CanSee(withinRange: true, withinCone: true, raycastClear: false));
        }

        [Test]
        public void CanSee_False_WhenOutOfRange()
        {
            Assert.IsFalse(SightRules.CanSee(withinRange: false, withinCone: true, raycastClear: true));
        }

        [Test]
        public void CanSee_False_WhenOutOfCone()
        {
            Assert.IsFalse(SightRules.CanSee(withinRange: true, withinCone: false, raycastClear: true));
        }

        [Test]
        public void CanSee_False_WhenEveryConditionFails()
        {
            Assert.IsFalse(SightRules.CanSee(withinRange: false, withinCone: false, raycastClear: false));
        }

        [Test]
        public void WithinRange_StrictlyCloserIsVisible()
        {
            Assert.IsTrue(SightRules.WithinRange(Vector3.zero, new Vector3(4f, 0f, 0f), range: 5f));
        }

        [Test]
        public void WithinRange_TargetExactlyOnTheBoundaryIsOutOfReach()
        {
            Assert.IsFalse(SightRules.WithinRange(Vector3.zero, new Vector3(5f, 0f, 0f), range: 5f));
        }

        [Test]
        public void WithinRange_NonPositiveRange_SeesNothing()
        {
            Assert.IsFalse(SightRules.WithinRange(Vector3.zero, Vector3.zero, range: 0f));
        }

        [Test]
        public void WithinCone_TrueInFront()
        {
            Assert.IsTrue(SightRules.WithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 5f), 45f));
        }

        [Test]
        public void WithinCone_FalseBehindTheCaster()
        {
            Assert.IsFalse(SightRules.WithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -5f), 45f));
        }

        [Test]
        public void WithinCone_FalseOutsideAFortyFiveDegreeHalfAngle()
        {
            // 60° off forward is outside a ±45° cone but inside a ±90° one.
            Vector3 target = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward * 5f;
            Assert.IsFalse(SightRules.WithinCone(Vector3.zero, Vector3.forward, target, 45f));
            Assert.IsTrue(SightRules.WithinCone(Vector3.zero, Vector3.forward, target, 90f));
        }

        [Test]
        public void WithinCone_TargetAtTheEye_IsVisible()
        {
            Assert.IsTrue(SightRules.WithinCone(
                new Vector3(2f, 0f, 2f), Vector3.forward, new Vector3(2f, 0f, 2f), 45f),
                "A target sharing the caster's position has no direction to be outside of.");
        }

        [Test]
        public void WithinCone_ZeroForward_SeesNothing()
        {
            Assert.IsFalse(SightRules.WithinCone(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 5f), 90f));
        }

        [Test]
        public void WithinCone_HalfAngleOf180_SeesEverything()
        {
            Assert.IsTrue(SightRules.WithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -5f), 180f));
        }

        [Test]
        public void WithinCone_IgnoresDistance_MagnitudeDoesNotMatter()
        {
            Assert.IsTrue(SightRules.WithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 0.001f), 45f));
            Assert.IsTrue(SightRules.WithinCone(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 100000f), 45f));
        }
    }
}
