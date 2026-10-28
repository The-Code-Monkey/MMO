using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMO.Persistence
{
    internal interface INakamaAuthTransport
    {
        Task<T> PostAsync<T>(string path, string jsonBody, bool useBasic, CancellationToken ct);
        Task<T> GetAsync<T>(string path, CancellationToken ct);
    }

    /// <summary>
    /// Shared service-account session: authenticate once, keep the access/refresh tokens fresh, and
    /// re-issue the session when a refresh token is revoked.
    /// </summary>
    internal sealed class NakamaSession
    {
        /// <summary>Refresh the session when fewer than this many seconds of life remain.</summary>
        public const int RefreshMarginSeconds = 30;

        /// <summary>How long a freshly issued/refreshed token is treated as valid for.</summary>
        public const int AssumedTokenLifetimeSeconds = 45;

        private readonly BackendSettings _settings;
        private readonly INakamaAuthTransport _transport;
        private string _baseUrl;
        private string _sessionToken;
        private string _refreshToken;
        private string _userId;
        private DateTime _tokenValidUntil = DateTime.MinValue;

        public NakamaSession(BackendSettings settings, INakamaAuthTransport transport)
        {
            _settings = settings ?? new BackendSettings();
            _transport = transport;
        }

        public bool IsConnected => !string.IsNullOrEmpty(_sessionToken);
        public string UserId => _userId;
        public string SessionToken => _sessionToken;
        public string BaseUrl => _baseUrl ??= $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";
        public string HttpBaseUrl => BaseUrl;

        public async Task ConnectAsync(CancellationToken ct)
        {
            _baseUrl = $"{_settings.Scheme}://{_settings.Host}:{_settings.Port}";

            RestAuthSession session = await _transport.PostAsync<RestAuthSession>(
                "/v2/account/authenticate/device?create=true&username=" + Uri.EscapeDataString(_settings.ServerUsername),
                JsonUtility.ToJson(new RestAccountDeviceRequest { id = _settings.ServerDeviceId }),
                useBasic: true,
                ct);

            if (session == null || string.IsNullOrEmpty(session.token))
                throw new InvalidOperationException("Nakama returned a session without a token.");

            _sessionToken = session.token;
            _refreshToken = session.refresh_token;
            _tokenValidUntil = DateTime.UtcNow.AddSeconds(AssumedTokenLifetimeSeconds);

            RestAccountResponse account = await _transport.GetAsync<RestAccountResponse>("/v2/account", ct);
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

        public async Task<string> GetFreshTokenAsync(CancellationToken ct)
        {
            RequireSession();

            if (_tokenValidUntil > DateTime.UtcNow.AddSeconds(RefreshMarginSeconds) ||
                string.IsNullOrEmpty(_refreshToken))
                return _sessionToken;

            string tokenBeforeRefresh = _sessionToken;
            try
            {
                RestRefreshResponse refreshed = await _transport.PostAsync<RestRefreshResponse>(
                    "/v2/account/session/refresh",
                    JsonUtility.ToJson(new RestRefreshRequest { token = _refreshToken }),
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                if (_sessionToken != tokenBeforeRefresh && !string.IsNullOrEmpty(_sessionToken))
                    return _sessionToken;

                _sessionToken = null;
                _refreshToken = null;
                _userId = null;
                _tokenValidUntil = DateTime.MinValue;
                await ConnectAsync(ct);
                return _sessionToken;
            }
        }

        public void RequireSession()
        {
            if (string.IsNullOrEmpty(_sessionToken) || string.IsNullOrEmpty(_userId))
                throw new InvalidOperationException("NakamaRestClient is not connected to the backend.");
        }
    }
}
