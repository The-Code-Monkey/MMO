using System;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Every rule the join gate enforces, plus the precedence between them. Rule order
    /// lives in <see cref="JoinPolicy"/>: each test here pins one rule and one ordering.
    /// </summary>
    public sealed class JoinPolicyTests
    {
        private static JoinDecision Evaluate(
            string name = "Bob",
            int connected = 0,
            int max = 10,
            float elapsed = 0f,
            bool maintenance = false,
            string[] allow = null,
            string[] block = null,
            float timeout = 5f)
            => JoinPolicy.Evaluate(new JoinRequest(name, connected, max, elapsed), maintenance, allow, block, timeout);

        [Test]
        public void Evaluate_Approves_NormalJoin()
        {
            Assert.IsTrue(Evaluate().Approved);
        }

        [Test]
        public void Approve_HasEmptyReason()
        {
            Assert.AreEqual(string.Empty, JoinDecision.Approve().Reason);
        }

        [Test]
        public void Reject_NullReason_BecomesEmpty()
        {
            Assert.AreEqual(string.Empty, JoinDecision.Reject(null).Reason);
        }

        [Test]
        public void Reject_KeepsReason()
        {
            Assert.AreEqual("nope", JoinDecision.Reject("nope").Reason);
        }

        [Test]
        public void JoinRequest_ExposesEveryField()
        {
            var request = new JoinRequest("Bob", 3, 10, 1.5f);

            Assert.AreEqual("Bob", request.PlayerName);
            Assert.AreEqual(3, request.ConnectedClients);
            Assert.AreEqual(10, request.MaxClients);
            Assert.AreEqual(1.5f, request.SecondsSinceConnected);
        }

        [Test]
        public void JoinRequest_SecondsSinceConnectedDefaultsToZero()
        {
            Assert.AreEqual(0f, new JoinRequest("Bob", 0, 10).SecondsSinceConnected);
        }

        [Test]
        public void Evaluate_Rejects_WhenMaintenance()
        {
            JoinDecision decision = Evaluate(maintenance: true);

            Assert.IsFalse(decision.Approved);
            Assert.AreEqual(JoinPolicy.ReasonMaintenance, decision.Reason);
        }

        [Test]
        public void Evaluate_Rejects_WhenCapacityMisconfigured()
        {
            Assert.AreEqual(JoinPolicy.ReasonInvalidCapacity, Evaluate(max: 0).Reason);
        }

        [Test]
        public void Evaluate_Rejects_WhenCapacityNegative()
        {
            Assert.AreEqual(JoinPolicy.ReasonInvalidCapacity, Evaluate(max: -1).Reason);
        }

        [Test]
        public void Evaluate_Rejects_WhenHandshakeTimedOut()
        {
            Assert.AreEqual(JoinPolicy.ReasonTimedOut, Evaluate(elapsed: 5.01f).Reason);
        }

        [Test]
        public void Evaluate_Allows_HandshakeExactlyAtTimeout()
        {
            Assert.IsTrue(Evaluate(elapsed: 5f).Approved);
        }

        [Test]
        public void Evaluate_Rejects_BlockedPlayer()
        {
            Assert.AreEqual(JoinPolicy.ReasonBlocked, Evaluate(block: new[] { "Bob" }).Reason);
        }

        [Test]
        public void BlockList_IsCaseInsensitive()
        {
            Assert.AreEqual(JoinPolicy.ReasonBlocked, Evaluate(block: new[] { "BOB" }).Reason);
        }

        [Test]
        public void Evaluate_Rejects_EmptyName()
        {
            Assert.AreEqual(JoinPolicy.ReasonMissingName, Evaluate(name: "   ").Reason);
        }

        [Test]
        public void Evaluate_Rejects_NullName()
        {
            Assert.AreEqual(JoinPolicy.ReasonMissingName, Evaluate(name: null).Reason);
        }

        [Test]
        public void Evaluate_Rejects_WhenNotOnAllowList()
        {
            Assert.AreEqual(JoinPolicy.ReasonNotAllowed, Evaluate(allow: new[] { "Alice" }).Reason);
        }

        [Test]
        public void Evaluate_Approves_WhenOnAllowList()
        {
            Assert.IsTrue(Evaluate(allow: new[] { "bob" }).Approved);
        }

        [Test]
        public void EmptyAllowList_AllowsEveryone()
        {
            Assert.IsTrue(Evaluate(allow: Array.Empty<string>()).Approved);
        }

        [Test]
        public void NullAllowList_AllowsEveryone()
        {
            Assert.IsTrue(Evaluate().Approved);
        }

        [Test]
        public void Evaluate_Rejects_WhenServerFull()
        {
            Assert.AreEqual(JoinPolicy.ReasonFull, Evaluate(connected: 10, max: 10).Reason);
        }

        [Test]
        public void Evaluate_Approves_WhenOneSlotLeft()
        {
            Assert.IsTrue(Evaluate(connected: 9, max: 10).Approved);
        }

        [Test]
        public void Evaluate_Rejects_WhenOverCapacity()
        {
            Assert.AreEqual(JoinPolicy.ReasonFull, Evaluate(connected: 11, max: 10).Reason);
        }

        [Test]
        public void Maintenance_TakesPrecedence_OverEverything()
        {
            JoinDecision decision = Evaluate(
                maintenance: true, connected: 99, max: 1, elapsed: 99f, block: new[] { "Bob" });

            Assert.AreEqual(JoinPolicy.ReasonMaintenance, decision.Reason);
        }

        [Test]
        public void InvalidCapacity_TakesPrecedence_OverBlocked()
        {
            Assert.AreEqual(JoinPolicy.ReasonInvalidCapacity, Evaluate(max: 0, block: new[] { "Bob" }).Reason);
        }

        [Test]
        public void Timeout_TakesPrecedence_OverBlocked()
        {
            Assert.AreEqual(JoinPolicy.ReasonTimedOut, Evaluate(elapsed: 99f, block: new[] { "Bob" }).Reason);
        }

        [Test]
        public void Blocked_TakesPrecedence_OverMissingName()
        {
            // A whitespace name can still be block listed; a null one cannot, since
            // Contains treats a null needle as "no match".
            Assert.AreEqual(JoinPolicy.ReasonBlocked, Evaluate(name: "  ", block: new[] { "  " }).Reason);
        }

        [Test]
        public void MissingName_TakesPrecedence_OverNotAllowed()
        {
            Assert.AreEqual(JoinPolicy.ReasonMissingName, Evaluate(name: null, allow: new[] { "Alice" }).Reason);
        }

        [Test]
        public void NotAllowed_TakesPrecedence_OverFull()
        {
            Assert.AreEqual(JoinPolicy.ReasonNotAllowed,
                Evaluate(allow: new[] { "Alice" }, connected: 99, max: 1).Reason);
        }

        [Test]
        public void Contains_NullList_ReturnsFalse()
        {
            Assert.IsFalse(JoinPolicy.Contains(null, "Bob"));
        }

        [Test]
        public void Contains_NullValue_ReturnsFalse()
        {
            Assert.IsFalse(JoinPolicy.Contains(new[] { "Bob" }, null));
        }

        [Test]
        public void Contains_EmptyList_ReturnsFalse()
        {
            Assert.IsFalse(JoinPolicy.Contains(Array.Empty<string>(), "Bob"));
        }

        [Test]
        public void Contains_FindsMatchLaterInList()
        {
            Assert.IsTrue(JoinPolicy.Contains(new[] { "Alice", "Bob" }, "bob"));
        }

        [Test]
        public void Contains_ExactCaseMatters_None()
        {
            Assert.IsTrue(JoinPolicy.Contains(new[] { "Bob" }, "BOB"));
        }

        [Test]
        public void Contains_NoMatch_ReturnsFalse()
        {
            Assert.IsFalse(JoinPolicy.Contains(new[] { "Alice", "Carol" }, "Bob"));
        }
    }
}