using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using MMO.Net;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Bootstrap
{
    /// <summary>
    /// The Main Menu's login flow, run from a submit: sign in through the API gateway, point the
    /// Tugboat transport at the zone the backend said this player was last standing in (falling
    /// back to the menu's configured default), hand the session token to the join client, and start
    /// the client connection. Only once the server has actually <em>admitted</em> the connection
    /// does the flow record the zone it reached — so the stored "last standing in" answer is always
    /// a server-verified fact, never a guess.
    ///
    /// <para>
    /// A stored zone can outlive the server it names: orchestrated raids are ephemeral containers,
    /// so the recorded address may be dead by the next login. When a <em>stored</em> zone fails and
    /// the menu supplied a different default zone, the connector retries once against that default
    /// and records whichever zone actually admitted it — the stale answer heals itself.
    /// </para>
    ///
    /// <para>
    /// Everything is one awaited method on a plain class so the whole story is unit-testable with a
    /// fake <see cref="IApiGateway"/>; the scene's <see cref="MainMenuBootstrap"/> owns the UI and
    /// submits into it. Progress surfaces through <see cref="StatusChanged"/>.
    /// </para>
    /// </summary>
    public sealed class LoginConnector
    {
        /// <summary>How long a started client connection may take to be admitted before the login is called off.</summary>
        public const int AdmissionTimeoutMilliseconds = 10000;

        private readonly NetworkManager _networkManager;
        private readonly IApiGateway _gateway;
        private readonly JoinRequestClient _joinClient;

        /// <summary>Raised with a player-facing status line at every step of the flow.</summary>
        public event Action<string> StatusChanged;

        public LoginConnector(NetworkManager networkManager, IApiGateway gateway)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _joinClient = networkManager.GetComponent<JoinRequestClient>();
        }

        /// <summary>True while a login is running; a second submit during that time is refused.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>The zone this connector applied (and, on the success path, recorded).</summary>
        public LastZoneInfo LastZone { get; private set; }

        /// <summary>
        /// The full login flow. Returns a success <see cref="LoginResult"/> only after the server
        /// admitted the connection; every refusal — bad input, backend rejection, unreachable zone —
        /// comes back as <see cref="LoginResult.Fail"/> with a player-facing message.
        /// </summary>
        /// <param name="fallbackZone">Zone to join when the player has no stored zone yet.</param>
        public async Task<LoginResult> LoginAndConnectAsync(
            string username, string password, LastZoneInfo fallbackZone = null)
        {
            if (IsBusy)
                return LoginResult.Fail("A login is already in progress.");

            IsBusy = true;
            SetStatus("Signing in...");
            try
            {
                string validationError;
                if (!AuthRules.Validate(username, password, out validationError))
                {
                    SetStatus(validationError);
                    return LoginResult.Fail(validationError);
                }

                LoginResult result = await _gateway.LoginAsync(username.Trim(), password);
                if (!result.Success)
                {
                    SetStatus(result.Error);
                    return result;
                }

                LastZoneInfo storedZone = result.LastZone != null && result.LastZone.IsValid
                    ? result.LastZone
                    : null;
                ApplyZone(storedZone ?? fallbackZone);

                if (_joinClient != null)
                {
                    _joinClient.SetSession(result.Token, result.RefreshToken, result.UserId);
                    _joinClient.SetCredentials(username.Trim(), password);
                    _joinClient.SetPlayerName(result.Username ?? username.Trim());
                }

                SetStatus("Connecting to zone...");
                string failure = await ConnectAndAdmitAsync();

                if (failure != null && ShouldRetryFallbackZone(storedZone, fallbackZone))
                {
                    SetStatus("The saved zone could not be reached. Trying the default zone...");
                    ApplyZone(fallbackZone);
                    failure = await ConnectAndAdmitAsync();
                }

                if (failure != null)
                {
                    SetStatus(failure);
                    return LoginResult.Fail(failure);
                }

                await RecordZoneAsync(result);

                LastZoneInfo reached = LastZone;
                SetStatus($"Connected to {reached}");
                return LoginResult.Ok(result.Token, result.UserId, result.Username, reached);
            }
            catch (OperationCanceledException)
            {
                const string cancelled = "Login was interrupted.";
                SetStatus(cancelled);
                return LoginResult.Fail(cancelled);
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(LoginConnector)} login flow failed: {e.Message}");
                const string failed = "Login failed. Try again.";
                SetStatus(failed);
                return LoginResult.Fail(failed);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyZone(LastZoneInfo zone)
        {
            if (zone == null || !zone.IsValid)
            {
                // Transport stays at whatever it was configured with; the record (if any) is
                // captured below from the transport when the connection comes up.
                return;
            }

            Transport transport = _networkManager.TransportManager?.Transport;
            if (transport == null)
                return;

            transport.SetClientAddress(zone.Ip);
            transport.SetPort((ushort)zone.Port);
            LastZone = zone;
        }

        /// <summary>
        /// One connection attempt: starts the client socket and waits for the gate to admit it.
        /// Returns null on admission, or the player-facing failure message.
        /// </summary>
        private async Task<string> ConnectAndAdmitAsync()
        {
            Transport transport = _networkManager.TransportManager?.Transport;
            Debug.Log($"[JoinClient] Starting client connection to " +
                      $"{(transport != null ? $"{transport.GetClientAddress()}:{transport.GetPort()}" : "<no transport>")}...");

            if (_networkManager.ClientManager == null ||
                !_networkManager.ClientManager.StartConnection())
                return "Could not start the connection to the zone.";

            bool admitted = await WaitForAdmissionAsync(_networkManager);
            if (admitted)
            {
                Debug.Log("[JoinClient] Admitted by the zone.");
                return null;
            }

            // Without this the UI can only ever show the generic timeout line, which hides
            // whether the socket never opened or the gate actively turned us away.
            Debug.LogWarning("[JoinClient] Not admitted within the handshake window. " +
                             $"Client state: {(_networkManager.ClientManager.Started ? "Started (socket up)" : "Not started")}.");
            return "Could not reach the zone server. Try again.";
        }

        /// <summary>
        /// True when a failed <paramref name="storedZone"/> is worth one retry against the menu
        /// default: there must be a usable default that is not simply the zone that just failed.
        /// The stored record outliving an ephemeral raid container is the motivating case.
        /// </summary>
        private static bool ShouldRetryFallbackZone(LastZoneInfo storedZone, LastZoneInfo fallbackZone)
            => storedZone != null &&
               fallbackZone != null &&
               fallbackZone.IsValid &&
               !(string.Equals(storedZone.Ip, fallbackZone.Ip, StringComparison.OrdinalIgnoreCase) &&
                 storedZone.Port == fallbackZone.Port);

        private async Task RecordZoneAsync(LoginResult result)
        {
            LastZoneInfo reached = ReadAppliedZone();
            if (reached == null || !reached.IsValid)
                return;

            try
            {
                await _gateway.SaveLastZoneAsync(result.UserId, result.Token, reached);
                LastZone = reached;
            }
            catch (Exception e)
            {
                // The connection is up; an unrecorded zone must not tear the login down.
                Debug.LogWarning($"Could not record the last zone: {e.Message}");
            }
        }

        /// <summary>
        /// The zone this login applied. The transport's own accessors report the client socket
        /// after connection, which is a local ephemeral port — never the server's listening port
        /// to store for the next login.
        /// </summary>
        private LastZoneInfo ReadAppliedZone()
            => LastZone != null && LastZone.IsValid
                ? LastZone
                : null;

        private void SetStatus(string message) => StatusChanged?.Invoke(message);

        /// <summary>
        /// Waits until the server has admitted the client. FishNet's client "Started" fires when the
        /// socket is up (before the join gate decides) so the completion signal here is
        /// <c>ClientManager.OnAuthenticated</c> — admission, not merely a connected socket. A
        /// stopped connection or the timeout resolves the wait as a failure.
        /// </summary>
        private static async Task<bool> WaitForAdmissionAsync(NetworkManager manager)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnConnectionState(ClientConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Stopped)
                    tcs.TrySetResult(false);
            }

            void OnAuthenticated() => tcs.TrySetResult(true);

            if (IsClientAdmitted(manager))
                return true;

            manager.ClientManager.OnClientConnectionState += OnConnectionState;
            manager.ClientManager.OnAuthenticated += OnAuthenticated;
            try
            {
                Task completed = await Task.WhenAny(tcs.Task, Task.Delay(AdmissionTimeoutMilliseconds));
                return completed == tcs.Task && await tcs.Task;
            }
            finally
            {
                manager.ClientManager.OnClientConnectionState -= OnConnectionState;
                manager.ClientManager.OnAuthenticated -= OnAuthenticated;
            }
        }

        private static bool IsClientAdmitted(NetworkManager manager)
            => manager.ClientManager != null &&
               manager.ClientManager.Connection != null &&
               manager.ClientManager.Connection.IsAuthenticated;
    }
}