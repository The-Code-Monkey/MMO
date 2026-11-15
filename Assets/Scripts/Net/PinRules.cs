using System;
using System.Collections.Generic;
using System.Text;

namespace MMO.Net
{
    /// <summary>Which certificate role a presented hash is being checked against.</summary>
    public enum PinTarget
    {
        Backend = 0,
        GameServer = 1,
    }

    /// <summary>
    /// Pinning as pure string arithmetic: is the shipped set sane, and does the certificate the
    /// transport presented match one of the pins for its role. The transport supplies the SPKI
    /// hash (<see cref="CertificatePinning"/>); this class only compares it, so the rotation and
    /// well-formedness rules are testable without a socket or a certificate.
    ///
    /// <para>
    /// Hashes are compared case-insensitively and with separators removed, because the same SHA-256
    /// is written as <c>ab:cd:…</c> by one tool and <c>ABCD…</c> by another. Normalising both sides
    /// means an operator's copy-paste style can never cause a false mismatch.
    /// </para>
    /// </summary>
    public static class PinRules
    {
        /// <summary>Length of a SHA-256 hash in hex characters.</summary>
        public const int Sha256HexLength = 64;

        /// <summary>
        /// Lower-cases a hash and drops the separators operators habitually paste
        /// (<c>:</c>, <c>-</c> and whitespace). Null reads as empty.
        /// </summary>
        public static string Normalize(string pin)
        {
            if (string.IsNullOrEmpty(pin))
                return string.Empty;

            var builder = new StringBuilder(pin.Length);
            for (int i = 0; i < pin.Length; i++)
            {
                char c = pin[i];
                if (c == ':' || c == '-' || char.IsWhiteSpace(c))
                    continue;

                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>True when <paramref name="pin"/> is a 64-character hex SHA-256 after normalisation.</summary>
        public static bool IsValidHash(string pin)
        {
            string normalized = Normalize(pin);
            if (normalized.Length != Sha256HexLength)
                return false;

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// True when the set can safely be shipped: the backend has two valid, different pins, and
        /// any game-server pins are paired one-for-one with valid, different backups. An empty
        /// game-server list is allowed (a backend-only client still ships a sane set).
        /// </summary>
        public static bool IsWellFormed(PinSet set)
        {
            if (set == null)
                return false;

            if (!IsValidHash(set.BackendPrimarySpkiSha256) || !IsValidHash(set.BackendBackupSpkiSha256))
                return false;

            if (Normalize(set.BackendPrimarySpkiSha256) == Normalize(set.BackendBackupSpkiSha256))
                return false;

            return AreServerPinsWellFormed(set.ServerPrimarySpkiSha256, set.ServerBackupSpkiSha256);
        }

        /// <summary>
        /// True when <paramref name="presentedSpkiSha256"/> matches one of the declared pins.
        /// An empty pin list never matches, so a set with no pins cannot silently accept anything.
        /// </summary>
        public static bool Verify(string presentedSpkiSha256, params string[] validPins)
        {
            string presented = Normalize(presentedSpkiSha256);
            if (presented.Length == 0 || validPins == null)
                return false;

            for (int i = 0; i < validPins.Length; i++)
            {
                if (Normalize(validPins[i]) == presented)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Verifies a presented hash against the pins for its role. The backend checks the backend
        /// pair; a game server checks the union of the server primary and backup lists, because a
        /// fleet may run several certificates during a rollout.
        /// </summary>
        public static bool Verify(PinSet set, PinTarget target, string presentedSpkiSha256)
        {
            if (set == null)
                return false;

            if (target == PinTarget.Backend)
                return Verify(presentedSpkiSha256, set.BackendPrimarySpkiSha256, set.BackendBackupSpkiSha256);

            return Verify(presentedSpkiSha256, Combine(set.ServerPrimarySpkiSha256, set.ServerBackupSpkiSha256));
        }

        /// <summary>True for a host that never leaves the machine and is exempt from pinning.</summary>
        public static bool IsLoopback(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return true;

            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(host, "::1", StringComparison.Ordinal)
                || string.Equals(host, "[::1]", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a connection should refuse a presentable-unpinned certificate: only when the set
        /// requires pinning and the address is not loopback. A local self-signed dev server is
        /// always allowed, exactly like the CA policy it layers on.
        /// </summary>
        public static bool ShouldEnforce(bool requirePinning, string host)
            => requirePinning && !IsLoopback(host);

        /// <summary>
        /// The build-time verdict on every shipped pin document. A document that is not valid JSON
        /// is always a violation; a document that requires pinning must also be well formed. An
        /// optional document with pinning off is allowed to exist for documentation, so it is not
        /// rejected merely for being incomplete — it is never consulted.
        /// </summary>
        public static IReadOnlyList<string> FindPinSetViolations(IReadOnlyList<string> jsonDocuments)
        {
            var violations = new List<string>();
            if (jsonDocuments == null)
                return violations;

            for (int i = 0; i < jsonDocuments.Count; i++)
            {
                PinSet set = PinSet.FromJson(jsonDocuments[i]);
                if (set == null)
                {
                    violations.Add($"Pin set #{i + 1} is not valid JSON.");
                    continue;
                }

                if (set.RequirePinning && !IsWellFormed(set))
                {
                    violations.Add(
                        $"Pin set '{set.Environment}' requires pinning but is not well formed: " +
                        "the backend needs two valid, different primary/backup SPKI SHA-256 hashes, " +
                        "and any server pins need matched, different backups.");
                }
            }

            return violations;
        }

        private static bool AreServerPinsWellFormed(string[] primary, string[] backup)
        {
            int primaryCount = primary?.Length ?? 0;
            int backupCount = backup?.Length ?? 0;

            if (primaryCount == 0 && backupCount == 0)
                return true;

            if (primaryCount != backupCount)
                return false;

            for (int i = 0; i < primaryCount; i++)
            {
                if (!IsValidHash(primary[i]) || !IsValidHash(backup[i]))
                    return false;

                if (Normalize(primary[i]) == Normalize(backup[i]))
                    return false;
            }

            return true;
        }

        private static string[] Combine(string[] first, string[] second)
        {
            int firstCount = first?.Length ?? 0;
            int secondCount = second?.Length ?? 0;
            if (secondCount == 0)
                return first ?? Array.Empty<string>();
            if (firstCount == 0)
                return second;

            var combined = new string[firstCount + secondCount];
            Array.Copy(first, combined, firstCount);
            Array.Copy(second, 0, combined, firstCount, secondCount);
            return combined;
        }
    }
}
