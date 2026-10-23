using MMO.Content;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The data surface and registries: authored fields come back through read-only properties,
    /// the clamped fields clamp, and quests/loot tables resolve by their registry key.
    /// </summary>
    public sealed class ContentDataTests
    {
        [TearDown]
        public void TearDown()
        {
            TestContentData.Cleanup();
            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void QuestData_ExposesAuthoredValues()
        {
            WeightedLootTable reward = TestContentData.LootTable("reward");
            QuestData quest = TestContentData.Quest(
                "intro",
                new[]
                {
                    TestContentData.Objective(QuestObjectiveKind.Kill, "Wolf", 3),
                    TestContentData.Objective(QuestObjectiveKind.Collect, "Ore", 2),
                },
                prerequisiteQuestIds: new[] { "prologue" },
                rewardXp: 25,
                rewardTable: reward);

            Assert.AreEqual("intro", quest.QuestId);
            Assert.AreEqual("intro", quest.Title);
            Assert.AreEqual(2, quest.Objectives.Count);
            Assert.AreEqual(QuestObjectiveKind.Kill, quest.Objectives[0].Kind);
            Assert.AreEqual("Wolf", quest.Objectives[0].TargetName);
            Assert.AreEqual(3, quest.Objectives[0].RequiredCount);
            CollectionAssert.AreEqual(new[] { "prologue" }, quest.PrerequisiteQuestIds);
            Assert.AreEqual(25, quest.RewardXp);
            Assert.AreSame(reward, quest.RewardTable);
        }

        [Test]
        public void QuestRegistry_RegistersResolvesAndClears()
        {
            QuestData quest = TestContentData.Quest("intro", register: false);

            Assert.IsNull(QuestRegistry.Resolve("intro"));

            QuestRegistry.Register(quest);
            Assert.AreSame(quest, QuestRegistry.Resolve("intro"));
            CollectionAssert.Contains(QuestRegistry.All, quest);

            QuestRegistry.Clear();
            Assert.IsNull(QuestRegistry.Resolve("intro"));
        }

        [Test]
        public void LootTableData_ExposesEntriesAndRegisters()
        {
            ItemDefinition ore = TestItemDefinitions.Create("Ore");
            WeightedLootTable table = TestContentData.LootTable("ore",
                TestContentData.Entry(ore, 0.25f, 1, 3));

            Assert.AreEqual("ore", table.TableId);
            Assert.AreEqual(1, table.Entries.Count);
            Assert.AreSame(ore, table.Entries[0].Item);
            Assert.AreEqual(0.25f, table.Entries[0].Chance, 0.0001f);
            Assert.AreEqual(1, table.Entries[0].MinCount);
            Assert.AreEqual(3, table.Entries[0].MaxCount);
            Assert.AreSame(table, LootTableRegistry.Resolve("ore"));
        }

        [Test]
        public void LootTableData_WithoutEntriesExposesAnEmptyList()
        {
            WeightedLootTable table = TestContentData.LootTable("empty");

            Assert.IsEmpty(table.Entries);
        }

        [Test]
        public void AuraData_ClampsNegativeDurationsAndStackCounts()
        {
            AuraData aura = TestContentData.Aura(
                "broken",
                AuraKind.Buff,
                durationSeconds: -1f,
                tickIntervalSeconds: -2f,
                maxStacks: 0);

            Assert.AreEqual(0f, aura.DurationSeconds);
            Assert.AreEqual(0f, aura.TickIntervalSeconds);
            Assert.AreEqual(1, aura.MaxStacks);
        }

        [Test]
        public void DialogueNode_ExposesItsChoices()
        {
            QuestData quest = TestContentData.Quest("intro");
            DialogueNode next = TestContentData.Dialogue("next");
            DialogueNode node = TestContentData.Dialogue(
                "start",
                TestContentData.Choice("Accept", next, DialogueAction.OfferQuest, quest));

            Assert.AreEqual("start", node.NodeId);
            Assert.AreEqual("Speaker", node.Speaker);
            Assert.AreEqual("Text", node.Text);
            Assert.AreEqual(1, node.Choices.Count);
            Assert.AreEqual("Accept", node.Choices[0].Text);
            Assert.AreSame(next, node.Choices[0].Next);
            Assert.AreEqual(DialogueAction.OfferQuest, node.Choices[0].Action);
            Assert.AreSame(quest, node.Choices[0].Quest);
        }
    }
}
