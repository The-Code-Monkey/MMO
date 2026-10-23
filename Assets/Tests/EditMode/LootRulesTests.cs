using System;
using System.Collections.Generic;
using MMO.Content;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The weighted loot roll. Entries are independent chances, which is what makes the ticket's
    /// "50% Leather, 1% Epic Mount" example expressible; the RNG is injected and sequenced so the
    /// test never depends on the runtime's random implementation.
    /// </summary>
    public sealed class LootRulesTests
    {
        [TearDown]
        public void TearDown()
        {
            TestContentData.Cleanup();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void Roll_NullTable_ReturnsEmpty()
        {
            Assert.IsEmpty(LootRules.Roll(null, new SequenceRandom()));
        }

        [Test]
        public void Roll_NullRandom_Throws()
        {
            WeightedLootTable table = TestContentData.LootTable("empty");

            Assert.Throws<ArgumentNullException>(() => LootRules.Roll(table, null));
        }

        [Test]
        public void Roll_EmptyOrZeroChanceTable_ReturnsEmpty()
        {
            ItemDefinition leather = TestItemDefinitions.Create("Leather");

            Assert.IsEmpty(LootRules.Roll(TestContentData.LootTable("empty"), new SequenceRandom()));
            Assert.IsEmpty(LootRules.Roll(
                TestContentData.LootTable("zero", TestContentData.Entry(leather, 0f)),
                new SequenceRandom(0.0)));
        }

        [Test]
        public void Roll_FiftyPercentLeather_OnePercentMount_IsDeterministic()
        {
            ItemDefinition leather = TestItemDefinitions.Create("Leather");
            ItemDefinition mount = TestItemDefinitions.Create("Epic Mount");
            WeightedLootTable table = TestContentData.LootTable("rare",
                TestContentData.Entry(leather, 0.50f),
                TestContentData.Entry(mount, 0.01f));

            List<LootDrop> leatherOnly = LootRules.Roll(table, new SequenceRandom(0.10, 0.50));
            Assert.AreEqual(1, leatherOnly.Count);
            Assert.AreSame(leather, leatherOnly[0].Item);

            List<LootDrop> both = LootRules.Roll(table, new SequenceRandom(0.10, 0.00));
            Assert.AreEqual(2, both.Count);
            Assert.AreSame(leather, both[0].Item);
            Assert.AreSame(mount, both[1].Item);
        }

        [Test]
        public void Roll_CountsWithinTheAuthoredRange()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Ore");
            WeightedLootTable table = TestContentData.LootTable("ore",
                TestContentData.Entry(ore, 1f, minCount: 2, maxCount: 4));

            Assert.AreEqual(2, LootRules.Roll(table, new SequenceRandom(0.0, 0.0))[0].Count);
            Assert.AreEqual(4, LootRules.Roll(table, new SequenceRandom(0.0, 0.999))[0].Count);
        }

        [Test]
        public void Roll_SkipsAnEntryWithNoItem()
        {
            WeightedLootTable table = TestContentData.LootTable("broken",
                TestContentData.Entry(null, 1f));

            Assert.IsEmpty(LootRules.Roll(table, new SequenceRandom(0.0)));
        }

        [Test]
        public void RollCount_HandlesAReversedRange()
        {
            Assert.AreEqual(5, LootRules.RollCount(5, 2, new SequenceRandom()));
        }

        [Test]
        public void RollCount_DoesNotConsumeRandomnessForAFixedCount()
        {
            var rng = new SequenceRandom(0.99);

            Assert.AreEqual(3, LootRules.RollCount(3, 3, rng));
            Assert.AreEqual(2, LootRules.RollCount(2, 2, rng));
            Assert.AreEqual(4, LootRules.RollCount(2, 4, rng),
                "The first variable range must still receive the queued 0.99 sample.");
        }

        private sealed class SequenceRandom : Random
        {
            private readonly Queue<double> _values;

            public SequenceRandom(params double[] values)
            {
                _values = new Queue<double>(values);
            }

            protected override double Sample()
            {
                return _values.Count > 0 ? _values.Dequeue() : 0d;
            }
        }
    }
}
