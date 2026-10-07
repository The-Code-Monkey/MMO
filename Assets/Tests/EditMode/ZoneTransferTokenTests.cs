using System;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The transfer-token shape: UTC round-trip of issue/expiry, the account binding the gate
    /// checks, and the well-formedness gate a malformed payload never passes.
    /// </summary>
    public sealed class ZoneTransferTokenTests
    {
        private const string Account = "acc-zone-1";

        private static ZoneTransferToken Token()
            => new ZoneTransferToken
            {
                TokenId = "tok-1",
                AccountId = Account,
                IssuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc),
            };

        [Test]
        public void IssuedAt_And_ExpiresAt_RoundTripUtc()
        {
            ZoneTransferToken token = Token();

            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), token.IssuedAt);
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc), token.ExpiresAt);
            Assert.AreEqual(1767225600L, token.IssuedAtUnixSeconds, "2026-01-01T00:00:00Z must round-trip exactly.");
        }

        [Test]
        public void IsExpired_BeforeExpiryIsFalse_AfterIsTrue()
        {
            ZoneTransferToken token = Token();
            DateTime before = new DateTime(2026, 1, 1, 0, 0, 29, DateTimeKind.Utc);
            DateTime atExpiry = new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc);
            DateTime after = new DateTime(2026, 1, 1, 0, 0, 31, DateTimeKind.Utc);

            Assert.IsFalse(token.IsExpired(before));
            Assert.IsTrue(token.IsExpired(atExpiry), "The expiry instant itself is too late.");
            Assert.IsTrue(token.IsExpired(after));
        }

        [Test]
        public void MatchesAccount_IsCaseSensitiveAndRejectsBlank()
        {
            ZoneTransferToken token = Token();

            Assert.IsTrue(token.MatchesAccount(Account));
            Assert.IsFalse(token.MatchesAccount(Account.ToUpperInvariant()));
            Assert.IsFalse(token.MatchesAccount(null));
            Assert.IsFalse(token.MatchesAccount(string.Empty));
        }

        [Test]
        public void IsWellFormed_RequiresIdAccountAndARealWindow()
        {
            ZoneTransferToken token = Token();

            Assert.IsTrue(token.IsWellFormed());

            token.TokenId = string.Empty;
            Assert.IsFalse(token.IsWellFormed());

            token.TokenId = "tok-1";
            token.AccountId = string.Empty;
            Assert.IsFalse(token.IsWellFormed());

            token.AccountId = Account;
            token.ExpiresAt = token.IssuedAt;
            Assert.IsFalse(token.IsWellFormed(), "A token that expires the moment it is issued is not valid.");
        }
    }
}