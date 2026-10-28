using MMO.Social;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class FriendListModelTests
    {
        [Test]
        public void Apply_FormatsStateAndOnlineFlag()
        {
            var model = new FriendListModel();
            model.Apply(JsonUtility.ToJson(new FriendListSnapshot
            {
                Friends =
                {
                    new FriendView { AccountId = "a", PlayerName = "Alice", State = FriendState.Accepted, Online = true },
                    new FriendView { AccountId = "b", PlayerName = "Bob", State = FriendState.PendingIn },
                },
            }));

            Assert.AreEqual("* Alice [ok]\n- Bob [in]", model.Text(16));
        }

        [Test]
        public void Apply_EmptySnapshotShowsPlaceholder()
        {
            var model = new FriendListModel();
            model.Apply("{}");

            Assert.AreEqual("No friends yet.", model.Text(16));
        }

        [Test]
        public void FirstPendingAndAccepted_ReturnTheMatchingAccount()
        {
            var model = new FriendListModel();
            model.Apply(JsonUtility.ToJson(new FriendListSnapshot
            {
                Friends =
                {
                    new FriendView { AccountId = "b", PlayerName = "Bob", State = FriendState.PendingIn },
                    new FriendView { AccountId = "a", PlayerName = "Alice", State = FriendState.Accepted },
                },
            }));

            Assert.AreEqual("b", model.FirstPendingAccountId);
            Assert.AreEqual("a", model.FirstAcceptedAccountId);
        }

        [Test]
        public void FirstPendingAndAccepted_NullWhenAbsent()
        {
            var model = new FriendListModel();
            model.Apply("{}");

            Assert.IsNull(model.FirstPendingAccountId);
            Assert.IsNull(model.FirstAcceptedAccountId);
        }
    }
}
