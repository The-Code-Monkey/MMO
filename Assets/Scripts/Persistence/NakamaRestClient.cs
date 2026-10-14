using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using UnityEngine;
using UnityEngine.Networking;

namespace MMO.Persistence
{
    /// <summary>Implemented by repositories that hold a persistent server-side connection.</summary>
    public interface INakamaConnected
    {
        Task ConnectAsync(CancellationToken ct);
        Task DisconnectAsync();
    }

    /// <summary>One storage object as the gateway returns it: the raw value plus its revision hash.</summary>
    public sealed class StorageObjectData
    {
        public string Collection;
        public string Key;
        public string Value;
        public string Version;
    }

    /// <summary>
    /// Thrown when an optimistic <em>if-match</em> storage write loses the race — Nakama answers
    /// 409 because another server instance moved the object after this caller read its version.
    /// The caller re-reads the object and retries rather than clobbering the other writer.
    /// </summary>
    public sealed class StorageVersionConflictException : Exception
    {
        public StorageVersionConflictException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// The single <c>UnityWebRequest</c> connection to Nakama's REST gateway that every
    /// server-side repository shares. It owns the shared service-account handshake and the raw
    /// storage primitives (<see cref="ReadObjectAsync"/>, <see cref="WriteObjectAsync"/>, …); the
    /// typed repositories (<see cref="NakamaPlayerRepository"/>, <see cref="NakamaSocialRepository"/>)
    /// only translate between those primitives and their records.
    ///
    /// <para>
    /// The server authenticates once as the shared service account, and every object is read and
    /// written under that one owner — Nakama scopes storage permissions to the authenticated
    /// caller, so one shared account is what lets any number of game servers read the same source
    /// of truth. Writes carry the version a caller loaded (or "*" for create-if-not-exists), so two
    /// servers racing a write collide on the version instead of silently overwriting each other.
    /// </para>
    ///
    /// <para>
    /// All calls must run on the main thread (<c>UnityWebRequest</c> is not thread-safe). The
    /// services drive every call from the main thread's <c>Update</c>/continuations, so that holds
    /// by construction.
    /// </para>
    /// </summary>
    public sealed class NakamaRestClient : INakamaConnected, INakamaSession
    {
        public const int RequestTimeoutSeconds = 10;

        /// <summary>Refresh the session when fewer than this many seconds of life remain.</summary>
        public const int RefreshMarginSeconds = 30;

        /// <summary>How long a freshly issued/refreshed token is treated as valid for.</summary>
        public const int AssumedTokenLifetimeSeconds = 45;

        private readonly BackendSettings _settings;
        private string _baseUrl;
        private string _sessionToken;
        private string _refreshToken;
        private string _userId;
        private DateTime _tokenValidUntil = DateTime.MinValue;

        public NakamaRestClient(BackendSettings settings = null)
            => _settings = settings ?? new BackendSettings();

        public bool IsConnected => !string.IsNullOrEmpty(_sessionToken);

        /// <summary>The shared service account's user id; the storage owner every object lives under.</summary>
        public string UserId => _userId;

        /// <summary>Scheme://host:port the REST calls are made against.</summary>
        public string BaseUrl => _baseUrl ??= $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";

        /// <summary>
        /// The socket handshake URL is derived from this, so it mirrors <see cref="BaseUrl"/>.
        /// Part of <see cref="INakamaSession"/>.
        /// </summary>
        public string HttpBaseUrl => BaseUrl;

        public async Task ConnectAsync(CancellationToken ct)
        {
            _baseUrl = $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";

            AuthSession session = await PostAsync<AuthSession>(
                "/v2/account/authenticate/device?create=true&username=" + Uri.EscapeDataString(_settings.ServerUsername),
                JsonUtility.ToJson(new AccountDeviceRequest { id = _settings.ServerDeviceId }),
                useBasic: true,
                ct);

            if (session == null || string.IsNullOrEmpty(session.token))
                throw new InvalidOperationException("Nakama returned a session without a token.");

            _sessionToken = session.token;
            _refreshToken = session.refresh_token;
            _tokenValidUntil = DateTime.UtcNow.AddSeconds(AssumedTokenLifetimeSeconds);

            AccountResponse account = await GetAsync<AccountResponse>("/v2/account", ct);
            if (account?.user == null || string.IsNullOrEmpty(account.user.id))
                throw new InvalidOperationException("Nakama returned a session without a user id.");

            _userId = account.user.id;
        }

