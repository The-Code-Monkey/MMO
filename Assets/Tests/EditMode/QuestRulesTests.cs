using System.Collections.Generic;
using MMO.Content;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The pure quest rules: availability from prerequisites, objective progress, and completion.
    /// No server, clock or scene is involved.
    /// </summary>
    public sealed class QuestRulesTests
    {
        [TearDown]
        public void TearDown() => TestContentData.Cleanup();

        [Test]
        public void IsAvailable_IsFalseForANullQuest()
        {
            Assert.IsFalse(QuestRules.IsAvailable(null, new HashSet<string>()));
        }

        [Test]
        public void IsAvailable_IsTrueWhenEveryPrerequisiteIsComplete()
        {
            QuestData quest = TestContentData.Quest("advance", prerequisiteQuestIds: new[] { "intro", "gather" });

            Assert.IsFalse(QuestRules.IsAvailable(quest, new HashSet<string> { "intro" }));
            Assert.IsTrue(QuestRules.IsAvailable(quest, new HashSet<string> { "intro", "gather" }));
        }

        [Test]
        public void IsAvailable_IsTrueForAQuestWithoutPrerequisites()
        {
            QuestData quest = TestContentData.Quest("intro");

            Assert.IsTrue(QuestRules.IsAvailable(quest, new HashSet<string>()));
        }

        [Test]
        public void Advance_AddsProgressAndCapsAtTheRequiredCount()
        {
            QuestObjective objective = TestContentData.Objective(QuestObjectiveKind.Kill, "Wolf", requiredCount: 3);

            Assert.AreEqual(2, QuestRules.Advance(1, objective, 1));
            Assert.AreEqual(3, QuestRules.Advance(2, objective, 5));
            Assert.AreEqual(3, QuestRules.Advance(3, objective, 1));
        }

        [Test]
        public void Advance_NeverMovesBackwards()
        {
            QuestObjective objective = TestContentData.Objective(QuestObjectiveKind.Collect, "Ore", requiredCount: 3);

            Assert.AreEqual(0, QuestRules.Advance(0, objective, -5));
            Assert.AreEqual(2, QuestRules.Advance(2, objective, -1));
        }

        [Test]
        public void IsObjectiveComplete_RequiresTheFullCount()
        {
            QuestObjective objective = TestContentData.Objective(QuestObjectiveKind.Interact, "Anvil", requiredCount: 2);

            Assert.IsFalse(QuestRules.IsObjectiveComplete(1, objective));
            Assert.IsTrue(QuestRules.IsObjectiveComplete(2, objective));
            Assert.IsTrue(QuestRules.IsObjectiveComplete(3, objective));
        }

        [Test]
        public void IsComplete_RequiresEveryObjective()
        {
            QuestData quest = TestContentData.Quest("mixed", new[]
            {
                TestContentData.Objective(QuestObjectiveKind.Kill, "Wolf", 2),
                TestContentData.Objective(QuestObjectiveKind.Collect, "Ore", 1),
            });

            Assert.IsFalse(QuestRules.IsComplete(quest, new[] { 2 }));
            Assert.IsFalse(QuestRules.IsComplete(quest, new[] { 1, 1 }));
            Assert.IsTrue(QuestRules.IsComplete(quest, new[] { 2, 1 }));
        }

        [Test]
        public void IsComplete_IsFalseForANullOrObjectiveLessQuest()
        {
            Assert.IsFalse(QuestRules.IsComplete(null, new[] { 1 }));
            Assert.IsFalse(QuestRules.IsComplete(TestContentData.Quest("empty"), new int[0]),
                "A quest with nothing to do is not silently complete.");
        }

        [Test]
        public void IsComplete_IsRepeatableWithoutChangingProgress()
        {
            QuestData quest = TestContentData.Quest("repeat", new[]
            {
                TestContentData.Objective(QuestObjectiveKind.Kill, "Wolf", 1),
            });
            int[] counts = { 1 };

            Assert.IsTrue(QuestRules.IsComplete(quest, counts));
            Assert.IsTrue(QuestRules.IsComplete(quest, counts));
            Assert.AreEqual(1, counts[0]);
        }
    }
}
