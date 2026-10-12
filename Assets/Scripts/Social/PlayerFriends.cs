using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// A player's friend-list surface. Like <see cref="PlayerChat"/>, clients submit intent only;
    /// the server validates against the versioned <c>friends</c> records and pushes a full snapshot
    /// back through <see cref="FriendListBroadcast"/>. Rule refusals and friend toasts share
    /// <see cref="FriendEventBroadcast"/>.
    /// </summary>
    public sealed class PlayerFriends : NetworkBehaviour
    {
        private string _accountId;

        /// <summary>Raised on the owning client with a fresh snapshot JSON.</summary>
        public event Action<string> SnapshotChanged;

        /// <summary>Raised on the owning client for a friend toast (see <see cref="FriendEvent"/>).</summary>
        public event Action<int> EventReceived;

        /// <summary>Raised on the acting client when the server refuses an intent.</summary>
        public event Action<FriendRule> RuleFailed;

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _accountId = ResolveAccountId();
            SocialHubService hub = SocialHubService.Instance;
            if (hub != null && !string.IsNullOrEmpty(_accountId))
                hub.AttachFriends(_accountId, DeliverSnapshot, DeliverEvent);
        }

        public override void OnStopNetwork()
        {
            if (!IsServer)
                return;

            SocialHubService hub = SocialHubService.Instance;
            if (hub != null && !string.IsNullOrEmpty(_accountId))
                hub.DetachFriends(_accountId);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (ClientManager == null)
                return;

            ClientManager.RegisterBroadcast<FriendListBroadcast>(OnFriendList);
            ClientManager.RegisterBroadcast<FriendEventBroadcast>(OnFriendEvent);

            // Pull the current list the moment the player object exists, so the panel is never stale.
            if (IsOwner)
                RpcRequestList();
        }

        public override void OnStopClient()
        {
            if (ClientManager != null)
            {
                ClientManager.UnregisterBroadcast<FriendListBroadcast>(OnFriendList);
                ClientManager.UnregisterBroadcast<FriendEventBroadcast>(OnFriendEvent);
            }

            base.OnStopClient();
        }

        #region Client intent

        public void RequestAdd(string playerName)
        {
            if (!OwnsSelf() || string.IsNullOrWhiteSpace(playerName))
                return;

            RpcRequestAdd(playerName);
        }

        public void RequestAccept(string fromAccountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(fromAccountId))
                return;

            RpcRequestAccept(fromAccountId);
        }

        public void RequestDecline(string fromAccountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(fromAccountId))
                return;

            RpcRequestDecline(fromAccountId);
        }

        public void RequestRemove(string accountId)
        {
            if (!OwnsSelf() || string.IsNullOrEmpty(accountId))
                return;

            RpcRequestRemove(accountId);
        }

        public void RequestList()
        {
            if (OwnsSelf())
                RpcRequestList();
        }

        [ServerRpc]
        private void RpcRequestAdd(string playerName, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveAsync(SocialHubService.Instance.RequestFriendAsync(_accountId, playerName));
        }

        [ServerRpc]
        private void RpcRequestAccept(string fromAccountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveAsync(SocialHubService.Instance.AcceptFriendAsync(_accountId, fromAccountId));
        }

        [ServerRpc]
        private void RpcRequestDecline(string fromAccountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveAsync(SocialHubService.Instance.DeclineFriendAsync(_accountId, fromAccountId));
        }

        [ServerRpc]
        private void RpcRequestRemove(string accountId, NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = ResolveAsync(SocialHubService.Instance.RemoveFriendAsync(_accountId, accountId));
        }

        [ServerRpc]
        private void RpcRequestList(NetworkConnection sender = null)
        {
            if (!ValidSender(sender))
                return;

            _ = SocialHubService.Instance.PushFriendsAsync(_accountId);
        }

        private async System.Threading.Tasks.Task ResolveAsync(System.Threading.Tasks.Task<FriendRule> pending)
        {
            FriendRule rule = await pending;
            if (rule != FriendRule.Ok)
                DeliverRule(rule);
        }

        #endregion

        #region Backend → client delivery

        [Server]
        private void DeliverSnapshot(string snapshotJson)
            => ServerManager.Broadcast(
                Owner,
                new FriendListBroadcast { SnapshotJson = snapshotJson },
                requireAuthenticated: true);

        [Server]
        private void DeliverEvent(int evt)
            => ServerManager.Broadcast(
                Owner,
                new FriendEventBroadcast { Rule = evt },
                requireAuthenticated: true);

        [Server]
        private void DeliverRule(FriendRule rule)
            => ServerManager.Broadcast(
                Owner,
                new FriendEventBroadcast { Rule = -(int)rule },   // negative space = rule failure
                requireAuthenticated: true);

        private void OnFriendList(FriendListBroadcast msg, Channel channel)
            => SnapshotChanged?.Invoke(msg.SnapshotJson);

        private void OnFriendEvent(FriendEventBroadcast msg, Channel channel)
        {
            if (msg.Rule < 0)
                RuleFailed?.Invoke((FriendRule)(-msg.Rule));
            else
                EventReceived?.Invoke(msg.Rule);
        }

        #endregion

        private bool OwnsSelf()
        {
            NetworkObject self = NetworkObject;
            return self != null && self.IsOwner;
        }

        private bool ValidSender(NetworkConnection sender)
            => sender != null && OwnerMatches(sender) && SocialHubService.Instance != null;

        private string ResolveAccountId()
        {
            PlayerPersistenceService service = PlayerPersistenceService.Instance;
            int clientId = Owner != null ? Owner.ClientId : -1;
            if (service != null && service.TryGetJoin(clientId, out string accountId, out _))
                return accountId;

            return null;
        }
    }
}