        public Task DisconnectAsync()
        {
            _sessionToken = null;
            _refreshToken = null;
            _userId = null;
            _tokenValidUntil = DateTime.MinValue;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns a session token that is valid for at least another
        /// <see cref="RefreshMarginSeconds"/> seconds. Nakama's default session expiry is 60 s, so a
        /// long-running zone server outlives its first token by definition: the socket reconnect
        /// path and every long-idle REST call refresh through here before they go stale.
        /// </summary>
        public async Task<string> GetFreshTokenAsync(CancellationToken ct)
        {
            RequireSession();

            if (_tokenValidUntil > DateTime.UtcNow.AddSeconds(RefreshMarginSeconds) ||
                string.IsNullOrEmpty(_refreshToken))
                return _sessionToken;

            RefreshResponse refreshed = await PostAsync<RefreshResponse>(
                "/v2/account/session/refresh",
                JsonUtility.ToJson(new RefreshRequest { token = _sessionToken, refresh_token = _refreshToken }),
                useBasic: true,
                ct);

            if (refreshed != null && !string.IsNullOrEmpty(refreshed.token))
            {
                _sessionToken = refreshed.token;
                if (!string.IsNullOrEmpty(refreshed.refresh_token))
                    _refreshToken = refreshed.refresh_token;

                _tokenValidUntil = DateTime.UtcNow.AddSeconds(AssumedTokenLifetimeSeconds);
            }

            return _sessionToken;
        }

        /// <summary>
        /// Calls a registered server-side Lua RPC (<c>POST /v2/rpc/{id}</c>). Nakama wraps the
        /// module's return value as <c>{"payload":"&lt;json string&gt;"}</c>; the inner string is
        /// returned unchanged.
        /// </summary>
        public async Task<string> RpcAsync(string id, string jsonPayload, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            RpcEnvelope response = await PostAsync<RpcEnvelope>(
                "/v2/rpc/" + id, jsonPayload, useBasic: false, ct);
            return response?.payload;
        }

        /// <summary>
        /// Calls a server-side Lua RPC and deserialises the module's own return value. The
        /// <c>?unwrap</c> query makes Nakama answer with the raw payload object instead of wrapping
        /// it as <c>{"payload":"&lt;json string&gt;"}</c>, so a module returning <c>{"ok":true}</c>
        /// lands directly in <typeparamref name="TResponse"/>.
        /// </summary>
        public async Task<TResponse> CallRpcAsync<TResponse>(
            string rpcId, string jsonPayload, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            return await PostAsync<TResponse>(
                "/v2/rpc/" + rpcId + "?unwrap", jsonPayload, useBasic: false, ct);
        }

        /// <summary>Reads one owned storage object, or null when it does not exist.</summary>
        public async Task<StorageObjectData> ReadObjectAsync(string collection, string key, CancellationToken ct)
        {
            List<StorageObjectData> objects = await ReadObjectsAsync(collection, new[] { key }, ct);
            return objects.Count > 0 ? objects[0] : null;
        }

        /// <summary>
        /// Batch-reads up to the gateway's 100-object limit under the shared owner. Results are
        /// returned in the order the objects came back; callers match them by
        /// <see cref="StorageObjectData.Key"/>.
        /// </summary>
        public async Task<List<StorageObjectData>> ReadObjectsAsync(
            string collection, IReadOnlyList<string> keys, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            var result = new List<StorageObjectData>();
            if (keys == null || keys.Count == 0)
                return result;

            var request = new ReadStorageObjectsRequest { objectIds = new List<ReadStorageObjectId>() };
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                request.objectIds.Add(new ReadStorageObjectId
                {
                    collection = collection,
                    key = key,
                    userId = _userId,
                });
            }

            if (request.objectIds.Count == 0)
                return result;

            StorageObjectsResponse response = await PostAsync<StorageObjectsResponse>(
                "/v2/storage", JsonUtility.ToJson(request), useBasic: false, ct);

            if (response?.objects == null)
                return result;

            foreach (StorageObject obj in response.objects)
            {
                result.Add(new StorageObjectData
                {
                    Collection = obj.collection,
                    Key = obj.key,
                    Value = obj.value,
                    Version = obj.version,
                });
            }

            return result;
        }

