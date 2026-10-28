using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Lazily connects the shared REST client to the backend and debounces failed attempts so the
    /// reconcile tick cannot hot-loop a dead endpoint.
    /// </summary>
    internal sealed class SocialBackendConnection
    {
        private readonly Func<NakamaRestClient> _rest;
        private readonly float _retrySeconds;
        private bool _connected;
        private Task<bool> _connectTask;
        private float _nextAttemptTime;

        public SocialBackendConnection(Func<NakamaRestClient> rest, float retrySeconds)
        {
            _rest = rest;
            _retrySeconds = retrySeconds;
        }

        public bool IsConnected => _connected;

        public void SetInitialState(bool connected) => _connected = connected;

        public Task<bool> EnsureConnectedAsync()
        {
            if (_connected)
                return Task.FromResult(true);

            NakamaRestClient rest = _rest();
            if (rest == null)
                return Task.FromResult(false);

            if (_connectTask != null)
                return _connectTask;

            if (Time.realtimeSinceStartup < _nextAttemptTime)
                return Task.FromResult(false);

            _nextAttemptTime = Time.realtimeSinceStartup + _retrySeconds;
            _connectTask = ConnectCoreAsync(rest);
            return _connectTask;
        }

        private async Task<bool> ConnectCoreAsync(NakamaRestClient rest)
        {
            try
            {
                await rest.ConnectAsync(CancellationToken.None);
                _connected = true;
                Debug.Log("[Social] Connected to the backend.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Backend unavailable; social features are read-only until it returns. {e.Message}");
                _connectTask = null;
                return false;
            }
        }
    }
}
