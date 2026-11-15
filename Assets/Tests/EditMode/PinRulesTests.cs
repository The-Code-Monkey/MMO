using System.Collections.Generic;
using MMO.Net;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// Certificate pinning is pure string arithmetic plus one document check, so every rule that
    /// could lock clients out — malformed sets, single pins, rotation windows, dev loopback — is
    /// pinned here without a certificate or a socket.
    /// </summary>
    public sealed class PinRulesTests
    {
        private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string C = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        [Test]
        public void Normalize_StripsSeparatorsAndLowercases()
        {
            Assert.AreEqual(A, PinRules.Normalize("AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA:AA"));
            Assert.AreEqual(A, PinRules.Normalize(A.ToUpperInvariant()));
        }

        [Test]
        public void IsValidHash_RequiresSixtyFourHexCharacters()
        {
            Assert.IsTrue(PinRules.IsValidHash(A));
            Assert.IsFalse(PinRules.IsValidHash("abc"));
            Assert.IsFalse(PinRules.IsValidHash(new string('z', 64)));
            Assert.IsFalse(PinRules.IsValidHash(null));
        }

        [Test]
        public void IsWellFormed_AcceptsPrimaryAndBackup()
        {
            Assert.IsTrue(PinRules.IsWellFormed(Set(A, B)));
        }

        [Test]
        public void IsWellFormed_RejectsSinglePin()
        {
            Assert.IsFalse(PinRules.IsWellFormed(Set(A, A)),
                "A build with one pin bricks itself on the first certificate rotation.");
        }

        [Test]
        public void IsWellFormed_RejectsEmptyBackend()
        {
            Assert.IsFalse(PinRules.IsWellFormed(Set(string.Empty, B)));
            Assert.IsFalse(PinRules.IsWellFormed(Set(A, string.Empty)));
            Assert.IsFalse(PinRules.IsWellFormed(null));
        }

        [Test]
        public void IsWellFormed_AcceptsPairedServerPins()
        {
            PinSet set = Set(A, B);
            set.ServerPrimarySpkiSha256 = new[] { C, A };
            set.ServerBackupSpkiSha256 = new[] { B, C };

            Assert.IsTrue(PinRules.IsWellFormed(set));
        }

        [Test]
        public void IsWellFormed_RejectsUnpairedOrIdenticalServerPins()
        {
            PinSet missingBackup = Set(A, B);
            missingBackup.ServerPrimarySpkiSha256 = new[] { C };
            Assert.IsFalse(PinRules.IsWellFormed(missingBackup));

            PinSet duplicated = Set(A, B);
            duplicated.ServerPrimarySpkiSha256 = new[] { C };
            duplicated.ServerBackupSpkiSha256 = new[] { C };
            Assert.IsFalse(PinRules.IsWellFormed(duplicated));
        }

        [Test]
        public void Verify_MatchesTheCurrentOrBackupPin()
        {
            Assert.IsTrue(PinRules.Verify(A, A, B), "The primary must be accepted.");
            Assert.IsTrue(PinRules.Verify(B, A, B), "The backup keeps the rotation window open.");
        }

        [Test]
        public void Verify_RejectsUnknownAndEmptyPins()
        {
            Assert.IsFalse(PinRules.Verify(C, A, B));
            Assert.IsFalse(PinRules.Verify(A));
            Assert.IsFalse(PinRules.Verify(A, new string[0]));
            Assert.IsFalse(PinRules.Verify(string.Empty, A, B));
        }

        [Test]
        public void Verify_ScopesBackendAndServerPins()
        {
            PinSet set = Set(A, B);
            set.ServerPrimarySpkiSha256 = new[] { C };
            set.ServerBackupSpkiSha256 = new[] { A };

            Assert.IsTrue(PinRules.Verify(set, PinTarget.Backend, B));
            Assert.IsFalse(PinRules.Verify(set, PinTarget.Backend, C),
                "A game-server certificate must not satisfy the backend pin.");
            Assert.IsTrue(PinRules.Verify(set, PinTarget.GameServer, C));
            Assert.IsFalse(PinRules.Verify(set, PinTarget.GameServer, B));
        }

        [Test]
        public void IsLoopback_ExemptsLocalDevelopers()
        {
            Assert.IsTrue(PinRules.IsLoopback("localhost"));
            Assert.IsTrue(PinRules.IsLoopback("127.0.0.1"));
            Assert.IsTrue(PinRules.IsLoopback("::1"));
            Assert.IsFalse(PinRules.IsLoopback("mmo.example.com"));
        }

        [Test]
        public void ShouldEnforce_OnlyWhenRequiredAndNotLoopback()
        {
            Assert.IsTrue(PinRules.ShouldEnforce(true, "mmo.example.com"));
            Assert.IsFalse(PinRules.ShouldEnforce(false, "mmo.example.com"));
            Assert.IsFalse(PinRules.ShouldEnforce(true, "127.0.0.1"));
        }

        [Test]
        public void FindPinSetViolations_FlagsMalformedRequiredSet()
        {
            PinSet set = Set(A, A);
            set.RequirePinning = true;
            set.Environment = "prod";

            IReadOnlyList<string> violations = PinRules.FindPinSetViolations(new[]
            {
                UnityEngine.JsonUtility.ToJson(set),
            });

            Assert.AreEqual(1, violations.Count);
            StringAssert.Contains("prod", violations[0]);
        }

        [Test]
        public void FindPinSetViolations_AllowsOptionalOrValidSets()
        {
            PinSet optional = Set(A, A);
            optional.RequirePinning = false;

            PinSet prod = Set(A, B);
            prod.RequirePinning = true;

            IReadOnlyList<string> violations = PinRules.FindPinSetViolations(new[]
            {
                UnityEngine.JsonUtility.ToJson(optional),
                UnityEngine.JsonUtility.ToJson(prod),
            });

            Assert.IsEmpty(violations);
        }

        [Test]
        public void FindPinSetViolations_RejectsInvalidJson()
        {
            Assert.AreEqual(1, PinRules.FindPinSetViolations(new[] { "{ not json" }).Count);
            Assert.IsEmpty(PinRules.FindPinSetViolations(null));
        }

        [Test]
        public void CertificatePinning_RejectsBytesThatAreNotACertificate()
        {
            Assert.IsFalse(CertificatePinning.TrySpkiSha256(new byte[] { 1, 2, 3 }, out string pin));
            Assert.AreEqual(string.Empty, pin);
        }

        private static PinSet Set(string primary, string backup)
        {
            return new PinSet
            {
                Environment = "test",
                BackendPrimarySpkiSha256 = primary,
                BackendBackupSpkiSha256 = backup,
            };
        }
    }
}