        /// <summary>
        /// Writes one owned object and returns its new version. An empty <paramref name="version"/>
        /// means "no object yet" and becomes Nakama's "*" create-if-not-exists; any other value is an
        /// optimistic if-match. A 409 surfaces as <see cref="StorageVersionConflictException"/>.
        /// </summary>
        public async Task<string> WriteObjectAsync(
            string collection, string key, string json, string version, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            var write = new StorageObjectWrite
            {
                collection = collection,
                key = key,
                value = json,
                version = string.IsNullOrEmpty(version) ? "*" : version,
                permissionRead = 1,   // owner read
                permissionWrite = 1,  // owner write
            };

            var request = new WriteStorageObjectsRequest
            {
                objects = new List<StorageObjectWrite> { write },
            };

            WriteStorageObjectsResponse response = await PutAsync<WriteStorageObjectsResponse>(
                "/v2/storage", JsonUtility.ToJson(request), ct);

            if (response?.acks == null || response.acks.Count == 0 || string.IsNullOrEmpty(response.acks[0].version))
                throw new InvalidOperationException("The backend did not acknowledge the storage write.");

            return response.acks[0].version;
        }

        /// <summary>Deletes one owned object. Missing objects are a no-op.</summary>
        public async Task DeleteObjectAsync(string collection, string key, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            var request = new DeleteStorageObjectsRequest
            {
                objectIds = new List<DeleteStorageObjectId>
                {
                    new DeleteStorageObjectId { collection = collection, key = key },
                },
            };

            await PutAsync<EmptyResponse>("/v2/storage/delete", JsonUtility.ToJson(request), ct);
        }

        private void RequireSession()
        {
            if (string.IsNullOrEmpty(_sessionToken) || string.IsNullOrEmpty(_userId))
                throw new InvalidOperationException(
                    $"{nameof(NakamaRestClient)} is not connected to the backend.");
        }

        private Task<T> GetAsync<T>(string path, CancellationToken ct)
            => SendAsync<T>("GET", path, null, false, ct);

        private Task<T> PostAsync<T>(string path, string jsonBody, bool useBasic, CancellationToken ct)
            => SendAsync<T>("POST", path, jsonBody, useBasic, ct);

        private Task<T> PutAsync<T>(string path, string jsonBody, CancellationToken ct)
            => SendAsync<T>("PUT", path, jsonBody, false, ct);

        private async Task<T> SendAsync<T>(string method, string path, string jsonBody, bool useBasic, CancellationToken ct)
        {
            string url = BaseUrl + path;

            using var request = new UnityWebRequest(url, method);
            if (jsonBody != null)
            {
                byte[] body = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(body);
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            if (jsonBody != null)
                request.SetRequestHeader("Content-Type", "application/json");

            if (useBasic)
                request.SetRequestHeader("Authorization", BuildBasicAuth());
            else if (!string.IsNullOrEmpty(_sessionToken))
                request.SetRequestHeader("Authorization", "Bearer " + _sessionToken);

            await SendAndWaitAsync(request);

            // A timed-out handshake cancels its read tokens; surface that so a racing continuation
            // never mistakes a cancelled read for a fresh object.
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                int status = (int)request.responseCode;
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;

                if (status == 409)
                    throw new StorageVersionConflictException(
                        $"Nakama {method} {url} lost an optimistic version check (409).");

                throw new InvalidOperationException(
                    $"Nakama {method} {url} failed ({status}): {request.error}" +
                    (string.IsNullOrEmpty(body) ? string.Empty : " - " + body));
            }

            string text = request.downloadHandler != null ? request.downloadHandler.text : null;
            if (string.IsNullOrEmpty(text))
                return default;

            return JsonUtility.FromJson<T>(text);
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

        [Serializable]
        private class AccountDeviceRequest
        {
            public string id;
        }

        [Serializable]
        private class AuthSession
        {
            public string token;

            // Nakama returns snake_case; JsonUtility is an exact match.
            public string refresh_token;
        }

        [Serializable]
        private class RefreshRequest
        {
            public string token;
            public string refresh_token;
        }

        [Serializable]
        private class RefreshResponse
        {
            public string token;
            public string refresh_token;
        }

        [Serializable]
        private class RpcEnvelope
        {
            public string id;
            public string payload;
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
            public List<StorageObject> objects;
        }

        [Serializable]
        private class StorageObject
        {
            public string collection;
            public string key;
            public string userId;
            public string value;
            public string version;
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
            public List<StorageObjectAck> acks;
        }

        [Serializable]
        private class StorageObjectAck
        {
            public string version;
        }

        [Serializable]
        private class DeleteStorageObjectsRequest
        {
            public List<DeleteStorageObjectId> objectIds;
        }

        [Serializable]
        private class DeleteStorageObjectId
        {
            public string collection;
            public string key;
        }

        [Serializable]
        private class EmptyResponse
        {
        }
    }
}
