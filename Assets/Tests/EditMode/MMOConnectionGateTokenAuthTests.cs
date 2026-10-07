using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The join gate's token seam, tested without a server. Once an <see cref="ITokenValidator"/> is
    /// wired, a join must be refused unless the backend vouches for the session token — and when it
    /// does, the account key and the display name come from that vouch, never from what the client
    /// wrote on the wire. A hand-set <c>TaskCompletionSource</c> stub drives the async verdicts on
    /// demand, the same shape as the loader tests.
    /// </summary>
    public sealed class MMOConnectionGateTokenAuthTests
    {
        private sealed class FakeTokenValidator : ITokenValidator
        {
            public string Token;
            private readonly TaskCompletionSource<TokenIdentity> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<TokenIdentity> ValidateAsync(string token, CancellationToken ct)
            {
                Token = token;
                return _source.Task;
            }

            public void Complete(TokenIdentity identity) => _source.SetResult(identity);
            public void CompleteAsException(Exception e) => _source.SetException(e);
        }

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

        private sealed class VerdictCapture
        {
            public bool? Approved;
            public int Count;

            public VerdictCapture(MMOConnectionGate gate)
                => gate.OnAuthenticationResult += (_, ok) => { Approved = ok; Count++; };
        }

        private const string ValidToken = "tok-valid";
        private const string BackedUserId = "backed-user-9";

        private GameObject _go;
        private MMOConnectionGate _gate;
        private FakeTokenValidator _validator;

        private static NetworkConnection Connection => NetworkManager.EmptyConnection;

        private static TokenIdentity IdentityFor(string userId, string username)
            => new TokenIdentity(userId, username);

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GateUnderTest");
            _gate = _go.AddComponent<MMOConnectionGate>();
            _validator = new FakeTokenValidator();
            _gate.TokenValidator = _validator;
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f));
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [Test]
        public void HandleJoinRequest_WithTokenAuth_RejectsAMissingToken()
        {
            var capture = new VerdictCapture(_gate);

            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", null);

            Assert.AreEqual(false, capture.Approved);
            Assert.AreEqual(1, capture.Count);
        }

        [Test]
        public void HandleJoinRequest_WithTokenAuth_RejectsABlankToken()
        {
            var capture = new VerdictCapture(_gate);

            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", "   ");

            Assert.AreEqual(false, capture.Approved);
            Assert.IsNull(_validator.Token, "A token with no content is never sent to the backend.");
        }

        [Test]
        public void HandleJoinRequest_WithTokenAuth_ParksUntilTheTokenValidates()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);

            Assert.AreEqual(1, _gate.PendingHandshakes, "The handshake stays parked while the token is checked.");
            Assert.IsNull(capture.Approved, "No verdict is possible before the backend has vouched.");
            Assert.AreEqual(ValidToken, _validator.Token, "The backend is asked about the token the client sent.");
        }

        [Test]
        public async Task ValidToken_AdmitsAndUsesTheVouchedIdentityAsTheAccountKey()
        {
            var capture = new VerdictCapture(_gate);
            string loadedAccount = null;
            var loader = new RecordingLoader();
            _gate.PlayerLoader = loader;
            _gate.PlayerJoinLoaded += (_, accountId, _) => loadedAccount = accountId;

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "claimed-name", "claimed-account", ValidToken);
            _validator.Complete(IdentityFor(BackedUserId, "VouchedName"));
            await Task.Delay(50);

            Assert.AreEqual(BackedUserId, loader.AccountId,
                "The save is loaded under the backend's user id, never the account id the client claimed.");
            Assert.AreEqual(BackedUserId, loadedAccount);

            loader.Complete(new PlayerSaveRecord { UserId = BackedUserId, Level = 3 });
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
            Assert.AreEqual(0, _gate.PendingHandshakes,
                "A settled join must be cleared so the reaper never turns it back.");
            Assert.AreEqual(1, capture.Count);
        }

        [Test]
        public async Task ValidToken_WithNoLoader_DecidesOnTheTokenIdentityAlone()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);
            _validator.Complete(IdentityFor("user-1", "Vouched"));
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [Test]
        public async Task InvalidToken_IsRefused()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", "junk-token");
            _validator.Complete(default);
            await Task.Delay(50);

            Assert.AreEqual(false, capture.Approved);
            Assert.AreEqual(0, _gate.PendingHandshakes);
        }

        [Test]
        public async Task ValidatorThrowing_FailsClosed()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);
            _validator.CompleteAsException(new InvalidOperationException("backend down"));
            await Task.Delay(50);

            Assert.AreEqual(false, capture.Approved,
                "An unreachable backend must not open the gate.");
        }

        [Test]
        public async Task ReapedHandshake_InFlightValidation_NeverDecidesTwice()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);
            Assert.AreEqual(1, _gate.PendingHandshakes);

            _gate.Apply(Connection, JoinDecision.Reject(JoinPolicy.ReasonTimedOut));

            Assert.AreEqual(1, capture.Count, "The reaper's rejection is the only verdict so far.");
            Assert.AreEqual(0, _gate.PendingHandshakes);

            _validator.Complete(IdentityFor("user-1", "Vouched"));
            await Task.Delay(50);

            Assert.AreEqual(1, capture.Count, "A vouch landing after the reaper must not admit a ghost.");
        }

        [Test]
        public async Task ReapedHandshake_InFlightRefusal_NeverDecidesTwice()
        {
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);
            _gate.Apply(Connection, JoinDecision.Reject(JoinPolicy.ReasonTimedOut));

            _validator.Complete(default);
            await Task.Delay(50);

            Assert.AreEqual(1, capture.Count, "A refusal, too, must never land on a dead handshake.");
        }

        [Test]
        public async Task BlockedClaimedName_IsNotRejectedWhenTheTokenCarriesAVouchedName()
        {
            // The client claims a name that is on the block list while the backend vouches a clean
            // one: the server's name must win, or a legitimate player could be turned away by a lie.
            _gate.SetSettings(new MMOConnectionSettings(
                7770, 10, 30f, blockedPlayers: new[] { "Intruder" }));
            var capture = new VerdictCapture(_gate);

            _gate.OnRemoteConnection(Connection);
            _gate.HandleJoinRequest(Connection, "Intruder", "acc-1", ValidToken);
            _validator.Complete(IdentityFor("user-1", "VouchedHost"));
            await Task.Delay(50);

            Assert.AreEqual(true, capture.Approved);
        }

        [Test]
        public async Task HandleJoinRequest_WithoutAPendingHandshake_NeverValidatesIntoLaterAdmission()
        {
            var capture = new VerdictCapture(_gate);

            _gate.HandleJoinRequest(Connection, "Bob", "acc-1", ValidToken);
            _validator.Complete(IdentityFor("user-1", "Vouched"));
            await Task.Delay(50);

            Assert.AreEqual(0, capture.Count,
                "The gate only decides joins it parked in OnRemoteConnection.");
        }
    }
}