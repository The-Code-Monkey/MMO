namespace MMO.Social
{
    /// <summary>The outcome of a mail mutation. <see cref="Ok"/> is the only success.</summary>
    public enum MailRule
    {
        Ok,
        BackendUnavailable,
        NotFound,
        InboxFull,
        NotClaimable,
        BagFull,
        Conflict,
    }

    /// <summary>
    /// Pure mail decisions: inbox capacity, expiry against a caller-supplied clock, and claimability.
    /// No backend or Unity dependency, so every branch is reachable from an EditMode test.
    /// </summary>
    public static class MailRules
    {
        public const int MaxInbox = 50;
        public const int MaxAttachments = 6;

        /// <summary>True when <paramref name="currentCount"/> is under the inbox cap.</summary>
        public static bool CanSend(int currentCount) => currentCount < MaxInbox;

        /// <summary>True when the message has an expiry in the past (or exactly now).</summary>
        public static bool IsExpired(MailMessage message, long nowTicks)
            => message != null && message.ExpiresAtTicks > 0 && nowTicks >= message.ExpiresAtTicks;

        /// <summary>True when the message may still be claimed: not claimed and not expired.</summary>
        public static bool Claimable(MailMessage message, long nowTicks)
            => message != null && message.State != MailState.Claimed && !IsExpired(message, nowTicks);

        /// <summary>Normalises an unknown enum value (a newer client's state) to <see cref="MailState.Unread"/>.</summary>
        public static MailState NormalizeState(int state)
        {
            switch (state)
            {
                case (int)MailState.Unread:
                case (int)MailState.Read:
                case (int)MailState.Claimed:
                case (int)MailState.Expired:
                    return (MailState)state;
                default:
                    return MailState.Unread;
            }
        }
    }
}
