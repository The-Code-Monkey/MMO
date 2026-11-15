using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MMO.Net
{
    /// <summary>
    /// Turns the certificate the transport presented into the pin format the rules compare: the
    /// SHA-256 of its DER-encoded SubjectPublicKeyInfo (SPKI).
    ///
    /// <para>
    /// SPKI is pinned rather than the leaf certificate so a re-issued certificate for the same key
    /// pair still matches — the common short-lived-certificate deployment — while a different key
    /// (an attacker's) never does. <c>X509Certificate.GetPublicKey()</c> returns exactly that DER
    /// blob, so no ASN.1 parsing is needed here.
    /// </para>
    ///
    /// <para>
    /// A certificate that cannot be parsed yields false rather than throwing: the caller treats
    /// "no hash" as "not pinned" and refuses the connection, which is the safe direction.
    /// </para>
    /// </summary>
    public static class CertificatePinning
    {
        /// <summary>
        /// Computes the lower-case hex SPKI SHA-256 of a DER certificate. False when the bytes are
        /// empty or not a certificate.
        /// </summary>
        public static bool TrySpkiSha256(byte[] certificateDer, out string pin)
        {
            pin = string.Empty;
            if (certificateDer == null || certificateDer.Length == 0)
                return false;

            try
            {
                using var certificate = new X509Certificate2(certificateDer);
                byte[] spki = certificate.GetPublicKey();
                if (spki == null || spki.Length == 0)
                    return false;

                using SHA256 sha = SHA256.Create();
                pin = ToHex(sha.ComputeHash(spki));
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string ToHex(byte[] hash)
        {
            const string digits = "0123456789abcdef";
            var hex = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                hex[i * 2] = digits[hash[i] >> 4];
                hex[i * 2 + 1] = digits[hash[i] & 0xF];
            }

            return new string(hex);
        }
    }
}
