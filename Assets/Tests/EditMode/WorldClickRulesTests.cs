using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>A release is a world click only while the pointer stayed still enough.</summary>
    public sealed class WorldClickRulesTests
    {
        [Test]
        public void IsClick_AcceptsAPressThatBarelyMoved()
        {
            Assert.IsTrue(WorldClickRules.IsClick(0f));
            Assert.IsTrue(WorldClickRules.IsClick(WorldClickRules.DragThresholdPixels));
        }

        [Test]
        public void IsClick_RejectsADrag()
        {
            Assert.IsFalse(WorldClickRules.IsClick(WorldClickRules.DragThresholdPixels + 0.01f));
            Assert.IsFalse(WorldClickRules.IsClick(40f));
        }
    }
}
