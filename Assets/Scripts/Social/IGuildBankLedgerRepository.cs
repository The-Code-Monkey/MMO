using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    public interface IGuildBankLedgerRepository
    {
        Task<GuildBankLedgerRecord> LoadLedgerAsync(string guildId, CancellationToken ct = default);
        Task SaveLedgerAsync(GuildBankLedgerRecord record, CancellationToken ct = default);
    }
}
