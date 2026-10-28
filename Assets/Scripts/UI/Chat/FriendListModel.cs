using System.Collections.Generic;
using MMO.Social;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>Friend-list snapshot behind the friend panel.</summary>
    public sealed class FriendListModel
    {
        private readonly List<FriendView> _friends = new List<FriendView>();

        public IReadOnlyList<FriendView> Friends => _friends;

        public void Apply(string snapshotJson)
        {
            FriendListSnapshot snapshot = null;
            try
            {
                snapshot = JsonUtility.FromJson<FriendListSnapshot>(snapshotJson);
            }
            catch (System.Exception)
            {
                // Ignore a malformed snapshot.
            }

            _friends.Clear();
            if (snapshot?.Friends != null)
                _friends.AddRange(snapshot.Friends);
        }

        public string Text(int maxLines)
        {
            var lines = new List<string>();
            for (int i = 0; i < _friends.Count && lines.Count < maxLines; i++)
            {
                FriendView view = _friends[i];
                string state = view.State switch
                {
                    FriendState.PendingIn => "in",
                    FriendState.PendingOut => "out",
                    FriendState.Accepted => "ok",
                    _ => "?",
                };
                lines.Add($"{(view.Online ? "*" : "-")} {view.PlayerName} [{state}]");
            }

            return lines.Count == 0 ? "No friends yet." : string.Join("\n", lines);
        }

        public string FirstPendingAccountId => First(FriendState.PendingIn);
        public string FirstAcceptedAccountId => First(FriendState.Accepted);

        private string First(byte state)
        {
            for (int i = 0; i < _friends.Count; i++)
            {
                if (_friends[i].State == state)
                    return _friends[i].AccountId;
            }

            return null;
        }
    }
}
