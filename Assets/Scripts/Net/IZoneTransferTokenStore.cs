using System.Threading;
using System.Threading.Tasks;

namespace MMO.Net
{
    /// <summary>
    /// The server-side seam for zone-transfer authorizations: the source server issues one, the
    /// destination server consumes it exactly once. Kept as an interface so the in-memory,
    /// secret-signed implementation can be swapped for a shared cache in a larger deployment, and
    /// so tests can drive the gate with a deterministic double.
    /// </summary>
    public interface IZoneTransferTokenStore
    {
        /// <summary>Mints (and returns) an opaque token string for <paramref name="token"/>.</summary>
        Task<string> IssueAsync(ZoneTransferToken token, CancellationToken ct);

        /// <summary>
        /// Validates a token string and revokes it. Returns null when the string is malformed, the
        /// signature is bad, the token has expired or it has already been used.
        /// </summary>
        Task<ZoneTransferToken> ConsumeAsync(string tokenString, CancellationToken ct);
    }
}
