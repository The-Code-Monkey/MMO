using System.Collections.Generic;
using MMO.Social;
using MMO.UI;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The player-mail half of the mailbox: the send gate (recipient validity and cooldown), the
    /// string caps, and the text a row/detail pane computes from an inbox snapshot.
    /// </summary>
    public sealed class MailPlayerRulesTests
    {
        private const long NowTicks = 10_000_000_000L;

        [Test]
        public void CanSendPlayerMail_AllowsAValidDifferentRecipient()
        {
            Assert.AreEqual(MailRule.Ok,
                MailRules.CanSendPlayerMail("me", "them", "Hi", "Body", 100d, 0d));
        }

        [Test]
        public void CanSendPlayerMail_RejectsBlankOrSelf()
        {
            Assert.AreEqual(MailRule.InvalidRecipient,
                MailRules.CanSendPlayerMail("me", "me", "Hi", "Body", 100d, 0d));
            Assert.AreEqual(MailRule.InvalidRecipient,
                MailRules.CanSendPlayerMail("me", null, "Hi", "Body", 100d, 0d));
            Assert.AreEqual(MailRule.InvalidRecipient,
                MailRules.CanSendPlayerMail("me", "them", "   ", "Body", 100d, 0d));
            Assert.AreEqual(MailRule.InvalidRecipient,
                MailRules.CanSendPlayerMail("me", "them", "Hi", string.Empty, 100d, 0d));
        }

        [Test]
        public void CanSendPlayerMail_ThrottlesInsideTheCooldown()
        {
            Assert.AreEqual(MailRule.RateLimited,
                MailRules.CanSendPlayerMail("me", "them", "Hi", "Body", 10d, 8d),
                "Two seconds after the last send is still inside the five-second cooldown.");

            Assert.AreEqual(MailRule.Ok,
                MailRules.CanSendPlayerMail("me", "them", "Hi", "Body", 20d, 8d));
        }

        [Test]
        public void Clean_TrimsAndCaps()
        {
            Assert.AreEqual("hello", MailRules.Clean("  hello  ", 20));
            Assert.AreEqual(string.Empty, MailRules.Clean(null, 20));
            Assert.AreEqual(5, MailRules.Clean("abcdefgh", 5).Length);
        }

        [Test]
        public void UnreadCount_CountsOnlyUnreadMessages()
        {
            MailInbox inbox = Inbox(
                NewMail(MailState.Unread),
                NewMail(MailState.Read),
                NewMail(MailState.Unread),
                NewMail(MailState.Claimed));

            Assert.AreEqual(2, MailUiFormat.UnreadCount(inbox));
            Assert.AreEqual(0, MailUiFormat.UnreadCount(null));
        }

        [Test]
        public void Title_ShowsTheUnreadBadge()
        {
            Assert.AreEqual("Mailbox", MailUiFormat.Title(0));
            Assert.AreEqual("Mailbox (3)", MailUiFormat.Title(3));
        }

        [Test]
        public void ClaimableCount_HonoursStateAndExpiry()
        {
            MailInbox inbox = Inbox(
                NewMail(MailState.Unread),
                NewMail(MailState.Read),
                NewMail(MailState.Claimed),
                NewMail(MailState.Unread, expiresAtTicks: NowTicks - 1));

            Assert.AreEqual(2, MailUiFormat.ClaimableCount(inbox, NowTicks));
        }

        [Test]
        public void Contents_DescribesGoldAndAttachments()
        {
            Assert.AreEqual("No attachments", MailUiFormat.Contents(NewMail(MailState.Unread)));

            MailMessage gold = NewMail(MailState.Unread);
            gold.Currency = 150;
            Assert.AreEqual("150 gold", MailUiFormat.Contents(gold));

            MailMessage both = NewMail(MailState.Unread);
            both.Currency = 50;
            both.Attachments.Add(new MMO.Persistence.ItemSaveData { ItemName = "Iron Ore" });
            Assert.AreEqual("50 gold and 1 attachment(s)", MailUiFormat.Contents(both));
        }

        [Test]
        public void Header_NamesTheSenderStateAndDate()
        {
            MailMessage message = NewMail(MailState.Unread);
            message.FromAccountId = "seller-1";
            message.SentAtTicks = new System.DateTime(2026, 11, 15, 10, 0, 0, System.DateTimeKind.Utc).Ticks;

            string header = MailUiFormat.Header(message);

            StringAssert.Contains("seller-1", header);
            StringAssert.Contains("Unread", header);
        }

        private static MailInbox Inbox(params MailMessage[] messages)
            => new MailInbox { AccountId = "me", Messages = new List<MailMessage>(messages) };

        private static MailMessage NewMail(MailState state, long expiresAtTicks = 0)
        {
            return new MailMessage
            {
                MailId = System.Guid.NewGuid().ToString("N"),
                FromAccountId = "sender",
                Subject = "Subject",
                Body = "Body",
                SentAtTicks = NowTicks,
                ExpiresAtTicks = expiresAtTicks,
                State = state,
                Attachments = new List<MMO.Persistence.ItemSaveData>(),
            };
        }
    }
}
