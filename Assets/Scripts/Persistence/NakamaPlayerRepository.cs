using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// Talks to Nakama's REST gateway (the same API its gRPC server exposes over HTTP) using only
    /// <c>UnityWebRequest</c>, so the backend needs no third-party .NET package to load under
    /// IL2CPP. The server authenticates once as a shared service account and every player record —
    /// collection "players", key = the player's AccountId — is read and written under that single
    /// owner, which is exactly the authoritative-server pattern this issue asks for: clients never
    /// touch storage directly.
    ///
    /// <para>
    /// Optimistic concurrency: a save carries the version read with the record (or "*" for a first
    /// write), and the write fails if another server instance has already moved the object. The
    /// caller (see <see cref="PlayerPersistenceService"/>) keeps the player dirty and retries on that
    /// failure rather than ever overwriting blindly — that is the property that makes N game servers
    /// share one source of truth.
    /// </para>
    ///
    /// <para>
    /// All calls must run on the main thread (UnityWebRequest is not thread-safe). The service runs
    /// loads from the join handshake continuation and saves from its <c>Update</c>, both on the main
    /// thread, so this holds by construction.
    /// </para>
    /// </summary>
    public sealed class NakamaPlayerRepository : IPlayerRepository, INakamaConnected
    {
        /// <summary>Storage collection every player record lives under.</summary>
        public const string StorageCollection = "players";

        private const int RequestTimeoutSeconds = 10;

        private readonly BackendSettings _settings;
        private string _baseUrl;
        private string _sessionToken;
        private string _userId;

        public NakamaPlayerRepository(BackendSettings settings)
            => _settings = settings ?? new BackendSettings();

        public bool IsConnected => !string.IsNullOrEmpty(_sessionToken);

        public async Task ConnectAsync(CancellationToken ct)
        {
            _baseUrl = $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";

            var session = await PostAsync<AuthSession>(
                "/v2/account/authenticate/device?create=true&username=" + Uri.EscapeDataString(_settings.ServerUsername),
                JsonUtility.ToJson(new AccountDeviceRequest { id = _settings.ServerDeviceId }),
                useBasic: true,
                ct);

            if (string.IsNullOrEmpty(session.token))
                throw new InvalidOperationException("Nakama returned a session without a token.");

            _sessionToken = session.token;

            AccountResponse account = await GetAsync<AccountResponse>("/v2/account", ct);
            if (account.user == null || string.IsNullOrEmpty(account.user.id))
                throw new InvalidOperationException("Nakama returned a session without a user id.");

            _userId = account.user.id;
        }

        public Task DisconnectAsync()
        {
            _sessionToken = null;
            _userId = null;
            return Task.CompletedTask;
        }

        public async Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct)
        {
            RequireSession();

            var request = new ReadStorageObjectsRequest
            {
                objectIds = new List<ReadStorageObjectId>
                {
                    new ReadStorageObjectId
                    {
                        collection = StorageCollection,
                        key = userId,
                        userId = _userId,
                    },
                },
            };

            StorageObjectsResponse response = await PostAsync<StorageObjectsResponse>(
                "/v2/storage", JsonUtility.ToJson(request), useBasic: false, ct);

            if (response.objects == null || response.objects.Count == 0)
                return null;

            StorageObject obj = response.objects[0];
            PlayerSaveRecord record = JsonUtility.FromJson<PlayerSaveRecord>(obj.value);
            if (record == null)
                return null;

            record.UserId = userId;
            record.StorageVersion = obj.version;
            return record;
        }

        public async Task SaveAsync(PlayerSaveRecord record, CancellationToken ct)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            RequireSession();

            var write = new StorageObjectWrite
            {
                collection = StorageCollection,
                key = record.UserId,
                value = JsonUtility.ToJson(record),
                // Empty version means "no object yet"; "*" tells Nakama to create-if-not-exists,
                // and any other hash performs an if-match against the version we loaded.
                version = string.IsNullOrEmpty(record.StorageVersion) ? "*" : record.StorageVersion,
                permissionRead = 1,   // owner read
                permissionWrite = 1,  // owner write
            };

            var request = new WriteStorageObjectsRequest
            {
                objects = new List<StorageObjectWrite> { write },
            };

            WriteStorageObjectsResponse response = await PutAsync<WriteStorageObjectsResponse>(
                "/v2/storage", JsonUtility.ToJson(request), ct);

            if (response.acks != null && response.acks.Count > 0)
                record.StorageVersion = response.acks[0].version;
        }

        private void RequireSession()
        {
            if (string.IsNullOrEmpty(_sessionToken) || string.IsNullOrEmpty(_userId))
                throw new InvalidOperationException(
                    $"{nameof(NakamaPlayerRepository)} is not connected to the backend.");
        }

        private Task<T> GetAsync<T>(string path, CancellationToken ct)
            => SendAsync<T>("GET", path, null, false, ct);

        private Task<T> PostAsync<T>(string path, string jsonBody, bool useBasic, CancellationToken ct)
            => SendAsync<T>("POST", path, jsonBody, useBasic, ct);

        private Task<T> PutAsync<T>(string path, string jsonBody, CancellationToken ct)
            => SendAsync<T>("PUT", path, jsonBody, false, ct);

        private async Task<T> SendAsync<T>(string method, string path, string jsonBody, bool useBasic, CancellationToken ct)
        {
            string url = _baseUrl + path;

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

            // A timed-out handshake cancels load tokens; surface that so a racing continuation
            // never mistakes a cancelled read for a fresh player.
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                throw new InvalidOperationException(
                    $"Nakama {method} {url} failed ({(int)request.responseCode}): {request.error}" +
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
            public string refreshToken;
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
    }
}