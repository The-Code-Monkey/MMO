using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using MMO.Persistence;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    ///
    /// When a <see cref="PlayerLoader"/> is configured, the verdict is deferred one step further:
    /// the player's save record is fetched from the backend before the client is admitted, so no
    /// player ever spawns with default stats. The load runs inside the existing handshake window,
    /// racing the timeout reaper; the continuation re-checks that the handshake is still live
    /// before committing, so a reaped client is never accidentally admitted.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/Net/Connection Gate")]
    public sealed class MMOConnectionGate : Authenticator
    {
        /// <summary>
        /// Reported when an already authenticated client replays a join request.
        /// </summary>
        public const string ReasonAlreadyJoined = "A join request was already accepted.";

        /// <summary>Reported when a client connects without the account id the backend keys on.</summary>
        public const string ReasonMissingAccountId = "A valid account id is required.";

        [SerializeField] private MMOConnectionSettings _settings = new();

        private readonly JoinHandshakeTracker _tracker = new();

        /// <summary>
        /// Consumed by FishNet's ServerManager. Approving promotes the connection,
        /// rejecting disconnects it.
        /// </summary>
        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        /// <summary>
        /// Optional persistence seam. When null the gate decides joins synchronously (backwards
        /// compatible with the pre-database gate); when set, the verdict waits for the loaded save.
        /// </summary>
        public IPlayerLoader PlayerLoader { get; set; }

        /// <summary>
        /// Raised once an accepted join's save record is loaded (or determined to be fresh), so the
        /// persistence service can cache it for the player object that spawns next.
        /// </summary>
        public event Action<int, string, PlayerSaveRecord> PlayerJoinLoaded;

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
            => HandleJoinRequest(connection, msg.PlayerName, msg.AccountId);

        /// <summary>
        /// Server side entry point for a client's "this is who I am" message.
        /// Public so tests can drive it without a socket.
        /// </summary>
        /// <param name="accountId">Stable account key; required once a <see cref="PlayerLoader"/> is wired.</param>
        public void HandleJoinRequest(NetworkConnection connection, string playerName, string accountId = null)
        {
            // A join request from an already authenticated client is a replay; a legitimate
            // client only ever announces itself once, before it is admitted.
            if (connection != null && connection.IsAuthenticated)
            {
                Apply(connection, JoinDecision.Reject(ReasonAlreadyJoined));
                return;
            }

            // No backend wired: behave like the pre-database gate and decide immediately.
            if (PlayerLoader == null)
            {
                EvaluateJoin(connection, playerName);
                return;
            }

            if (string.IsNullOrWhiteSpace(accountId))
            {
                Apply(connection, JoinDecision.Reject(ReasonMissingAccountId));
                return;
            }

            int clientId = connection != null ? connection.ClientId : -1;
            _ = LoadAndDecideAsync(connection, clientId, playerName, accountId);
        }

        /// <summary>
        /// Fetches the player's save record, then commits the verdict using the cached policy. Runs
        /// to completion on the Unity main thread (repo awaits on the captured SynchronizationContext)
        /// and abandons quietly if the handshake was reaped or disconnected while the read was in
        /// flight — <c>Apply</c> on a dead handshake would bless a player the reaper already turned
        /// away.
        /// </summary>
        private async Task LoadAndDecideAsync(
            NetworkConnection connection, int clientId, string playerName, string accountId)
        {
            PlayerSaveRecord record = null;
            try
            {
                record = await PlayerLoader.LoadAsync(accountId, _tracker.TokenFor(clientId));
            }
            catch (OperationCanceledException)
            {
                return; // handshake ended while loading; nothing to decide.
            }
            catch (Exception e)
            {
                // The loader owns fail-open semantics; this is a safety net for a throwing stub.
                Debug.LogError($"Save load failed for '{accountId}': {e.Message}");
            }

            // Timed out or disconnected while reading — applying now would admit a ghost.
            if (!_tracker.IsPending(clientId))
                return;

            PlayerJoinLoaded?.Invoke(clientId, accountId, record);

            Apply(connection,
                Decide(playerName, CountAuthenticatedClients(), _tracker.Elapsed(clientId, Time.unscaledTimeAsDouble)));
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