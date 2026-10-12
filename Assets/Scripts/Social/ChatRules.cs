namespace MMO.Social
{
    /// <summary>The outcome of a chat send. <see cref="Ok"/> is the only success.</summary>
    public enum ChatRule
    {
        Ok,
        Empty,
        TooLong,
        RateLimited,
        NoParty,
        BackendUnavailable,
    }

    /// <summary>
    /// Every chat decision as a pure function — isolated EditMode tests per AGENTS.md. Nothing here
    /// touches the network, storage or Unity.
    /// </summary>
    public static class ChatRules
    {
        /// <summary>Client-facing channel ids, shared with <see cref="PlayerChat"/>.</summary>
        public const byte ChannelGlobal = 0;
        public const byte ChannelZone = 1;
        public const byte ChannelParty = 2;

        /// <summary>Server-side cap on a single message.</summary>
        public const int MaxBodyLength = 200;

        /// <summary>The token-bucket refill window: one message per account per window.</summary>
        public const double MinSecondsBetweenMessages = 1.5;

        /// <summary>
        /// Whether <paramref name="body"/> may be sent now. <paramref name="nextAllowedAt"/> is the
        /// account's rate-limit deadline (0 for a first message); <paramref name="senderHasParty"/>
        /// gates the party channel.
        /// </summary>
        public static ChatRule CanSend(
            string body, double now, double nextAllowedAt, bool senderHasParty, byte channel)
        {
            if (string.IsNullOrWhiteSpace(body))
                return ChatRule.Empty;

            if (body.Length > MaxBodyLength)
                return ChatRule.TooLong;

            if (now < nextAllowedAt)
                return ChatRule.RateLimited;

            if (channel == ChannelParty && !senderHasParty)
                return ChatRule.NoParty;

            return ChatRule.Ok;
        }

        /// <summary>Receiving zone side: does a local player with this party get this envelope?</summary>
        public static bool Receives(SocialBusEnvelope env, string localZoneId, string localPartyId)
        {
            if (env == null)
                return false;

            switch (env.ch)
            {
                case "global":
                    return true;

                case "zone":
                    return env.target == localZoneId;

                case "party":
                    return !string.IsNullOrEmpty(localPartyId) && env.target == localPartyId;

                default:
                    return false;
            }
        }

        /// <summary>The wire channel label for a client channel id.</summary>
        public static string ChannelName(byte channel)
            => channel == ChannelGlobal ? "global"
             : channel == ChannelZone ? "zone"
             : channel == ChannelParty ? "party"
             : null;
    }
}
