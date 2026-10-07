using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The join gate with a validator wired, over a real (local) socket. The server must refuse to
    /// admit a client whose session token it cannot vouch for — without ever asking the backend when
    /// there is no token at all — and must admit one whose token it can vouch for, using the
    /// vouched identity rather than anything the client wrote.
    /// </summary>
    public sealed class TokenAuthPlayModeTests
    {
        private sealed class StubValidator : ITokenValidator
        {
            private readonly TokenIdentity _identity;

            public int Calls;

            public StubValidator(TokenIdentity identity) => _identity = identity;

            public Task<TokenIdentity> ValidateAsync(string token, CancellationToken ct)
            {
                Calls++;
                return Task.FromResult(_identity);
            }
        }

        private static readonly TokenIdentity VouchedIdentity = new("user-1", "VouchedHost");

        private NetworkManager _manager;
        private MMOConnectionGate _gate;
        private StubValidator _validator;

        const string ValidToken = "tok-valid";
        const string JunkToken = "junk-token";

        [UnitySetUp]
        public IEnumerator EnsureManagerReady()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;

            _gate = NetworkManagerTestHelper.Gate(_manager);
            _validator = new StubValidator(VouchedIdentity);
            _gate.TokenValidator = _validator;
        }

        [TearDown]
        public void TearDown()
        {
            if (_gate != null)
                _gate.TokenValidator = null;
            JoinRequestClient attached = _manager?.gameObject.GetComponent<JoinRequestClient>();
            if (attached != null)
                attached.SetAuthToken(null);
            NetworkManagerTestHelper.StopIfListening(_manager);
        }

        [UnityTearDown]
        public IEnumerator DrainTeardown()
        {
            yield return null;
        }

        private static IEnumerator WaitUntilSettled(NetworkManager manager)
        {
            bool stopped = false;
            manager.ClientManager.OnClientConnectionState += args =>
            {
                if (args.ConnectionState == LocalConnectionState.Stopped)
                    stopped = true;
            };

            NetworkManagerTestHelper.StartHost(manager);
            // Admission also waits for the persistence loader (attached by the factory), which can
            // take a while when the backend is unreachable; poll generously rather than guess.
            for (int i = 0; i < 600 && !manager.IsClientStarted && !stopped; i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator MissingToken_ConnectionIsRefused_WithoutConsultingTheBackend()
        {
            yield return WaitUntilSettled(_manager);

            Assert.IsFalse(_manager.IsClientStarted, "A client with no token must never be admitted.");
            Assert.AreEqual(0, _validator.Calls,
                "A missing token is refused locally; it must not cost a backend round trip.");
        }

        [UnityTest]
        public IEnumerator ValidToken_HostConnectionIsAdmitted_AndTheBackendWasAsked()
        {
            _manager.gameObject.GetComponent<JoinRequestClient>().SetAuthToken(ValidToken);

            yield return WaitUntilSettled(_manager);

            Assert.IsTrue(_manager.IsClientStarted, "A voucher from the backend must admit the client.");
            Assert.AreEqual(1, _validator.Calls);
        }

        [UnityTest]
        public IEnumerator InvalidToken_ConnectionIsRefused_AfterTheBackendSaysNo()
        {
            var rejecting = new StubValidator(default);
            _gate.TokenValidator = rejecting;
            _manager.gameObject.GetComponent<JoinRequestClient>().SetAuthToken(JunkToken);

            yield return WaitUntilSettled(_manager);

            Assert.IsFalse(_manager.IsClientStarted,
                "A token the backend cannot vouch for must refuse the client.");
            Assert.AreEqual(1, rejecting.Calls, "The backend must have been consulted before the refusal.");
        }

        [UnityTest]
        public IEnumerator HostWithoutTokenAuth_StillJoins_AsTheLegacyOpenGate()
        {
            _gate.TokenValidator = null;

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);

            Assert.IsTrue(_manager.IsHostStarted,
                "With the seam unwired the game keeps its legacy open-join behaviour.");
        }
    }
}