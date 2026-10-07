using System.Threading;
using System.Threading.Tasks;

namespace MMO.Persistence
{
    /// <summary>
    /// The backend surface the client Main Menu talks to. One concrete implementation
    /// (<see cref="NakamaApiGateway"/>) talks to Nakama's REST gateway; tests substitute fakes so
    /// the whole login → join story runs without a server.
    /// </summary>
    public interface IApiGateway
    {
        /// <summary>
        /// Signs the player in. Never throws for backend refusals — a refused login is a
        /// <see cref="LoginResult.Fail"/> with a readable message. Returns success only once the
        /// backend has both issued a session token and vouched for the identity behind it.
        /// </summary>
        Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct = default);

        /// <summary>Reads the player's own last-zone record. Returns null when none is stored.</summary>
        Task<LastZoneInfo> LoadLastZoneAsync(string userId, string token, CancellationToken ct = default);

        /// <summary>Stores the player's own last-zone record.</summary>
        Task SaveLastZoneAsync(string userId, string token, LastZoneInfo zone, CancellationToken ct = default);
    }
}