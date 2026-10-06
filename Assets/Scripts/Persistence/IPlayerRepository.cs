using System.Threading;
using System.Threading.Tasks;

namespace MMO.Persistence
{
    /// <summary>
    /// The persistence boundary between gameplay (which captures and applies
    /// <see cref="PlayerSaveRecord"/>s) and whatever backend holds them. The server logic only
    /// ever talks to this interface, so an in-memory fake drives every test offline and a concrete
    /// implementation (Nakama through <see cref="NakamaPlayerRepository"/>, or a file writer) can be
    /// swapped without touching gameplay code.
    /// </summary>
    public interface IPlayerRepository
    {
        /// <summary>Returns null when <paramref name="userId"/> has never been saved.</summary>
        Task<PlayerSaveRecord> LoadAsync(string userId, CancellationToken ct = default);

        Task SaveAsync(PlayerSaveRecord record, CancellationToken ct = default);
    }
}