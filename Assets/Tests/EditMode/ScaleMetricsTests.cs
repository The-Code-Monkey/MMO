using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ScaleMetrics"/>. Pure arithmetic, no scene, no engine types
    /// beyond Vector3 for OnGrid — so these run in milliseconds and gate every commit.
    /// </summary>
    public sealed class ScaleMetricsTests
    {
        [Test]
        public void Doorway_AdmitsAStandingCharacter()
        {
            Assert.IsTrue(ScaleMetrics.AdmitsCharacter(ScaleMetrics.DoorWidth, ScaleMetrics.DoorHeight),
                "Default doorway must admit a standing character with clearance.");
        }

        [Test]
        public void Doorway_TooLow_RejectsCharacter()
        {
            // Height 1.9m is below CharacterHeight (1.8m) + CharacterClearance (0.2m) = 2.0m
            Assert.IsFalse(ScaleMetrics.AdmitsCharacter(1.5f, 1.9f),
                "Doorway below 2.0m must reject a standing character.");
        }

        [Test]
        public void Doorway_TooNarrow_RejectsCharacter()
        {
            // Width 0.9m is below CharacterDiameter (1.0m)
            Assert.IsFalse(ScaleMetrics.AdmitsCharacter(0.9f, 2.5f),
                "Doorway narrower than character diameter must reject.");
        }

        [Test]
        public void Doorway_ExactlyAtBoundary_AdmitsCharacter()
        {
            // Exactly at minimum: width = 1.0m, height = 2.0m
            Assert.IsTrue(ScaleMetrics.AdmitsCharacter(1.0f, 2.0f),
                "Doorway exactly at the boundary must admit (inclusive check).");
        }

        [Test]
        public void KitDimensions_FallOnTheGrid()
        {
            Assert.IsTrue(ScaleMetrics.OnGrid(1f, ScaleMetrics.GridSmall), "1m is on 1m grid");
            Assert.IsTrue(ScaleMetrics.OnGrid(2f, ScaleMetrics.GridSmall), "2m is on 1m grid");
            Assert.IsTrue(ScaleMetrics.OnGrid(4f, ScaleMetrics.GridLarge), "4m is on 2m grid");
            Assert.IsTrue(ScaleMetrics.OnGrid(6f, ScaleMetrics.GridLarge), "6m is on 2m grid");

            Assert.IsFalse(ScaleMetrics.OnGrid(1.5f, ScaleMetrics.GridLarge),
                "1.5m is not a whole multiple of 2m grid");
            Assert.IsFalse(ScaleMetrics.OnGrid(2.5f, ScaleMetrics.GridSmall),
                "2.5m is not a whole multiple of 1m grid");
        }

        [Test]
        public void OnGrid_ZeroGrid_ReturnsFalse()
        {
            Assert.IsFalse(ScaleMetrics.OnGrid(1f, 0f), "Zero grid must return false (guard).");
        }

        [Test]
        public void OnGrid_NegativeGrid_ReturnsFalse()
        {
            Assert.IsFalse(ScaleMetrics.OnGrid(1f, -1f), "Negative grid must return false (guard).");
        }

        [Test]
        public void StepHeight_IsBelowCharacterHeight()
        {
            // Sanity: a character can climb a step (step height < character height)
            Assert.Less(ScaleMetrics.MaxStepHeight, ScaleMetrics.CharacterHeight,
                "Max step height must be less than character height so climbing is possible.");
        }

        [Test]
        public void CharacterRadius_DefinesDiameter()
        {
            float diameter = ScaleMetrics.CharacterRadius * 2f;
            Assert.AreEqual(1.0f, diameter, 0.0001f,
                "Character diameter must be 1.0m (2 × 0.5m radius).");
        }

        [Test]
        public void DoorwayDefaults_ClearCharacterWithHeadroom()
        {
            float minHeight = ScaleMetrics.CharacterHeight + ScaleMetrics.CharacterClearance;
            Assert.LessOrEqual(minHeight, ScaleMetrics.DoorHeight,
                "Default door height must be at least character height + clearance.");
            Assert.LessOrEqual(ScaleMetrics.CharacterRadius * 2f, ScaleMetrics.DoorWidth,
                "Default door width must be at least character diameter.");
        }

        [Test]
        public void Unit_IsOneMetre()
        {
            Assert.AreEqual(1f, ScaleMetrics.Unit,
                "Unit constant must be 1 (Unity unit = 1 metre).");
        }

        [Test]
        public void GridConstants_AreMultiplesOfUnit()
        {
            Assert.AreEqual(ScaleMetrics.Unit, ScaleMetrics.GridSmall);
            Assert.AreEqual(ScaleMetrics.Unit * 2f, ScaleMetrics.GridLarge);
        }

        [Test]
        public void ScaleMetrics_IsStaticClass()
        {
            Assert.IsTrue(typeof(ScaleMetrics).IsAbstract && typeof(ScaleMetrics).IsSealed,
                "ScaleMetrics must be a static class (abstract + sealed in C#).");
        }
    }
}