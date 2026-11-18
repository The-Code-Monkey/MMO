using MMO.Social;

namespace MMO.UI
{
    /// <summary>
    /// Text shared by the mailbox rows and detail pane. Pure so the unread count, state labels and
    /// attachment summary can be asserted without building a panel.
    /// </summary>
    public static class MailUiFormat
    {
        /// <summary>Messages still unread; claimed and expired messages do not count.</summary>
        public static int UnreadCount(MailInbox inbox, long nowTicks)
        {
            if (inbox?.Messages == null)
                return 0;

            int count = 0;
            for (int i = 0; i < inbox.Messages.Count; i++)
            {
                MailMessage message = inbox.Messages[i];
                if (message != null
                    && message.State == MailState.Unread
                    && !MailRules.IsExpired(message, nowTicks))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Messages the server would still let this player claim right now.</summary>
        public static int ClaimableCount(MailInbox inbox, long nowTicks)
        {
            if (inbox?.Messages == null)
                return 0;

            int count = 0;
            for (int i = 0; i < inbox.Messages.Count; i++)
            {
                if (MailRules.Claimable(inbox.Messages[i], nowTicks))
                    count++;
            }

            return count;
        }

        /// <summary>The player-facing text for a refusal the server answered with.</summary>
        public static string RuleMessage(MailRule rule)
        {
            return rule switch
            {
                MailRule.InboxFull => "That mailbox is full.",
                MailRule.BagFull => "Your bags are full. Make room before claiming.",
                MailRule.NotClaimable => "That message cannot be claimed.",
                MailRule.RateLimited => "You are sending mail too quickly.",
                MailRule.InvalidRecipient => "Check the recipient, subject and message.",
                MailRule.NotFound => "That message or recipient no longer exists.",
                MailRule.Conflict => "The mailbox changed. Try again.",
                MailRule.BackendUnavailable => "The mail service is unavailable. Try again shortly.",
                _ => string.Empty,
            };
        }

        /// <summary>"Mailbox" or "Mailbox (3)" — the HUD's unread badge.</summary>
        public static string Title(int unreadCount)
            => unreadCount > 0 ? $"Mailbox ({unreadCount})" : "Mailbox";

        public static string StateLabel(MailMessage message)
        {
            if (message == null)
                return string.Empty;

            return message.State switch
            {
                MailState.Unread => "Unread",
                MailState.Read => "Read",
                MailState.Claimed => "Claimed",
                MailState.Expired => "Expired",
                _ => "Unread",
            };
        }

        /// <summary>What claiming this message would grant, or "No attachments".</summary>
        public static string Contents(MailMessage message)
        {
            if (message == null)
                return string.Empty;

            int attachments = message.Attachments?.Count ?? 0;
            if (message.Currency > 0 && attachments > 0)
                return $"{message.Currency:N0} gold and {attachments} attachment(s)";

            if (message.Currency > 0)
                return $"{message.Currency:N0} gold";

            return attachments > 0 ? $"{attachments} attachment(s)" : "No attachments";
        }

        public static string Sender(string accountId)
            => string.IsNullOrEmpty(accountId) ? "System" : accountId;

        /// <summary>Detail header: "From X · 2026-11-15 10:00 · Unread".</summary>
        public static string Header(MailMessage message)
        {
            if (message == null)
                return string.Empty;

            return $"From {Sender(message.FromAccountId)} · {Date(message.SentAtTicks)} · {StateLabel(message)}";
        }

        public static string Date(long ticks)
        {
            if (ticks <= 0)
                return string.Empty;

            var date = new System.DateTime(ticks, System.DateTimeKind.Utc).ToLocalTime();
            return date.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
