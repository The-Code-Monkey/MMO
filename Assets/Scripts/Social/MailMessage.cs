using System;
using System.Collections.Generic;
using MMO.Persistence;

namespace MMO.Social
{
    /// <summary>Where a mail message is in its read/claim lifecycle.</summary>
    public enum MailState
    {
        Unread = 0,
        Read = 1,
        Claimed = 2,
        Expired = 3,
    }

    /// <summary>
    /// One message in a player's inbox. Attachments are the auction's
    /// <see cref="ItemSaveData"/> shape so the claim path shares the save mapper; a currency payload
    /// rides separately as <see cref="Currency"/> (a single-currency grant, like a refund).
    /// </summary>
    [Serializable]
    public sealed class MailMessage
    {
        public string MailId;
        public string FromAccountId;
        public string Subject;
        public string Body;
        public long SentAtTicks;

        /// <summary>Zero means the message never expires.</summary>
        public long ExpiresAtTicks;

        public MailState State;

        /// <summary>Currency granted on claim (0 = none).</summary>
        public long Currency;

        public List<ItemSaveData> Attachments = new List<ItemSaveData>();
    }

    /// <summary>
    /// A player's whole mailbox. Collection "mail_inbox", key = <see cref="AccountId"/>, owned by the
    /// shared service account so a message sent while the player is offline still lands. The exact
    /// owner-scoped CAS pattern every social record uses; <see cref="StorageVersion"/> is the
    /// backend revision hash, not mail data.
    /// </summary>
    [Serializable]
    public sealed class MailInbox
    {
        public const string Collection = "mail_inbox";

        public string AccountId;
        public List<MailMessage> Messages = new List<MailMessage>();

        /// <summary>The backend's revision hash for optimistic concurrency; not inbox data.</summary>
        public string StorageVersion;
    }
}
