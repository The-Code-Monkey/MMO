using MMO.Gameplay;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The donkey's carrying rule: the bag keeps every item on dismount, and each item above the
    /// base capacity slows the player by 0.1, down to a floor.
    /// </summary>
    public sealed class MountRulesTests
    {
        private const int BaseCapacity = 48;

        [Test]
        public void WithinCapacity_NoPenalty()
        {
            Assert.AreEqual(1f, MountRules.UnmountedSpeedMultiplier(BaseCapacity, BaseCapacity), 0.0001f);
            Assert.AreEqual(0, MountRules.OverloadCount(40, BaseCapacity));
        }

        [Test]
        public void EachItemOverCapacity_CostsTenPercent()
        {
            Assert.AreEqual(0.9f, MountRules.UnmountedSpeedMultiplier(BaseCapacity + 1, BaseCapacity), 0.0001f);
            Assert.AreEqual(0.5f, MountRules.UnmountedSpeedMultiplier(BaseCapacity + 5, BaseCapacity), 0.0001f,
                "The five mount slots are exactly the free capacity; over them is a real penalty.");
        }

        [Test]
        public void HeavyOverload_FloorsAtTheMinimum()
        {
            Assert.AreEqual(MountRules.MinimumSpeedMultiplier,
                MountRules.UnmountedSpeedMultiplier(BaseCapacity + 50, BaseCapacity), 0.0001f);
        }

        [Test]
        public void OverloadCount_NeverGoesNegative()
        {
            Assert.AreEqual(0, MountRules.OverloadCount(0, BaseCapacity));
            Assert.AreEqual(0, MountRules.OverloadCount(BaseCapacity - 1, BaseCapacity));
            Assert.AreEqual(3, MountRules.OverloadCount(BaseCapacity + 3, BaseCapacity));
        }
    }
}
