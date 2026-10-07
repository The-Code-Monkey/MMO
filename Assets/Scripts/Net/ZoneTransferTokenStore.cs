using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// The in-memory, secret-signed <see cref="IZoneTransferTokenStore"/>. Tokens are self-contained
    /// — <c>base64url(json).base64url(hmac)</c> — so any server that shares the secret can validate
    /// one without seeing the issuer's memory: that is what lets Server A hand a client to a
    /// *different process* (Server B) rather than only another object in the same process. Expiry is
    /// checked against an injectable clock, and every consumed id is remembered until its own expiry
    /// so a token is single-use. The used-id table only ever grows to the number of transfers within
    /// one TTL window, which is why it is safe to keep in memory.
    /// </summary>
    public sealed class ZoneTransferTokenStore : IZoneTransferTokenStore
    {
        /// <summary>How long an issued token is accepted, when the caller does not say.</summary>
        public const float DefaultTtlSeconds = 30f;

        private const char Separator = '.';
        private const int MaximumConsumedIds = 1024;

        private readonly byte[] _secret;
        private readonly double _defaultTtlSeconds;
        private readonly Func<DateTime> _clock;
        private readonly object _gate = new();
        private readonly Dictionary<string, long> _consumedIds = new();

        /// <param name="secretKey">Shared by every zone server in the deployment; never blank.</param>
        /// <param name="defaultTtlSeconds">Fallback lifetime for tokens that carry no expiry.</param>
        /// <param name="clock">
        /// UTC clock, injectable so expiry can be tested without waiting. Defaults to
        /// <see cref="DateTime.UtcNow"/>.
        /// </param>
        public ZoneTransferTokenStore(string secretKey, float defaultTtlSeconds = DefaultTtlSeconds, Func<DateTime> clock = null)
        {
            if (string.IsNullOrEmpty(secretKey))
                throw new ArgumentException("A transfer signing secret is required.", nameof(secretKey));

            _secret = Encoding.UTF8.GetBytes(secretKey);
            _defaultTtlSeconds = defaultTtlSeconds > 0f ? defaultTtlSeconds : DefaultTtlSeconds;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <inheritdoc/>
        public Task<string> IssueAsync(ZoneTransferToken token, CancellationToken ct)
        {
            if (token == null)
                throw new ArgumentNullException(nameof(token));

            FillDefaults(token);

            string payload = JsonUtility.ToJson(token);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
            string signature = Base64Url(Sign(payloadBytes));

            return Task.FromResult(Base64Url(payloadBytes) + Separator + signature);
        }

        /// <inheritdoc/>
        public Task<ZoneTransferToken> ConsumeAsync(string tokenString, CancellationToken ct)
        {
            ZoneTransferToken token = Decode(tokenString);
            if (token == null)
                return Task.FromResult<ZoneTransferToken>(null);

            lock (_gate)
            {
                PruneExpiredIdsLocked();

                if (_consumedIds.ContainsKey(token.TokenId))
                    return Task.FromResult<ZoneTransferToken>(null);

                _consumedIds[token.TokenId] = token.ExpiresAtUnixSeconds;
            }

            return Task.FromResult(token);
        }

        /// <summary>
        /// Verifies the signature and decodes the token without consuming it. Public so a caller can
        /// inspect a token (say, for logging) without spending its single use.
        /// </summary>
        /// <returns>The token, or null when the string is malformed, tampered with or expired.</returns>
        public ZoneTransferToken Decode(string tokenString)
        {
            if (string.IsNullOrEmpty(tokenString))
                return null;

            int split = tokenString.IndexOf(Separator);
            if (split <= 0 || split >= tokenString.Length - 1)
                return null;

            string payloadPart = tokenString.Substring(0, split);
            string signaturePart = tokenString.Substring(split + 1);

            byte[] payloadBytes;
            byte[] signature;
            try
            {
                payloadBytes = FromBase64Url(payloadPart);
                signature = FromBase64Url(signaturePart);
            }
            catch (FormatException)
            {
                return null;
            }

            if (!FixedTimeEquals(signature, Sign(payloadBytes)))
                return null;

            ZoneTransferToken token;
            try
            {
                token = JsonUtility.FromJson<ZoneTransferToken>(Encoding.UTF8.GetString(payloadBytes));
            }
            catch (Exception)
            {
                return null;
            }

            if (token == null || !token.IsWellFormed())
                return null;

            if (token.IsExpired(_clock()))
                return null;

            return token;
        }

        private void FillDefaults(ZoneTransferToken token)
        {
            DateTime now = _clock();

            if (string.IsNullOrEmpty(token.TokenId))
                token.TokenId = Guid.NewGuid().ToString("N");

            if (token.IssuedAtUnixSeconds <= 0)
                token.IssuedAt = now;

            if (token.ExpiresAtUnixSeconds <= token.IssuedAtUnixSeconds)
                token.ExpiresAt = token.IssuedAt.AddSeconds(_defaultTtlSeconds);
        }

        private byte[] Sign(byte[] payloadBytes)
        {
            using var hmac = new HMACSHA256(_secret);

            return hmac.ComputeHash(payloadBytes);
        }

        private void PruneExpiredIdsLocked()
        {
            if (_consumedIds.Count < MaximumConsumedIds)
                return;

            long now = new DateTimeOffset(_clock().ToUniversalTime()).ToUnixTimeSeconds();
            var expired = new List<string>();
            foreach (KeyValuePair<string, long> entry in _consumedIds)
            {
                if (entry.Value <= now)
                    expired.Add(entry.Key);
            }

            foreach (string id in expired)
                _consumedIds.Remove(id);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
                difference |= a[i] ^ b[i];

            return difference == 0;
        }

        private static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }
    }
}
