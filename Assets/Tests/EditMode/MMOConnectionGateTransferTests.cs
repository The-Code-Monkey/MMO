using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The join gate's zone-transfer seam, tested without a server. Once an
    /// <see cref="IZoneTransferTokenStore"/> is wired, a join that carries a transfer token is the
    /// redirected join of a player the source zone just handed off: the account the source saved
    /// decides, the token must be genuine and single-use, and a mismatch or a forgery is a refused
    /// join. Joins without a token keep the legacy account-id verdict exactly as before.
    /// </summary>
    public sealed class MMOConnectionGateTransferTests
    {
        private sealed class RecordingLoader : IPlayerLoader
        {
            public string AccountId;
            private readonly TaskCompletionSource<PlayerSaveRecord> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct)
            {
                AccountId = accountId;
                return _source.Task;
            }

            public void Complete(PlayerSaveRecord record) => _source.SetResult(record);
        }

        private sealed class FakeTokenValidator : ITokenValidator
        {
            public readonly TaskCompletionSource<TokenIdentity> Source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<TokenIdentity> ValidateAsync(string token, CancellationToken ct) => Source.Task;
        }

        private sealed class VerdictCapture
        {
            public bool? Approved;

            public VerdictCapture(MMOConnectionGate gate)
                => gate.OnAuthenticationResult += (_, ok) => Approved = ok;
        }

        private const string Secret = "transfer-test-secret";
        private const string Account = "acc-zone-11";

        private GameObject _go;
        private MMOConnectionGate _gate;
        private ZoneTransferTokenStore _store;
        private RecordingLoader _loader;

        private static NetworkConnection Connection => NetworkManager.EmptyConnection;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GateUnderTest");
            _gate = _go.AddComponent<MMOConnectionGate>();
            _store = new ZoneTransferTokenStore(Secret);
            _loader = new RecordingLoader();
            _gate.PlayerLoader = _loader;
            _gate.TransferTokenStore = _store;
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        private async Task<string> IssueToken(string account = Account, string sourceServerId = null)
            => await _store.IssueAsync(
                new ZoneTransferToken
                {
                    AccountId = account,
                    SourceServerId = sourceServerId ?? string.Empty,
                    TargetHost = "127.0.0.1",
                    TargetPort = 7770
                },
                CancellationToken.None);

        [Test]
        public async Task HandleJoinRequest_WithAValidTransferToken_LoadsUnderTheTokensAccount()
        {
            string token = await IssueToken();
            var capture = new VerdictCapture(_gate);
            string loadedAccount = null;
            _gate.PlayerJoinLoaded += (_, accountId, _) => loadedAccount = accountId;

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", null, null, token);
            _loader.Complete(new PlayerSaveRecord { UserId = Account, Level = 4 });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
            Assert.AreEqual(Account, _loader.AccountId,
                "The destination loads the account the source saved, not one the client claims.");
            Assert.AreEqual(Account, loadedAccount);
            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [Test]
        public async Task HandleJoinRequest_WithAValidTransferToken_RemembersTheSourceServer()
        {
            string token = await IssueToken(sourceServerId: ZoneServerIds.SafeZone);
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", null, null, token);
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
            Assert.IsTrue(_gate.TryGetTransferSource(Connection.ClientId, out string source));
            Assert.AreEqual(ZoneServerIds.SafeZone, source);
        }

        [Test]
        public async Task HandleJoinRequest_MatchingClaimedAccount_IsAdmitted()
        {
            string token = await IssueToken();
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_MismatchedClaimedAccount_IsRefused()
        {
            string token = await IssueToken(Account);
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "someone-else", null, token);

            Assert.AreEqual(false, capture.Approved);
            Assert.IsNull(_loader.AccountId, "No load happens for a refused handoff.");
        }

        [Test]
        public async Task HandleJoinRequest_GarbageTransferToken_IsRefused()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, "not-a-token");

            Assert.AreEqual(false, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_TamperedTransferToken_IsRefused()
        {
            string token = await IssueToken();
            string tampered = (token[0] == 'A' ? "B" : "A") + token.Substring(1);
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, tampered);

            Assert.AreEqual(false, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_ReusedTransferToken_IsRefusedOnTheSecondJoin()
        {
            string token = await IssueToken();
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved, "The first redirected join is the legitimate one.");

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);

            Assert.AreEqual(false, capture.Approved, "A consumed token must not admit a replay.");
        }

        [Test]
        public async Task HandleJoinRequest_ExpiredTransferToken_IsRefused()
        {
            string token = await IssueToken();
            _store = new ZoneTransferTokenStore(Secret, clock: () => DateTime.UtcNow.AddSeconds(120));
            _gate.TransferTokenStore = _store;
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);

            Assert.AreEqual(false, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_NoStore_IgnoresTheTransferTokenAndJoinsAsBefore()
        {
            _gate.TransferTokenStore = null;
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, "any-token");
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved, "A server with no transfer seam must keep deciding by account id.");
            Assert.AreEqual(Account, _loader.AccountId);
        }

        [Test]
        public async Task HandleJoinRequest_WithoutAPendingHandshake_NeverDecides()
        {
            string token = await IssueToken();
            var capture = new VerdictCapture(_gate);

            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);

            Assert.IsNull(capture.Approved, "The gate only decides joins it parked in OnRemoteConnection.");
        }

        [Test]
        public async Task HandleJoinRequest_TransferWithTokenAuth_AdmitsWhenTheVouchedIdentityMatches()
        {
            var validator = new FakeTokenValidator();
            _gate.TokenValidator = validator;
            string token = await IssueToken(Account);

            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", null, "session-token", token);
            validator.Source.SetResult(new TokenIdentity(Account, "Vouched"));
            await Task.Delay(50);
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
            Assert.AreEqual(Account, _loader.AccountId);
        }

        [Test]
        public async Task HandleJoinRequest_TransferWithTokenAuth_RefusesWhenTheVouchedIdentityDiffers()
        {
            var validator = new FakeTokenValidator();
            _gate.TokenValidator = validator;
            string token = await IssueToken(Account);

            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", null, "session-token", token);
            validator.Source.SetResult(new TokenIdentity("someone-else", "Other"));
            await Task.Delay(50);

            Assert.AreEqual(false, capture.Approved,
                "The backend vouch and the saved account must name the same player.");
        }

        [Test]
        public async Task HandleJoinRequest_TransferWithTokenAuth_MissingAuthToken_IsRefused()
        {
            _gate.TokenValidator = new FakeTokenValidator();
            string token = await IssueToken(Account);
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", Account, null, token);

            Assert.AreEqual(false, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_TransferWithTokenAuth_ExpiredSessionFallsBackToTheSignedToken()
        {
            var validator = new FakeTokenValidator();
            _gate.TokenValidator = validator;
            string token = await IssueToken(Account);
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", null, "expired-session-token", token);
            validator.Source.SetResult(new TokenIdentity(null));
            await Task.Delay(50);
            _loader.Complete(new PlayerSaveRecord { UserId = Account });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved,
                "A long play session can outlive the Nakama session token; the signed transfer token still names the account.");
            Assert.AreEqual(Account, _loader.AccountId);
        }
    }
}