using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Net
{
    /// <summary>
    /// The identity a backend vouches for when it issues a session token. The server that admits a
    /// join trusts only this — never the account id or name a client claims on the wire — so the
    /// account key every save is stored under comes from the token, not the account id field.
    /// </summary>
    public readonly struct TokenIdentity
    {
        public TokenIdentity(string userId, string username = null)
        {
            UserId = userId ?? string.Empty;
            Username = username ?? string.Empty;
        }

        /// <summary>The backend's user id for the token's owner; empty when the token is not genuine.</summary>
        public string UserId { get; }

        /// <summary>The display name the backend stores for this user.</summary>
        public string Username { get; }

        public bool IsValid => !string.IsNullOrWhiteSpace(UserId);

        public override string ToString() => IsValid ? $"{Username} ({UserId})" : "(invalid identity)";
    }

    /// <summary>
    /// Server-side seam that decides whether a join's session token is genuine. <see cref="MMOConnectionGate"/>
    /// consumes it: when a validator is wired, joins are refused unless the token is real and the
    /// account key is derived from the identity the backend returned. Null validator (the default)
    /// keeps the legacy open join path for local development.
    /// </summary>
    public interface ITokenValidator
    {
        /// <summary>
        /// Returns <see cref="TokenIdentity.IsValid"/> false for a token the backend rejects.
        /// May also throw (backend unreachable) — the gate treats any non-valid outcome as a
        /// refusal and fails closed.
        /// </summary>
        Task<TokenIdentity> ValidateAsync(string token, CancellationToken ct = default);
    }
}