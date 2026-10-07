using MMO.Net;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The runtime re-wirer: a baked scene can't serialize the gate's runtime-only token seam, so a
    /// component created by the shared factory reads the serialized
    /// <see cref="MMOConnectionSettings.UseTokenAuth"/> flag back and attaches the real gateway when
    /// it is on. The rule that keeps tests (and operators) honest: it must never overwrite a
    /// validator an owner set explicitly, and must never attach one when the flag is off.
    /// </summary>
    public sealed class TokenAuthBootstrapTests
    {
        private GameObject _managerObject;
        private GameObject _bootstrapObject;
        private MMOConnectionGate _gate;
        private TokenAuthBootstrap _bootstrap;

        /// <summary>A placeholder identity the tests use to confirm an explicitly set validator survives.</summary>
        private sealed class HandWiredValidator : ITokenValidator
        {
            public System.Threading.Tasks.Task<TokenIdentity> ValidateAsync(
                string token, System.Threading.CancellationToken ct)
                => System.Threading.Tasks.Task.FromResult(default(TokenIdentity));
        }

        [SetUp]
        public void SetUp()
        {
            _managerObject = new GameObject("ManagerUnderTest");
            _gate = _managerObject.AddComponent<MMOConnectionGate>();
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f, useTokenAuth: false));

            _bootstrapObject = new GameObject("BootstrapUnderTest");
            _bootstrapObject.transform.SetParent(_managerObject.transform, false);
            _bootstrap = _bootstrapObject.AddComponent<TokenAuthBootstrap>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_managerObject != null)
            {
                Object.DestroyImmediate(_bootstrapObject);
                Object.DestroyImmediate(_managerObject);
            }
        }

        [Test]
        public void Attach_WithTokenAuthOn_WiresTheBackendGateway()
        {
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f, useTokenAuth: true));

            _bootstrap.Attach(_gate);

            Assert.IsInstanceOf<NakamaApiGateway>(_gate.TokenValidator,
                "The flag being on means the gate must authenticate joins against the real backend.");
        }

        [Test]
        public void Attach_WithTokenAuthOff_LeavesTheGateOpen()
        {
            _bootstrap.Attach(_gate);

            Assert.IsNull(_gate.TokenValidator, "A gate baked without the flag must stay the legacy open gate.");
        }

        [Test]
        public void Attach_NeverDowngradesAnExplicitValidator()
        {
            var validator = new HandWiredValidator();
            _gate.TokenValidator = validator;
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f, useTokenAuth: true));

            _bootstrap.Attach(_gate);

            Assert.AreSame(validator, _gate.TokenValidator,
                "A validator wired by an owner (a test double, say) must survive the bootstrap.");
        }

        [Test]
        public void Attach_IsSafeToCallAgain()
        {
            _gate.SetSettings(new MMOConnectionSettings(7770, 10, 30f, useTokenAuth: true));

            _bootstrap.Attach(_gate);
            _bootstrap.Attach(_gate);

            Assert.IsInstanceOf<NakamaApiGateway>(_gate.TokenValidator);
        }

        [Test]
        public void Attach_NullOrMismatchedGate_DoesNotThrow()
        {
            var foreign = new GameObject("Foreign");
            try
            {
                Assert.DoesNotThrow(() => _bootstrap.Attach(null));
                Assert.DoesNotThrow(() => _bootstrap.Attach(foreign.AddComponent<MMOConnectionGate>()));
            }
            finally
            {
                Object.DestroyImmediate(foreign);
            }
        }
    }
}