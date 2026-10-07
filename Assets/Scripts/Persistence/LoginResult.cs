namespace MMO.Persistence
{
    /// <summary>
    /// The outcome of a sign-in attempt. Either the backend vouched for the player
    /// (<see cref="Success"/> with the session token and the identity the token binds, plus the
    /// player's own last-zone record or null when none is stored yet) or it refused, in which case
    /// <see cref="Error"/> is a message produced for the player to read.
    /// </summary>
    public sealed class LoginResult
    {
        public bool Success { get; }

        /// <summary>The bearer token the backend issued; proves the login on the join handshake.</summary>
        public string Token { get; }

        /// <summary>The Nakama user id the token belongs to; the server's account key from here on.</summary>
        public string UserId { get; }

        /// <summary>The username the backend displays for this token.</summary>
        public string Username { get; }

        /// <summary>The stored "last standing in" zone, or null when none has been recorded yet.</summary>
        public LastZoneInfo LastZone { get; }

        /// <summary>Player-facing reason for a refused login; null on success.</summary>
        public string Error { get; }

        private LoginResult(
            bool success, string token, string userId, string username, LastZoneInfo lastZone, string error)
        {
            Success = success;
            Token = token;
            UserId = userId;
            Username = username;
            LastZone = lastZone;
            Error = error;
        }

        public static LoginResult Ok(string token, string userId, string username, LastZoneInfo lastZone = null)
            => new(true, token, userId, username, lastZone, null);

        public static LoginResult Fail(string error)
            => new(false, null, null, null, null, error ?? "Sign-in failed.");
    }
}