using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The single-writer ownership registry, pinned in EditMode. Because it is plain C# with no
    /// server, the duplicate-refusal guarantee — the structural half of dupe protection — is
    /// asserted directly: an id held by one holder can never be granted to another.
    /// </summary>
    public sealed class ItemOwnershipRegistryTests
    {
        private ItemOwnershipRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new ItemOwnershipRegistry();
        }

        [Test]
        public void Grant_FirstTime_IsOk()
        {
            Assert.AreEqual(OwnershipMove.Ok, _registry.Grant("i1", "alice"));
            Assert.IsTrue(_registry.IsHeldBy("i1", "alice"));
        }

        [Test]
        public void Grant_SameHolderAgain_IsIdempotentOk()
        {
            _registry.Grant("i1", "alice");
            Assert.AreEqual(OwnershipMove.Ok, _registry.Grant("i1", "alice"),
                "Re-granting to the current holder is not a dupe; it is the same item in the same hands.");
        }

        [Test]
        public void Grant_Twice_SecondHolderIsDuplicateDetected()
        {
            Assert.AreEqual(OwnershipMove.Ok, _registry.Grant("i1", "alice"));
            Assert.AreEqual(OwnershipMove.DuplicateDetected, _registry.Grant("i1", "bob"));
            Assert.IsTrue(_registry.IsHeldBy("i1", "alice"), "The refused grant must not move the item.");
        }

        [Test]
        public void Grant_EmptyId_IsUnknownItem()
        {
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Grant(null, "alice"));
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Grant(string.Empty, "alice"));
        }

        [Test]
        public void Grant_EmptyHolder_IsUnknownItem()
        {
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Grant("i1", null));
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Grant("i1", string.Empty));
        }

        [Test]
        public void Transfer_FromCurrentHolder_MovesOwnership()
        {
            _registry.Grant("i1", "alice");

            Assert.AreEqual(OwnershipMove.Ok, _registry.Transfer("i1", "alice", "bob"));
            Assert.IsTrue(_registry.IsHeldBy("i1", "bob"));
            Assert.IsFalse(_registry.IsHeldBy("i1", "alice"));
        }

        [Test]
        public void Transfer_FromANonHolder_IsNotHeld()
        {
            _registry.Grant("i1", "alice");

            Assert.AreEqual(OwnershipMove.NotHeld, _registry.Transfer("i1", "mallory", "bob"));
            Assert.IsTrue(_registry.IsHeldBy("i1", "alice"), "A non-holder cannot move someone else's item.");
        }

        [Test]
        public void Transfer_UnknownItem_IsUnknownItem()
        {
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Transfer("ghost", "alice", "bob"));
        }

        [Test]
        public void Transfer_AfterTransfer_TheOldHolderIsNotHeld()
        {
            _registry.Grant("i1", "alice");
            _registry.Transfer("i1", "alice", "bob");

            Assert.AreEqual(OwnershipMove.NotHeld, _registry.Transfer("i1", "alice", "carol"),
                "A holder who already gave the item away cannot give it again — the dupe attempt.");
            Assert.IsTrue(_registry.IsHeldBy("i1", "bob"));
        }

        [Test]
        public void Transfer_ToTheCurrentHolder_IsOk()
        {
            _registry.Grant("i1", "alice");
            Assert.AreEqual(OwnershipMove.Ok, _registry.Transfer("i1", "alice", "alice"));
        }

        [Test]
        public void Release_ByOwner_RemovesTracking()
        {
            _registry.Grant("i1", "alice");

            Assert.AreEqual(OwnershipMove.Ok, _registry.Release("i1", "alice"));
            Assert.IsFalse(_registry.TryGetOwner("i1", out _));
            Assert.AreEqual(OwnershipMove.UnknownItem, _registry.Release("i1", "alice"));
        }

        [Test]
        public void Release_ByNonOwner_IsNotHeld()
        {
            _registry.Grant("i1", "alice");

            Assert.AreEqual(OwnershipMove.NotHeld, _registry.Release("i1", "bob"));
            Assert.IsTrue(_registry.IsHeldBy("i1", "alice"));
        }

        [Test]
        public void TryGetOwner_ReportsTheCurrentHolder()
        {
            _registry.Grant("i1", "alice");

            Assert.IsTrue(_registry.TryGetOwner("i1", out string holder));
            Assert.AreEqual("alice", holder);

            Assert.IsFalse(_registry.TryGetOwner("ghost", out string missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void Count_TracksGrantedAndReleasedItems()
        {
            _registry.Grant("i1", "alice");
            _registry.Grant("i2", "bob");
            Assert.AreEqual(2, _registry.Count);

            _registry.Release("i1", "alice");
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void Clear_DropsEverything()
        {
            _registry.Grant("i1", "alice");
            _registry.Clear();

            Assert.AreEqual(0, _registry.Count);
            Assert.IsFalse(_registry.TryGetOwner("i1", out _));
        }
    }
}
