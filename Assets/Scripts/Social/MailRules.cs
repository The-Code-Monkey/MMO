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

        /// <summary>The sender tried again before the player-mail cooldown elapsed.</summary>
        RateLimited,

        /// <summary>The recipient is blank, is the sender, or the message body/subject is empty.</summary>
        InvalidRecipient,
    }

    /// <summary>
    /// Pure mail decisions: inbox capacity, expiry against a caller-supplied clock, and claimability.
    /// No backend or Unity dependency, so every branch is reachable from an EditMode test.
    /// </summary>
    public static class MailRules
    {
        public const int MaxInbox = 50;
        public const int MaxAttachments = 6;

        /// <summary>Longest player-authored subject, in characters.</summary>
        public const int MaxSubjectLength = 60;

        /// <summary>Longest player-authored body, in characters.</summary>
        public const int MaxBodyLength = 1000;

        /// <summary>Seconds between player-to-player sends from one account.</summary>
        public const double MinSecondsBetweenSends = 5d;

        /// <summary>True when <paramref name="currentCount"/> is under the inbox cap.</summary>
        public static bool CanSend(int currentCount) => currentCount < MaxInbox;

        /// <summary>True when the message has an expiry in the past (or exactly now).</summary>
        public static bool IsExpired(MailMessage message, long nowTicks)
            => message != null && message.ExpiresAtTicks > 0 && nowTicks >= message.ExpiresAtTicks;

        /// <summary>True when the message may still be claimed: not claimed and not expired.</summary>
        public static bool Claimable(MailMessage message, long nowTicks)
            => message != null && message.State != MailState.Claimed && !IsExpired(message, nowTicks);

        /// <summary>
        /// True when the message carries something to claim. Text-only player mail has nothing to
        /// collect, so an automatic claim must leave it unread until the player opens it.
        /// </summary>
        public static bool HasRewards(MailMessage message)
            => message != null
               && (message.Currency > 0
                   || (message.Attachments != null && message.Attachments.Count > 0));

        /// <summary>
        /// The player-to-player gate, pure and clock-injected: a non-empty subject and body, a
        /// different recipient, and no send inside the cooldown. Inbox capacity is checked by the
        /// delivery path once the recipient's inbox is loaded, so the rule here does not need it.
        /// </summary>
        public static MailRule CanSendPlayerMail(
            string fromAccountId,
            string toAccountId,
            string subject,
            string body,
            double nowSeconds,
            double lastSentSeconds)
        {
            if (string.IsNullOrEmpty(fromAccountId)
                || string.IsNullOrEmpty(toAccountId)
                || fromAccountId == toAccountId)
            {
                return MailRule.InvalidRecipient;
            }

            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                return MailRule.InvalidRecipient;

            if (lastSentSeconds > 0d && nowSeconds - lastSentSeconds < MinSecondsBetweenSends)
                return MailRule.RateLimited;

            return MailRule.Ok;
        }

        /// <summary>Trims and caps a player-authored field at <paramref name="maxLength"/>.</summary>
        public static string Clean(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }

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
