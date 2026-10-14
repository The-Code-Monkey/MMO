using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Social
{
    /// <summary>
    /// The storage boundary for the zone-server registry (<see cref="ZoneRegistryRecord"/>). Kept
    /// separate from <see cref="ISocialRepository"/> for the same reason
    /// <see cref="IRaidInstanceRepository"/> is: it is a different aggregator with a different
    /// lifecycle, and tests inject a fake with no backend.
    ///
    /// <para>
    /// <see cref="LoadZonesAsync"/> returns every advertised zone so the host selector can choose
    /// among them; a deployment with a single zone can inject
    /// <see cref="SingleZoneRegistryRepository"/> instead and behave exactly as #51 v1 did.
    /// </para>
    /// </summary>
    public interface IZoneRegistryRepository
    {
        /// <summary>Every registered zone (healthy or stale); the caller filters on freshness.</summary>
        Task<List<ZoneRegistryRecord>> LoadZonesAsync(CancellationToken ct = default);

        /// <summary>Creates when <c>record.StorageVersion</c> is empty, otherwise if-matches it.</summary>
        Task SaveZoneAsync(ZoneRegistryRecord record, CancellationToken ct = default);
    }
}
