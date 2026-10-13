using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    public interface IRaidInstanceRepository
    {
        Task<RaidInstanceRecord> LoadRaidInstanceAsync(string instanceId, CancellationToken ct = default);
        Task SaveRaidInstanceAsync(RaidInstanceRecord record, CancellationToken ct = default);
        Task DeleteRaidInstanceAsync(string instanceId, CancellationToken ct = default);
    }
}
