namespace MMO.Persistence
{
    /// <summary>
    /// A refreshed Nakama session: the new bearer token and the rotated refresh token that replaces
    /// the one it was issued from. Refresh tokens are single-use, so callers must keep both.
    /// </summary>
    public sealed class SessionTokens
    {
        public SessionTokens(string token, string refreshToken)
        {
            Token = token;
            RefreshToken = refreshToken;
        }

        /// <summary>The new bearer token sent on the join handshake and storage calls.</summary>
        public string Token { get; }

        /// <summary>The replacement refresh token for the next refresh.</summary>
        public string RefreshToken { get; }
    }
}
