using System;
using MMO.Social;
using NUnit.Framework;

namespace MMO.Tests.EditMode
{
    public sealed class MailRulesTests
    {
        private static MailMessage NewMail(long expiresAtTicks = 0, MailState state = MailState.Unread)
            => new MailMessage { MailId = "m1", ExpiresAtTicks = expiresAtTicks, State = state };

        [Test]
        public void CanSend_RejectsFullInbox()
        {
            Assert.IsTrue(MailRules.CanSend(MailRules.MaxInbox - 1));
            Assert.IsFalse(MailRules.CanSend(MailRules.MaxInbox));
            Assert.IsFalse(MailRules.CanSend(MailRules.MaxInbox + 1));
        }

        [Test]
        public void IsExpired_UsesServerClock()
        {
            long now = 1000;
            Assert.IsFalse(MailRules.IsExpired(NewMail(expiresAtTicks: 1001), now));
            Assert.IsTrue(MailRules.IsExpired(NewMail(expiresAtTicks: 1000), now));
            Assert.IsTrue(MailRules.IsExpired(NewMail(expiresAtTicks: 999), now));
            Assert.IsFalse(MailRules.IsExpired(NewMail(expiresAtTicks: 0), now)); // never expires
        }

        [Test]
        public void Claimable_FalseAfterClaimed()
            => Assert.IsFalse(MailRules.Claimable(NewMail(state: MailState.Claimed), DateTime.MaxValue.Ticks));

        [Test]
        public void Claimable_FalseForExpired()
            => Assert.IsFalse(MailRules.Claimable(NewMail(expiresAtTicks: 1000), 1000));

        [Test]
        public void Claimable_TrueForUnreadWithinExpiry()
            => Assert.IsTrue(MailRules.Claimable(NewMail(expiresAtTicks: 1000), 999));

        [Test]
        public void NormalizeState_UnknownValueBecomesUnread()
        {
            Assert.AreEqual(MailState.Unread, MailRules.NormalizeState(99));
            Assert.AreEqual(MailState.Claimed, MailRules.NormalizeState((int)MailState.Claimed));
        }
    }
}
