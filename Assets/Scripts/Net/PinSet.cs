using System;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// The pins a build trusts for its backend and game servers: a primary and a backup SPKI
    /// SHA-256 hash for each, shipped per environment as JSON next to the build.
    ///
    /// <para>
    /// Two valid pins are the whole point. A single pin turns a routine certificate rotation into
    /// a client-wide outage — every shipped build refuses the new certificate at once — so the
    /// backup slot is filled with the <em>next</em> certificate before the server rotates, and the
    /// old one remains trusted during the changeover. <see cref="PinRules.IsWellFormed"/> enforces
    /// that shape and the build guard refuses to ship a set that lacks it.
    /// </para>
    ///
    /// <para>
    /// The payload is a plain serializable class (public fields) so <c>JsonUtility</c> can read it
    /// from a <c>TextAsset</c> without a custom parser. The hashes are hex strings; colons and
    /// upper-case are accepted and normalised by <see cref="PinRules"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PinSet
    {
        /// <summary>Environment this set belongs to, e.g. "prod" or "staging". Documentation only.</summary>
        public string Environment = string.Empty;

        /// <summary>
        /// When true, a non-loopback connection whose certificate is not pinned is refused. A
        /// loopback address is always exempt so local self-signed development keeps working.
        /// </summary>
        public bool RequirePinning;

        /// <summary>SPKI SHA-256 of the backend certificate currently in service.</summary>
        public string BackendPrimarySpkiSha256 = string.Empty;

        /// <summary>SPKI SHA-256 of the backend certificate that will replace the primary.</summary>
        public string BackendBackupSpkiSha256 = string.Empty;

        /// <summary>SPKI SHA-256 of each game-server certificate currently in service.</summary>
        public string[] ServerPrimarySpkiSha256 = Array.Empty<string>();

        /// <summary>SPKI SHA-256 of each game-server certificate that will replace its primary.</summary>
        public string[] ServerBackupSpkiSha256 = Array.Empty<string>();

        /// <summary>
        /// Parses a pin document. A malformed or empty document yields null rather than throwing —
        /// a caller that requires pinning then fails closed at <see cref="PinRules.IsWellFormed"/>,
        /// never with an exception from <c>JsonUtility</c>.
        /// </summary>
        public static PinSet FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<PinSet>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
