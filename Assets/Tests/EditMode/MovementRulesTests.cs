using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The movement validator, pinned in EditMode. Because the rule is pure arithmetic, the whole
    /// "can the server have observed this move?" decision is asserted without a CharacterController,
    /// a scene or a moving object.
    /// </summary>
    public sealed class MovementRulesTests
    {
        [Test]
        public void MaxDistance_PositiveInputs_ScalesWithSpeedAndTime()
        {
            // 6 units/s for 0.5s with the default 1.25 grace = 3.75, plus the snap tolerance.
            Assert.AreEqual(3.75f + MovementRules.SnapTolerance,
                MovementRules.MaxDistance(maxSpeed: 6f, deltaSeconds: 0.5f), 0.0001f);
        }

        [Test]
        public void MaxDistance_NonPositiveInputs_IsJustTheSnapTolerance()
        {
            Assert.AreEqual(MovementRules.SnapTolerance, MovementRules.MaxDistance(-5f, 1f), 0.0001f);
            Assert.AreEqual(MovementRules.SnapTolerance, MovementRules.MaxDistance(6f, -1f), 0.0001f);
            Assert.AreEqual(MovementRules.SnapTolerance, MovementRules.MaxDistance(0f, 0f), 0.0001f);
        }

        [Test]
        public void IsPlausible_RejectsFiftyUnitsInPointOneSeconds()
        {
            Assert.IsFalse(MovementRules.IsPlausible(
                Vector3.zero, new Vector3(50f, 0f, 0f), maxSpeed: 6f, deltaSeconds: 0.1f));
        }

        [Test]
        public void IsPlausible_AllowsNormalRun()
        {
            // 0.6 units in 0.1s at speed 6 is well inside the 0.76 budget.
            Assert.IsTrue(MovementRules.IsPlausible(
                Vector3.zero, new Vector3(0.6f, 0f, 0f), maxSpeed: 6f, deltaSeconds: 0.1f));
        }

        [Test]
        public void IsPlausible_CountsDiagonalDistance_NotPerAxis()
        {
            // A 3-4-5 triangle: 3 units each axis is a 5-unit move, which exceeds a 4-unit budget.
            Assert.IsFalse(MovementRules.IsPlausible(
                Vector3.zero, new Vector3(3f, 0f, 4f), maxSpeed: 6f, deltaSeconds: 0.5f));
        }

        [Test]
        public void IsPlausible_AllowsMoveExactlyOnTheBudgetBoundary()
        {
            float budget = MovementRules.MaxDistance(maxSpeed: 10f, deltaSeconds: 1f);
            Assert.IsTrue(MovementRules.IsPlausible(
                Vector3.zero, new Vector3(budget, 0f, 0f), maxSpeed: 10f, deltaSeconds: 1f),
                "The boundary itself is legal; only a move past it is a teleport.");
        }

        [Test]
        public void IsPlausible_RejectsAMoveJustPastTheBudget()
        {
            float budget = MovementRules.MaxDistance(maxSpeed: 10f, deltaSeconds: 1f);
            Assert.IsFalse(MovementRules.IsPlausible(
                Vector3.zero, new Vector3(budget + 1f, 0f, 0f), maxSpeed: 10f, deltaSeconds: 1f));
        }

        [Test]
        public void IsPlausible_MeasuresFromTheLastServerPosition_NotTheOrigin()
        {
            var lastServerPosition = new Vector3(100f, -5f, 20f);
            Assert.IsTrue(MovementRules.IsPlausible(
                lastServerPosition, lastServerPosition + new Vector3(0.6f, 0f, 0f), maxSpeed: 6f, deltaSeconds: 0.1f));
            Assert.IsFalse(MovementRules.IsPlausible(
                lastServerPosition, new Vector3(50f, -5f, 20f), maxSpeed: 6f, deltaSeconds: 0.1f));
        }

        [Test]
        public void IsPlausible_ZeroDelta_StillAllowsStandingStill()
        {
            Assert.IsTrue(MovementRules.IsPlausible(
                new Vector3(4f, 0f, 9f), new Vector3(4f, 0f, 9f), maxSpeed: 6f, deltaSeconds: 0f),
                "A non-moving report is always plausible within the snap tolerance.");
        }

        [Test]
        public void SnapBack_ReturnsLastServerPosition()
        {
            var last = new Vector3(1f, 2f, 3f);
            Assert.AreEqual(last, MovementRules.SnapBack(last));
        }
    }
}
