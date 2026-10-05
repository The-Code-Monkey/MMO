using System;
using System.Collections.Generic;
using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace MMO.Networking
{
    public sealed class JoinGateAuthenticator : Authenticator
    {
        [SerializeField] private float _timeoutSeconds = 10f;

        private readonly JoinPolicy _policy = new();
        private readonly Dictionary<int, NetworkConnection> _connections = new();
        private JoinHandshakeTracker _tracker;

        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            _tracker = new JoinHandshakeTracker(_timeoutSeconds);
            NetworkManager.ServerManager.RegisterBroadcast<JoinRequestBroadcast>(OnJoinRequest, false);
        }

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            if (connection == null)
                return;

            _connections[connection.ClientId] = connection;
            _tracker.AddPending(connection.ClientId, Time.unscaledTime);
        }

        private void OnJoinRequest(NetworkConnection conn, JoinRequestBroadcast broadcast, Channel channel)
        {
            if (conn == null || conn.IsAuthenticated)
                return;

            _tracker.TryRemove(conn.ClientId);
            _connections.Remove(conn.ClientId);

            bool approved = _policy.Approve(broadcast.PlayerName);
            OnAuthenticationResult?.Invoke(conn, approved);
        }

        private void Update()
        {
            if (_tracker == null)
                return;

            float now = Time.unscaledTime;
            IReadOnlyList<int> expired = _tracker.CollectExpired(now);
            for (int i = 0; i < expired.Count; i++)
            {
                int key = expired[i];
                _tracker.TryRemove(key);
                if (_connections.TryGetValue(key, out NetworkConnection conn))
                {
                    _connections.Remove(key);
                    OnAuthenticationResult?.Invoke(conn, false);
                }
            }
        }
    }
}
