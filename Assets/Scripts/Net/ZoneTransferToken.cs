using System;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// A short-lived, single-use authorization to (re)join a specific zone server as a specific
    /// account. Issued by the source server only after it has flushed the player's save; consumed
    /// by the target server's <see cref="MMOConnectionGate"/> during the redirected join.
    ///
    /// <para>
    /// The token is serialized to JSON and signed with a shared secret
    /// (<see cref="ZoneTransferTokenStore"/>) so a target process can validate it without a shared
    /// in-memory table: every zone server in a deployment holds the same secret, just as they share
    /// one Nakama service account. The fields are public properties over private serialized fields
    /// so <c>JsonUtility</c> can round-trip them.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ZoneTransferToken
    {
        [SerializeField] private string _tokenId = string.Empty;
        [SerializeField] private string _accountId = string.Empty;
        [SerializeField] private string _sourceServerId = string.Empty;
        [SerializeField] private string _targetServerId = string.Empty;
        [SerializeField] private string _targetHost = string.Empty;
        [SerializeField] private ushort _targetPort;
        [SerializeField] private long _issuedAtUnixSeconds;
        [SerializeField] private long _expiresAtUnixSeconds;

        /// <summary>Unique id, also the single-use key the consuming server revokes.</summary>
        public string TokenId
        {
            get => _tokenId;
            set => _tokenId = value ?? string.Empty;
        }

        /// <summary>The account the source server saved and the target must load.</summary>
        public string AccountId
        {
            get => _accountId;
            set => _accountId = value ?? string.Empty;
        }

        /// <summary>Id of the server that issued the token.</summary>
        public string SourceServerId
        {
            get => _sourceServerId;
            set => _sourceServerId = value ?? string.Empty;
        }

        /// <summary>Id of the server allowed to admit the token.</summary>
        public string TargetServerId
        {
            get => _targetServerId;
            set => _targetServerId = value ?? string.Empty;
        }

        /// <summary>Address the token is bound to.</summary>
        public string TargetHost
        {
            get => _targetHost;
            set => _targetHost = value ?? string.Empty;
        }

        /// <summary>Port the token is bound to.</summary>
        public ushort TargetPort
        {
            get => _targetPort;
            set => _targetPort = value;
        }

        /// <summary>When the token was minted, in UTC.</summary>
        public DateTime IssuedAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(_issuedAtUnixSeconds).UtcDateTime;
            set => _issuedAtUnixSeconds = new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();
        }

        /// <summary>When the token stops being accepted, in UTC.</summary>
        public DateTime ExpiresAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(_expiresAtUnixSeconds).UtcDateTime;
            set => _expiresAtUnixSeconds = new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();
        }

        /// <summary>Raw issue time, the value <c>JsonUtility</c> actually round-trips.</summary>
        public long IssuedAtUnixSeconds
        {
            get => _issuedAtUnixSeconds;
            set => _issuedAtUnixSeconds = value;
        }

        /// <summary>Raw expiry, the value <c>JsonUtility</c> actually round-trips.</summary>
        public long ExpiresAtUnixSeconds
        {
            get => _expiresAtUnixSeconds;
            set => _expiresAtUnixSeconds = value;
        }

        /// <summary>True once <paramref name="utcNow"/> has reached the expiry instant.</summary>
        public bool IsExpired(DateTime utcNow)
            => _expiresAtUnixSeconds <= new DateTimeOffset(utcNow.ToUniversalTime()).ToUnixTimeSeconds();

        /// <summary>True when the token names <paramref name="accountId"/>.</summary>
        public bool MatchesAccount(string accountId)
            => !string.IsNullOrEmpty(accountId)
               && string.Equals(_accountId, accountId, StringComparison.Ordinal);

        /// <summary>
        /// True when the token carries everything a verdict needs: an id, an account and a real
        /// window. A malformed payload must never be treated as an authorization.
        /// </summary>
        public bool IsWellFormed()
            => !string.IsNullOrEmpty(_tokenId)
               && !string.IsNullOrEmpty(_accountId)
               && _expiresAtUnixSeconds > _issuedAtUnixSeconds;
    }
}
