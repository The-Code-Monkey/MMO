using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Net
{
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Connection Gate")]
    public sealed class MMOConnectionGate : Authenticator
    {
        [SerializeField] private MMOConnectionSettings _settings = new();

        private readonly JoinHandshakeTracker _tracker = new();

        /// Called by FishNet's ServerManager. Do NOT conclude anything here — we have no identity yet.
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public MMOConnectionSettings Settings => _settings;
        public int PendingHandshakes => _tracker.Count;

        public void SetSettings(MMOConnectionSettings settings)
            => _settings = settings ?? new MMOConnectionSettings();

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);

            NetworkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            // Unauthenticated on purpose: this is the only channel open to a pending client.
            NetworkManager.ServerManager.RegisterBroadcast<JoinRequestBroadcast>(
                OnJoinRequestBroadcast, requireAuthentication: false);
        }

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            if (connection == null)
                return;

            // The host's own client always gets in, no handshake needed.
            if (connection.IsLocalClient)
            {
                OnAuthenticationResult?.Invoke(connection, true);
                return;
            }

            _tracker.Begin(connection.ClientId, Time.unscaledTimeAsDouble);
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped)
                _tracker.End(connection.ClientId);
        }

        private void OnJoinRequestBroadcast(NetworkConnection connection, JoinRequestBroadcast msg, Channel channel)
            => EvaluateJoin(connection, msg.PlayerName);

        /// Public so tests can drive a decision without a socket.
        public void EvaluateJoin(NetworkConnection connection, string playerName)
        {
            if (connection == null)
                return;

            var request = new JoinRequest(
                playerName,
                CountAuthenticatedClients(),
                _settings.MaxClients,
                _tracker.Elapsed(connection.ClientId, Time.unscaledTimeAsDouble));

            JoinDecision decision = JoinPolicy.Evaluate(
                request,
                _settings.MaintenanceMode,
                _settings.AllowedPlayers,
                _settings.BlockedPlayers,
                _settings.HandshakeTimeoutSeconds);

            if (!decision.Approved)
            {
                NetworkManager.ServerManager.Broadcast(connection, new JoinResultBroadcast
                {
                    Approved = false,
                    Reason = decision.Reason
                }, requireAuthenticated: false);
            }

            _tracker.End(connection.ClientId);
            OnAuthenticationResult?.Invoke(connection, decision.Approved);
        }

        /// Rejects clients that connected but never identified themselves.
        public int ReapTimedOutConnections()
        {
            List<int> expired = _tracker.Expired(Time.unscaledTimeAsDouble, _settings.HandshakeTimeoutSeconds);
            foreach (int clientId in expired)
            {
                if (!NetworkManager.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection conn))
                    continue;

                EvaluateJoin(conn, string.Empty);
            }

            return expired.Count;
        }

        private int CountAuthenticatedClients()
        {
            int count = 0;
            foreach (NetworkConnection conn in NetworkManager.ServerManager.Clients.Values)
            {
                if (conn.IsAuthenticated)
                    count++;
            }

            return count;
        }

        private void Update()
        {
            if (NetworkManager != null && NetworkManager.IsServerStarted)
                ReapTimedOutConnections();
        }
    }
}
