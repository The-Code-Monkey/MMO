using FishNet.Broadcast;

namespace MMO.Net
{
    public struct JoinRequestBroadcast : IBroadcast
    {
        public string PlayerName;

        /// <summary>
        /// The stable account key (a device GUID the client generates once and persists). The
        /// server loads and saves this player's record under it on the legacy open-join path.
        /// Token authentication ignores it entirely — see <see cref="AuthToken"/>.
        /// </summary>
        public string AccountId;

        /// <summary>
        /// The session token the client's sign-in received from the backend. Present only when
        /// token authentication is enabled; the server gate validates it and derives the player's
        /// account key from the identity the backend vouches for, never from <see cref="AccountId"/>.
        /// </summary>
        public string AuthToken;

        /// <summary>
        /// Short-lived, single-use zone-transfer token minted by the previous zone server. Present
        /// only on a redirected join; the gate consumes it so the same account lands on the
        /// destination with the save the source just flushed.
        /// </summary>
        public string TransferToken;
    }

    public struct JoinResultBroadcast : IBroadcast
    {
        public bool Approved;
        public string Reason;
    }
}
