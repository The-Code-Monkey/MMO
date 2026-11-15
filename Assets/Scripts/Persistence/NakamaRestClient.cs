using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;
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
    public sealed class NakamaRestClient : INakamaConnected, INakamaSession, INakamaAuthTransport
    {
        public const int RequestTimeoutSeconds = 10;

        private readonly BackendSettings _settings;
        private readonly NakamaSession _session;
        private readonly PinSet _pinSet;

        /// <param name="settings">Backend connection details; null falls back to the defaults.</param>
        /// <param name="pinSet">
        /// Certificate pins for the backend. Null loads the shipped document
        /// (<see cref="PinSetLoader"/>); a set that does not require pinning changes nothing.
        /// </param>
        public NakamaRestClient(BackendSettings settings = null, PinSet pinSet = null)
        {
            _settings = settings ?? new BackendSettings();
            _session = new NakamaSession(_settings, this);
            _pinSet = pinSet ?? PinSetLoader.Load();
        }

        public bool IsConnected => _session.IsConnected;

        /// <summary>The shared service account's user id; the storage owner every object lives under.</summary>
        public string UserId => _session.UserId;

        /// <summary>Scheme://host:port the REST calls are made against.</summary>
        public string BaseUrl => _session.BaseUrl;

        /// <summary>
        /// The socket handshake URL is derived from this, so it mirrors <see cref="BaseUrl"/>.
        /// Part of <see cref="INakamaSession"/>.
        /// </summary>
        public string HttpBaseUrl => _session.HttpBaseUrl;

        public Task ConnectAsync(CancellationToken ct)
            => _session.ConnectAsync(ct);

        public Task DisconnectAsync()
            => _session.DisconnectAsync();

        public Task<string> GetFreshTokenAsync(CancellationToken ct)
            => _session.GetFreshTokenAsync(ct);

        /// <summary>
        /// Calls a registered server-side Lua RPC (<c>POST /v2/rpc/{id}</c>). Nakama wraps the
        /// module's return value as <c>{"payload":"&lt;json string&gt;"}</c>; the inner string is
        /// returned unchanged.
        /// </summary>
        public async Task<string> RpcAsync(string id, string jsonPayload, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            RestRpcEnvelope response = await PostAsync<RestRpcEnvelope>(
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

            var request = new RestReadStorageObjectsRequest { objectIds = new List<RestReadStorageObjectId>() };
            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;

                request.objectIds.Add(new RestReadStorageObjectId
                {
                    collection = collection,
                    key = key,
                    userId = _session.UserId,
                });
            }

            if (request.objectIds.Count == 0)
                return result;

            RestStorageObjectsResponse response = await PostAsync<RestStorageObjectsResponse>(
                "/v2/storage", JsonUtility.ToJson(request), useBasic: false, ct);

            if (response?.objects == null)
                return result;

            foreach (RestStorageObject obj in response.objects)
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
        /// Lists the owned objects in a collection under the shared owner (up to the gateway's
        /// page size). Used by the zone registry, which is discovered by collection rather than by
        /// a known set of keys. Callers match entries by <see cref="StorageObjectData.Key"/>.
        /// </summary>
        public async Task<List<StorageObjectData>> ListObjectsAsync(
            string collection, CancellationToken ct)
        {
            RequireSession();
            await GetFreshTokenAsync(ct);

            var result = new List<StorageObjectData>();
            if (string.IsNullOrEmpty(collection))
                return result;

            string path = "/v2/storage/" + Uri.EscapeDataString(collection)
                + "?user_id=" + Uri.EscapeDataString(_session.UserId ?? string.Empty)
                + "&limit=100";

            RestStorageObjectListResponse response = await GetAsync<RestStorageObjectListResponse>(path, ct);
            if (response?.objects == null)
                return result;

            foreach (RestStorageObject obj in response.objects)
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

            var write = new RestStorageObjectWrite
            {
                collection = collection,
                key = key,
                value = json,
                version = string.IsNullOrEmpty(version) ? "*" : version,
                permissionRead = 1,   // owner read
                permissionWrite = 1,  // owner write
            };

            var request = new RestWriteStorageObjectsRequest
            {
                objects = new List<RestStorageObjectWrite> { write },
            };

            RestWriteStorageObjectsResponse response = await PutAsync<RestWriteStorageObjectsResponse>(
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

            var request = new RestDeleteStorageObjectsRequest
            {
                objectIds = new List<RestDeleteStorageObjectId>
                {
                    new RestDeleteStorageObjectId { collection = collection, key = key },
                },
            };

            await PutAsync<RestEmptyResponse>("/v2/storage/delete", JsonUtility.ToJson(request), ct);
        }

        private void RequireSession()
            => _session.RequireSession();

        Task<T> INakamaAuthTransport.PostAsync<T>(string path, string jsonBody, bool useBasic, CancellationToken ct)
            => PostAsync<T>(path, jsonBody, useBasic, ct);

        Task<T> INakamaAuthTransport.GetAsync<T>(string path, CancellationToken ct)
            => GetAsync<T>(path, ct);

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

            // Pin the gateway when the build ships a pin set. The handler is a no-op unless the set
            // requires pinning and the host is not loopback, so local development is untouched.
            if (_pinSet != null)
            {
                request.certificateHandler = new CertificatePinHandler(_pinSet, PinTarget.Backend, _settings.Host);
                request.disposeCertificateHandlerOnDispose = true;
            }

            request.SetRequestHeader("Accept", "application/json");

            if (jsonBody != null)
                request.SetRequestHeader("Content-Type", "application/json");

            if (useBasic)
                request.SetRequestHeader("Authorization", BuildBasicAuth());
            else if (!string.IsNullOrEmpty(_session.SessionToken))
                request.SetRequestHeader("Authorization", "Bearer " + _session.SessionToken);

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

    }
}
