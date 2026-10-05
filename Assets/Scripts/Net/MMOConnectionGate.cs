using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// The join gate. FishNet 4.x removed the old connection-approval callback, so the
    /// supported hook is <see cref="Authenticator"/>: FishNet calls
    /// <see cref="OnRemoteConnection"/> once a client has finished the version handshake,
    /// and promotes or drops the connection purely based on what we report through
    /// <see cref="OnAuthenticationResult"/>.
    ///
    /// At that point the server knows nothing about the player — not even a name — so the
    /// gate defers the verdict: the connection is parked in a
    /// <see cref="JoinHandshakeTracker"/> until the client announces itself over an
    /// *unauthenticated* broadcast, or until the configured handshake timeout runs out.
    /// That is the same shape as FishNet's own password authenticator.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Connection Gate")]
    public sealed class MMOConnectionGate : Authenticator
    {
        /// <summary>
        /// Reported when an already authenticated client replays a join request.
        /// </summary>
        public const string ReasonAlreadyJoined = "A join request was already accepted.";

        [SerializeField] private MMOConnectionSettings _settings = new();

        private readonly JoinHandshakeTracker _tracker = new();

        /// <summary>
        /// Consumed by FishNet's ServerManager. Approving promotes the connection,
        /// rejecting disconnects it.
        /// </summary>
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public MMOConnectionSettings Settings => _settings;

        /// <summary>
        /// Connections that have arrived but not yet identified themselves.
        /// </summary>
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

        /// <summary>
        /// Called by FishNet once a client has completed the version handshake. No verdict
        /// is possible yet, so the connection is only parked and given a deadline.
        /// </summary>
        public override void OnRemoteConnection(NetworkConnection connection)
        {
            if (connection == null)
                return;

            _tracker.Begin(connection.ClientId, Time.unscaledTimeAsDouble);
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            _tracker.End(connection.ClientId);
        }

        private void OnJoinRequestBroadcast(NetworkConnection connection, JoinRequestBroadcast msg, Channel channel)
            => HandleJoinRequest(connection, msg.PlayerName);

        /// <summary>
        /// Server side entry point for a client's "this is who I am" message.
        /// Public so tests can drive it without a socket.
        /// </summary>
        public void HandleJoinRequest(NetworkConnection connection, string playerName)
        {
            // A join request from an already authenticated client is a replay; a legitimate
            // client only ever announces itself once, before it is admitted.
            if (connection != null && connection.IsAuthenticated)
            {
                Apply(connection, JoinDecision.Reject(ReasonAlreadyJoined));
                return;
            }

            EvaluateJoin(connection, playerName);
        }

        /// <summary>
        /// Applies the configured rules to a join attempt. Counts are supplied by the
        /// caller so the policy can be exercised without a live server.
        /// </summary>
        public JoinDecision Decide(string playerName, int connectedClients, float secondsSinceConnected)
        {
            var request = new JoinRequest(playerName, connectedClients, _settings.MaxClients, secondsSinceConnected);

            return JoinPolicy.Evaluate(
                request,
                _settings.MaintenanceMode,
                _settings.AllowedPlayers,
                _settings.BlockedPlayers,
                _settings.HandshakeTimeoutSeconds);
        }

        /// <summary>
        /// Decides a join attempt from live server state and commits the outcome.
        /// </summary>
        public void EvaluateJoin(NetworkConnection connection, string playerName)
        {
            if (connection == null)
                return;

            float elapsed = _tracker.Elapsed(connection.ClientId, Time.unscaledTimeAsDouble);
            Apply(connection, Decide(playerName, CountAuthenticatedClients(), elapsed));
        }

        /// <summary>
        /// Commits a decision: tells the client why it was turned away, drops its
        /// handshake deadline and hands the verdict to FishNet.
        /// </summary>
        public void Apply(NetworkConnection connection, JoinDecision decision)
        {
            if (connection == null)
                return;

            if (!decision.Approved)
                SendRejection(connection, decision.Reason);

            _tracker.End(connection.ClientId);
            OnAuthenticationResult?.Invoke(connection, decision.Approved);
        }

        private void SendRejection(NetworkConnection connection, string reason)
        {
            if (NetworkManager == null || NetworkManager.ServerManager == null)
                return;

            NetworkManager.ServerManager.Broadcast(connection, new JoinResultBroadcast
            {
                Approved = false,
                Reason = reason
            }, requireAuthenticated: false);
        }

        /// <summary>
        /// Turns away clients that connected but never identified themselves.
        /// </summary>
        /// <param name="now">Unscaled time to evaluate against.</param>
        /// <returns>How many pending handshakes had expired.</returns>
        public int ReapTimedOutConnections(double now)
        {
            List<int> expired = _tracker.Expired(now, _settings.HandshakeTimeoutSeconds);

            // Reaping only means anything while a server is up; with no ServerManager
            // there is nothing to disconnect, but the count is still worth reporting.
            if (NetworkManager != null && NetworkManager.ServerManager != null)
            {
                for (int i = 0; i < expired.Count; i++)
                {
                    if (NetworkManager.ServerManager.Clients.TryGetValue(expired[i], out NetworkConnection conn))
                        Apply(conn, JoinDecision.Reject(JoinPolicy.ReasonTimedOut));
                }
            }

            return expired.Count;
        }

        private int CountAuthenticatedClients()
        {
            if (NetworkManager == null || NetworkManager.ServerManager == null)
                return 0;

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
                ReapTimedOutConnections(Time.unscaledTimeAsDouble);
        }
    }
}