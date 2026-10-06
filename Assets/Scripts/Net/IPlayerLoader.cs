using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;

namespace MMO.Net
{
    /// <summary>
    /// What the join gate needs in order to defer a verdict during the handshake: a way to fetch the
    /// connecting player's save record before admitting them. The <see cref="MMOConnectionGate"/>
    /// only depends on this seam, so a test can inject a stub and a real server wires
    /// <see cref="Persistence.PlayerPersistenceService"/> behind it.
    /// </summary>
    public interface IPlayerLoader
    {
        /// <summary>Returns null when the account has never been saved.</summary>
        Task<PlayerSaveRecord> LoadAsync(string accountId, CancellationToken ct);
    }
}