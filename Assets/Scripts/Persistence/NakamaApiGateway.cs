using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace MMO.Persistence
{
    /// <summary>
    /// Client-facing access to Nakama's REST gateway, using only <c>UnityWebRequest</c> so the
    /// backend needs no third-party .NET package. Where <see cref="NakamaPlayerRepository"/> is the
    /// game server's own backend connection, this is what a client's Main Menu talks to: it signs
    /// the player in with username/password, reads the last-zone record, and hands back a session
    /// token that the join gate later verifies through <see cref="ITokenValidator"/>.
    ///
    /// <para>
    /// <b>Ownership.</b> Nakama scopes every storage write to the authenticated caller — an HTTP
    /// client can never write another user's object — so the last-zone record lives under the
    /// <em>player's own</em> account (read 1 / write 1), written by the client after the transport
    /// successfully connects and read back on the next login. It records only "this player was last
    /// standing in zone X"; it is never the source of input or world state.
    /// </para>
    ///
    /// <para>
    /// HTTP calls must run on the main thread (UnityWebRequest is not thread-safe); sign-in runs
    /// from the menu's button handler and zone writes from the login continuation, so that holds by
    /// construction. The URL and payload builders are public statics so the exact bytes sent are
    /// testable without a socket.
    /// </para>
    /// </summary>
    public sealed class NakamaApiGateway : IApiGateway, ITokenValidator
    {
        /// <summary>Nakama storage collection the player-owned zone record lives under.</summary>
        public const string LastZoneCollection = "player_metadata";

        /// <summary>Nakama storage key for the last-zone record.</summary>
        public const string LastZoneKey = "last_zone";

        public const int RequestTimeoutSeconds = 10;

        private readonly BackendSettings _settings;
        private string _baseUrl;

        public NakamaApiGateway(BackendSettings settings = null)
            => _settings = settings ?? new BackendSettings();

        /// <summary>Scheme://host:port the REST calls are made against.</summary>
        public string BaseUrl => _baseUrl ??= $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";

        #region Builders (pure, unit-testable)

        /// <summary>
        /// Email sign-in with auto-registration. <c>create=true</c> lets a first-time player's
        /// credentials create the account; an existing account matches on the email and the
        /// username query is ignored, exactly like a real login form.
        /// </summary>
        public static string BuildAuthenticateUrl(string baseUrl, string username)
            => $"{baseUrl}/v2/account/authenticate/email?create=true&username={Uri.EscapeDataString(username ?? string.Empty)}";

        public static string BuildAuthenticateJson(string email, string password)
            => JsonUtility.ToJson(new AccountEmailRequest { email = email, password = password });

        public static string BuildZoneReadJson(string userId)
            => JsonUtility.ToJson(new ReadStorageObjectsRequest
            {
                objectIds = new List<ReadStorageObjectId>
                {
                    new ReadStorageObjectId
                    {
                        collection = LastZoneCollection,
                        key = LastZoneKey,
                        userId = userId,
                    },
                },
            });

        /// <summary>
        /// Writes the zone record under the <em>caller's</em> ownership with an empty version —
        /// unconditional upsert, which is what overwriting "where I last stood" needs. Read/write
        /// permission 1 keeps the record visible only to its owner.
        /// </summary>
        public static string BuildZoneWriteJson(LastZoneInfo zone)
            => JsonUtility.ToJson(new WriteStorageObjectsRequest
            {
                objects = new List<StorageObjectWrite>
                {
                    new StorageObjectWrite
                    {
                        collection = LastZoneCollection,
                        key = LastZoneKey,
                        value = JsonUtility.ToJson(zone),
                        version = string.Empty,
                        permissionRead = 1,
                        permissionWrite = 1,
                    },
                },
            });

        /// <summary>Extracts the bearer token from an authenticate response; null when absent.</summary>
        public static string ParseSessionToken(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<LoginSession>(json).token;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Extracts the refresh token from an authenticate or refresh response.</summary>
        public static string ParseRefreshToken(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<LoginSession>(json).refresh_token;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Extracts the identity from a <c>GET /v2/account</c> response; invalid when absent.</summary>
        public static TokenIdentity ParseAccountResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                AccountResponse response = JsonUtility.FromJson<AccountResponse>(json);
                if (response.user == null)
                    return default;

                return new TokenIdentity(response.user.id, response.user.username);
            }
            catch (ArgumentException)
            {
                return default;
            }
        }

        /// <summary>
        /// Extracts the zone record from a storage read response. Missing objects, empty object
        /// lists, and malformed payloads all read as "no zone yet" (null); a foreign payload must
        /// never take the login down.
        /// </summary>
        public static LastZoneInfo ParseZoneReadResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                StorageObjectsResponse response = JsonUtility.FromJson<StorageObjectsResponse>(json);
                if (response.objects == null || response.objects.Count == 0)
                    return null;

                StorageObjectValue obj = response.objects[0];
                if (string.IsNullOrWhiteSpace(obj.value))
                    return null;

                return LastZoneInfo.FromJson(obj.value);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>Extracts the human-readable message from a Nakama error body; null when absent.</summary>
        public static string ParseErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                return JsonUtility.FromJson<NakamaError>(responseBody).message;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        #endregion

        #region IApiGateway

        /// <inheritdoc cref="IApiGateway.LoginAsync"/>
        public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            try
            {
                string email = AuthRules.EmailForUsername(username);
                string authText = await SendAsync(
                    "POST", BuildAuthenticateUrl(BaseUrl, username), BuildAuthenticateJson(email, password),
                    BuildBasicAuth(), null, ct);

                string token = ParseSessionToken(authText);
                if (string.IsNullOrEmpty(token))
                    return LoginResult.Fail("The backend returned no session token.");

                string refreshToken = ParseRefreshToken(authText);

                TokenIdentity identity = await ValidateAsync(token, ct);
                if (!identity.IsValid)
                    return LoginResult.Fail("The backend did not vouch for the session token.");

                // A broken zone read must not fail a valid login: the record is the joining hint,
                // not the login. Log and fall back to the caller's default zone.
                LastZoneInfo zone = null;
                try
                {
                    zone = await LoadLastZoneAsync(identity.UserId, token, ct);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Last-zone record unavailable; joining the default zone. {e.Message}");
                }

                return LoginResult.Ok(token, identity.UserId, identity.Username, zone, refreshToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (NakamaHttpException e)
            {
                string message = ParseErrorMessage(e.ResponseBody);
                if (string.IsNullOrEmpty(message))
                {
                    if (e.StatusCode < 0)
                        message = "Could not reach the backend. Try again in a moment.";
                    else if (e.StatusCode == 401 || e.StatusCode == 404)
                        message = "The username or password is wrong.";
                    else
                        message = $"The backend rejected the login ({e.StatusCode}).";
                }

                return LoginResult.Fail(message);
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(NakamaApiGateway)} login request failed: {e.Message}");
                return LoginResult.Fail("Could not reach the backend. Try again in a moment.");
            }
        }

        /// <inheritdoc cref="IApiGateway.LoadLastZoneAsync"/>
        public async Task<LastZoneInfo> LoadLastZoneAsync(string userId, string token, CancellationToken ct = default)
        {
            RequireToken(token);
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("A user id is required to read the zone record.");

            string text = await SendAsync("POST", BaseUrl + "/v2/storage", BuildZoneReadJson(userId), null, token, ct);
            return ParseZoneReadResponse(text);
        }

        /// <summary>
        /// Renews an expired session token. Returns null when the refresh token was refused; the
        /// caller keeps its old session and lets the server decide what to do with it.
        /// </summary>
        public async Task<SessionTokens> RefreshSessionAsync(
            string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return null;

            try
            {
                // Nakama's refresh endpoint takes the *refresh* token in the "token" field; the
                // access token is not part of the request at all.
                string text = await SendAsync(
                    "POST",
                    BaseUrl + "/v2/account/session/refresh",
                    JsonUtility.ToJson(new RefreshRequest { token = refreshToken }),
                    BuildBasicAuth(), null, ct);

                string token = ParseSessionToken(text);
                if (string.IsNullOrEmpty(token))
                    return null;

                string nextRefresh = ParseRefreshToken(text);
                return new SessionTokens(token, string.IsNullOrEmpty(nextRefresh) ? refreshToken : nextRefresh);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not refresh the session token: {e.Message}");
                return null;
            }
        }

        /// <inheritdoc cref="IApiGateway.SaveLastZoneAsync"/>
        public async Task SaveLastZoneAsync(string userId, string token, LastZoneInfo zone, CancellationToken ct = default)
        {
            if (zone == null)
                throw new ArgumentNullException(nameof(zone));
            if (!zone.IsValid)
                throw new ArgumentException("The zone record must name a real host on a real port.");

            RequireToken(token);
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("A user id is required to store the zone record.");

            string text = await SendAsync("PUT", BaseUrl + "/v2/storage", BuildZoneWriteJson(zone), null, token, ct);

            WriteStorageObjectsResponse response = null;
            try
            {
                response = JsonUtility.FromJson<WriteStorageObjectsResponse>(text);
            }
            catch (ArgumentException)
            {
                // Fall through to the ack check below.
            }

            if (response?.acks == null || response.acks.Count == 0 || string.IsNullOrEmpty(response.acks[0].version))
                throw new InvalidOperationException("The backend did not acknowledge the zone write.");
        }

        #endregion

        #region ITokenValidator

        /// <inheritdoc cref="ITokenValidator.ValidateAsync"/>
        public async Task<TokenIdentity> ValidateAsync(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                return default;

            string text = await SendAsync("GET", BaseUrl + "/v2/account", null, null, token, ct);
            return ParseAccountResponse(text);
        }

        #endregion

        private void RequireToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("A session token is required for this call.");
        }

        private async Task<string> SendAsync(
            string method, string url, string jsonBody, string basic, string bearer, CancellationToken ct)
        {
            using var request = new UnityWebRequest(url, method);
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            if (!string.IsNullOrEmpty(basic))
                request.SetRequestHeader("Authorization", basic);
            else if (!string.IsNullOrEmpty(bearer))
                request.SetRequestHeader("Authorization", "Bearer " + bearer);

            await SendAndWaitAsync(request);

            // A cancelled handshake cancels validation tokens; surface it so a racing continuation
            // never mistakes a cancelled read for a refused token.
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                throw new NakamaHttpException((int)request.responseCode, body);
            }

            return request.downloadHandler != null ? request.downloadHandler.text : null;
        }

        private string BuildBasicAuth()
        {
            byte[] key = Encoding.UTF8.GetBytes(_settings.ServerKey + ":");
            return "Basic " + Convert.ToBase64String(key);
        }

        private static async Task SendAndWaitAsync(UnityWebRequest request)
        {
            AsyncOperation operation = request.SendWebRequest();
            var tcs = new TaskCompletionSource<bool>();
            operation.completed += _ => tcs.TrySetResult(true);
            await tcs.Task;
        }

        /// <summary>A failed HTTP request, carrying the body so the caller can fish out the message.</summary>
        private sealed class NakamaHttpException : Exception
        {
            public int StatusCode { get; }
            public string ResponseBody { get; }

            public NakamaHttpException(int statusCode, string responseBody)
                : base($"Nakama HTTP request failed with status {(int)statusCode}.")
            {
                StatusCode = statusCode;
                ResponseBody = responseBody;
            }
        }

        [Serializable]
        private class AccountEmailRequest
        {
            public string email;
            public string password;
        }

        [Serializable]
        private class LoginSession
        {
            public string token;
            public string refresh_token;
        }

        [Serializable]
        private class RefreshRequest
        {
            public string token;
        }

        [Serializable]
        private class AccountResponse
        {
            public AccountUser user;
        }

        [Serializable]
        private class AccountUser
        {
            public string id;
            public string username;
        }

        [Serializable]
        private class NakamaError
        {
            public string message;
        }

        [Serializable]
        private class ReadStorageObjectsRequest
        {
            public List<ReadStorageObjectId> objectIds;
        }

        [Serializable]
        private class ReadStorageObjectId
        {
            public string collection;
            public string key;
            public string userId;
        }

        [Serializable]
        private class StorageObjectsResponse
        {
            public List<StorageObjectValue> objects;
        }

        [Serializable]
        private class StorageObjectValue
        {
            public string value;
        }

        [Serializable]
        private class WriteStorageObjectsRequest
        {
            public List<StorageObjectWrite> objects;
        }

        [Serializable]
        private class StorageObjectWrite
        {
            public string collection;
            public string key;
            public string value;
            public string version;
            public int permissionRead;
            public int permissionWrite;
        }

        [Serializable]
        private class WriteStorageObjectsResponse
        {
            public List<WriteStorageObjectAck> acks;
        }

        [Serializable]
        private class WriteStorageObjectAck
        {
            public string version;
        }
    }
}