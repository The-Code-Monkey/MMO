using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MMO.Net
{
    /// <summary>
    /// HTTP implementation of <see cref="IOrchestratorClient"/> that talks to the Node.js orchestrator
    /// control plane. Mirrors <see cref="NakamaRestClient"/>'s UnityWebRequest patterns so all calls
    /// run on the main thread and share the same timeout/error handling conventions.
    /// </summary>
    public sealed class HttpOrchestratorClient : IOrchestratorClient
    {
        public const int RequestTimeoutSeconds = 10;

        private readonly string _baseUrl;
        private readonly bool _enabled;

        public bool Enabled => _enabled;

        /// <summary>
        /// Creates a client pointing at the orchestrator service.
        /// </summary>
        /// <param name="baseUrl">Scheme://host:port of the orchestrator (e.g., "http://127.0.0.1:8080").</param>
        /// <param name="enabled">Whether orchestration is enabled. When false, all calls return failed results.</param>
        public HttpOrchestratorClient(string baseUrl, bool enabled = true)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _enabled = enabled;
        }

        public async Task<OrchestrationHandle> StartAsync(string raidId, string zoneTemplateId, string tokenSecret, CancellationToken ct)
        {
            if (!_enabled)
                throw new InvalidOperationException("Orchestrator is disabled.");

            if (string.IsNullOrEmpty(raidId))
                throw new ArgumentException("raidId is required.", nameof(raidId));
            if (string.IsNullOrEmpty(zoneTemplateId))
                throw new ArgumentException("zoneTemplateId is required.", nameof(zoneTemplateId));
            if (string.IsNullOrEmpty(tokenSecret))
                throw new ArgumentException("tokenSecret is required.", nameof(tokenSecret));

            var requestBody = new StartRequest
            {
                raidId = raidId,
                buildRef = null,
                zoneTemplateId = zoneTemplateId,
                tokenSecret = tokenSecret,
            };

            string json = JsonUtility.ToJson(requestBody);
            StartResponse response = await PostAsync<StartResponse>("/instances", json, ct);

            if (response == null || string.IsNullOrEmpty(response.instanceId))
                throw new InvalidOperationException("Orchestrator did not return an instanceId.");

            return new OrchestrationHandle(response.instanceId, raidId, zoneTemplateId, tokenSecret);
        }

        public async Task<OrchestrationState> WaitHealthyAsync(
            OrchestrationHandle handle,
            Func<OrchestrationState, bool, bool, double, double, OrchestrationState> tick,
            double timeoutSeconds,
            CancellationToken ct)
        {
            if (!_enabled)
                return OrchestrationState.Failed;

            if (!handle.IsValid)
                throw new ArgumentException("Invalid orchestration handle.", nameof(handle));

            var startTime = DateTime.UtcNow;
            OrchestrationState state = OrchestrationState.Requested;

            while (!ct.IsCancellationRequested)
            {
                double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

                StatusResponse status = await GetAsync<StatusResponse>($"/instances/{handle.InstanceId}", ct);

                bool containerHealthy = status?.state == "Healthy" || status?.state == "Ready";
                bool failed = status?.state == "Failed" || status?.state == "TimedOut";

                state = tick(state, containerHealthy, failed, elapsed, timeoutSeconds);

                if (state == OrchestrationState.Ready || state == OrchestrationState.Failed || state == OrchestrationState.TimedOut)
                    return state;

                await Task.Delay(1000, ct);
            }

            return OrchestrationState.Failed;
        }

        public async Task StopAsync(OrchestrationHandle handle, CancellationToken ct)
        {
            if (!_enabled || !handle.IsValid)
                return;

            try
            {
                await DeleteAsync($"/instances/{handle.InstanceId}", ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HttpOrchestratorClient] StopAsync failed for {handle.InstanceId}: {e.Message}");
            }
        }

        public async Task<(string Host, ushort Port)?> GetEndpointAsync(OrchestrationHandle handle, CancellationToken ct)
        {
            if (!_enabled || !handle.IsValid)
                return null;

            StatusResponse status = await GetAsync<StatusResponse>($"/instances/{handle.InstanceId}", ct);

            if (status == null || (status.state != "Healthy" && status.state != "Ready"))
                return null;

            if (!status.port.HasValue)
                return null;

            return (status.host ?? "host.docker.internal", (ushort)status.port.Value);
        }

        private async Task<T> GetAsync<T>(string path, CancellationToken ct)
            => await SendAsync<T>("GET", path, null, ct);

        private async Task<T> PostAsync<T>(string path, string jsonBody, CancellationToken ct)
            => await SendAsync<T>("POST", path, jsonBody, ct);

        private async Task DeleteAsync(string path, CancellationToken ct)
            => await SendAsync<object>("DELETE", path, null, ct);

        private async Task<T> SendAsync<T>(string method, string path, string jsonBody, CancellationToken ct)
        {
            string url = _baseUrl + path;

            using var request = new UnityWebRequest(url, method);
            if (jsonBody != null)
            {
                byte[] body = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            await SendAndWaitAsync(request);

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            if (request.result != UnityWebRequest.Result.Success)
            {
                int status = (int)request.responseCode;
                string body = request.downloadHandler?.text;

                if (status == 404)
                    return default;

                throw new InvalidOperationException(
                    $"Orchestrator {method} {url} failed ({status}): {request.error}" +
                    (string.IsNullOrEmpty(body) ? string.Empty : " - " + body));
            }

            string text = request.downloadHandler?.text;
            if (string.IsNullOrEmpty(text))
                return default;

            return JsonUtility.FromJson<T>(text);
        }

        private static async Task SendAndWaitAsync(UnityWebRequest request)
        {
            AsyncOperation operation = request.SendWebRequest();
            var tcs = new TaskCompletionSource<bool>();
            operation.completed += _ => tcs.TrySetResult(true);
            await tcs.Task;
        }

        [Serializable]
        private class StartRequest
        {
            public string raidId;
            public string buildRef;
            public string zoneTemplateId;
            public string tokenSecret;
        }

        [Serializable]
        private class StartResponse
        {
            public string instanceId;
        }

        [Serializable]
        private class StatusResponse
        {
            public string instanceId;
            public string state;
            public string raidId;
            public string zoneTemplateId;
            public long startedAt;
            public string host;
            public int? port;
            public string error;
        }
    }
}