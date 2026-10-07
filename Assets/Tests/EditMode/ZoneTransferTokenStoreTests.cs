using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The signed, single-use token store in isolation: the issued string is self-contained (any
    /// process holding the same secret can validate it), tampering or a wrong secret is rejected,
    /// expiry is judged against an injectable clock, and a token spends its one use the first time
    /// it is consumed.
    /// </summary>
    public sealed class ZoneTransferTokenStoreTests
    {
        private const string Secret = "shared-secret";
        private const string Account = "acc-zone-7";

        private DateTime _now;

        private ZoneTransferTokenStore _store;
        private ZoneTransferTokenStore _otherStore;

        private ZoneTransferTokenStore NewStore(string secret = Secret)
            => new ZoneTransferTokenStore(secret, clock: () => _now);

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _store = NewStore();
            _otherStore = NewStore("different-secret");
        }

        private static ZoneTransferToken CreateToken(string account = Account)
            => new ZoneTransferToken { AccountId = account, TargetHost = "10.0.0.2", TargetPort = 7771 };

        [Test]
        public async Task IssueAsync_ReturnsAPortableSignedToken()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);

            Assert.IsFalse(string.IsNullOrEmpty(issued));
            Assert.AreEqual(2, issued.Split('.').Length, "A token is payload + signature.");
        }

        [Test]
        public async Task IssueAsync_FillsTheIdAndDefaultExpiry()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);

            ZoneTransferToken decoded = _store.Decode(issued);

            Assert.IsNotNull(decoded);
            Assert.IsFalse(string.IsNullOrEmpty(decoded.TokenId));
            Assert.AreEqual(Account, decoded.AccountId);
            Assert.IsFalse(decoded.IsExpired(_now));
            Assert.IsTrue(decoded.IsExpired(_now.AddSeconds(31)), "The default TTL is 30 seconds.");
        }

        [Test]
        public async Task ConsumeAsync_OnAnotherProcessWithTheSameSecret_AdmitsTheToken()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);
            // Server B is a different process; all it shares is the secret.
            ZoneTransferTokenStore target = NewStore();

            ZoneTransferToken consumed = await target.ConsumeAsync(issued, CancellationToken.None);

            Assert.IsNotNull(consumed);
            Assert.AreEqual(Account, consumed.AccountId);
            Assert.AreEqual("10.0.0.2", consumed.TargetHost);
            Assert.AreEqual(7771, consumed.TargetPort);
        }

        [Test]
        public async Task ConsumeAsync_WrongSecret_RejectsTheToken()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);

            ZoneTransferToken consumed = await _otherStore.ConsumeAsync(issued, CancellationToken.None);

            Assert.IsNull(consumed, "A token must never validate under a secret other than the issuer's.");
        }

        [Test]
        public async Task ConsumeAsync_TamperedPayload_RejectsTheToken()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);

            // Flip one payload character: it decodes but no longer matches the signature.
            string tampered = (issued[0] == 'A' ? "B" : "A") + issued.Substring(1);

            Assert.IsNull(await _store.ConsumeAsync(tampered, CancellationToken.None));
        }

        [Test]
        public async Task ConsumeAsync_Expired_RejectsTheToken()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);
            _now = _now.AddSeconds(31);

            Assert.IsNull(await _store.ConsumeAsync(issued, CancellationToken.None));
        }

        [Test]
        public async Task ConsumeAsync_BlankString_ReturnsNull()
        {
            Assert.IsNull(await _store.ConsumeAsync(null, CancellationToken.None));
            Assert.IsNull(await _store.ConsumeAsync(string.Empty, CancellationToken.None));
            Assert.IsNull(await _store.ConsumeAsync("payload-only", CancellationToken.None));
        }

        [Test]
        public async Task ConsumeAsync_SingleUse_SecondConsumeIsRejected()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);

            ZoneTransferToken first = await _store.ConsumeAsync(issued, CancellationToken.None);
            ZoneTransferToken second = await _store.ConsumeAsync(issued, CancellationToken.None);

            Assert.IsNotNull(first);
            Assert.IsNull(second, "A transfer token spends its only use on the first admission.");
        }

        [Test]
        public async Task ConsumeAsync_ReplayAfterExpiryIsRejected_EvenIfUnused()
        {
            string issued = await _store.IssueAsync(CreateToken(), CancellationToken.None);
            _now = _now.AddSeconds(31);

            Assert.IsNull(await _store.ConsumeAsync(issued, CancellationToken.None));
        }
    }
}