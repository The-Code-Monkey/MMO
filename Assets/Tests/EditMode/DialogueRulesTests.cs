using MMO.Content;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The pure dialogue graph rules: choice indexing, next-node lookup and the quest action a
    /// choice carries. The server shell validates a client's index through these.
    /// </summary>
    public sealed class DialogueRulesTests
    {
        [TearDown]
        public void TearDown() => TestContentData.Cleanup();

        [Test]
        public void ChoiceCount_IsZeroForANullNode()
        {
            Assert.AreEqual(0, DialogueRules.ChoiceCount(null));
        }

        [Test]
        public void CanChoose_RejectsIndexesOutsideTheNode()
        {
            DialogueNode node = TestContentData.Dialogue("start",
                TestContentData.Choice("Continue"),
                TestContentData.Choice("Leave"));

            Assert.IsFalse(DialogueRules.CanChoose(null, 0));
            Assert.IsFalse(DialogueRules.CanChoose(node, -1));
            Assert.IsTrue(DialogueRules.CanChoose(node, 0));
            Assert.IsTrue(DialogueRules.CanChoose(node, 1));
            Assert.IsFalse(DialogueRules.CanChoose(node, 2));
        }

        [Test]
        public void Next_ReturnsTheChoiceTarget()
        {
            DialogueNode next = TestContentData.Dialogue("next");
            DialogueNode node = TestContentData.Dialogue("start", TestContentData.Choice("Continue", next));

            Assert.AreSame(next, DialogueRules.Next(node, 0));
        }

        [Test]
        public void Next_ReturnsNullForAnInvalidChoice()
        {
            DialogueNode node = TestContentData.Dialogue("start", TestContentData.Choice("Continue"));

            Assert.IsNull(DialogueRules.Next(node, 4));
            Assert.IsNull(DialogueRules.Next(null, 0));
        }

        [Test]
        public void TryChoose_ReturnsTheChoiceActionAndQuest()
        {
            QuestData quest = TestContentData.Quest("intro");
            DialogueNode node = TestContentData.Dialogue(
                "start",
                TestContentData.Choice("Accept", action: DialogueAction.OfferQuest, quest: quest));

            Assert.IsTrue(DialogueRules.TryChoose(node, 0, out DialogueChoice choice));
            Assert.AreEqual("Accept", choice.Text);
            Assert.AreEqual(DialogueAction.OfferQuest, choice.Action);
            Assert.AreSame(quest, choice.Quest);
        }

        [Test]
        public void TryChoose_RejectsAnInvalidIndex()
        {
            DialogueNode node = TestContentData.Dialogue("start", TestContentData.Choice("Continue"));

            Assert.IsFalse(DialogueRules.TryChoose(node, 3, out _));
            Assert.IsFalse(DialogueRules.TryChoose(null, 0, out _));
        }
    }
}
