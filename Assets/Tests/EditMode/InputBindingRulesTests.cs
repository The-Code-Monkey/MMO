using System.Collections.Generic;
using MMO.Client;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Rebinding has two promises: no control is bound twice, and system keys cannot be stolen.
    /// Both are decided by the pure rule before the Input System is touched.
    /// </summary>
    public sealed class InputBindingRulesTests
    {
        [Test]
        public void Resolve_ClearsTheOtherActionUsingTheSameControl()
        {
            var current = new Dictionary<string, string>
            {
                ["Ability1"] = "<Keyboard>/q",
                ["Ability2"] = "<Keyboard>/2",
            };

            InputBindingPlan plan = InputBindingRules.Resolve("Ability2", "<Keyboard>/q", current);

            Assert.IsFalse(plan.IsReserved);
            CollectionAssert.AreEqual(new[] { "Ability1" }, plan.ClearedActions);
        }

        [Test]
        public void Resolve_DoesNotClearTheActionBeingRebound()
        {
            var current = new Dictionary<string, string>
            {
                ["Ability1"] = "<Keyboard>/q",
            };

            InputBindingPlan plan = InputBindingRules.Resolve("Ability1", "<Keyboard>/q", current);

            Assert.IsEmpty(plan.ClearedActions);
        }

        [Test]
        public void Resolve_IsCaseInsensitiveOnControlPaths()
        {
            var current = new Dictionary<string, string>
            {
                ["Ability1"] = "<KEYBOARD>/Q",
            };

            InputBindingPlan plan = InputBindingRules.Resolve("Ability2", "<keyboard>/q", current);

            CollectionAssert.AreEqual(new[] { "Ability1" }, plan.ClearedActions);
        }

        [Test]
        public void Resolve_RefusesReservedControl()
        {
            InputBindingPlan plan = InputBindingRules.Resolve(
                "Ability1", "<Keyboard>/escape", new Dictionary<string, string>());

            Assert.IsTrue(plan.IsReserved);
            Assert.IsEmpty(plan.ClearedActions);
        }

        [Test]
        public void Resolve_RefusesEmptyAction()
        {
            InputBindingPlan plan = InputBindingRules.Resolve(string.Empty, "<Keyboard>/q", null);

            Assert.IsTrue(plan.IsReserved);
        }

        [Test]
        public void Resolve_NoCurrentBindings_ClearsNothing()
        {
            InputBindingPlan plan = InputBindingRules.Resolve("Ability1", "<Keyboard>/q", null);

            Assert.IsFalse(plan.IsReserved);
            Assert.IsEmpty(plan.ClearedActions);
        }

        [Test]
        public void IsReserved_RecognisesSystemKeysOnly()
        {
            Assert.IsTrue(InputBindingRules.IsReserved("<Keyboard>/escape"));
            Assert.IsTrue(InputBindingRules.IsReserved("<Keyboard>/f1"));
            Assert.IsFalse(InputBindingRules.IsReserved("<Keyboard>/q"));
            Assert.IsTrue(InputBindingRules.IsReserved(null));
        }
    }
}
