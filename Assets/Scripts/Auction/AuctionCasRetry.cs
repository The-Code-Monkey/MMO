using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Auction
{
    /// <summary>Bounded CAS retry loop for auction mutations.</summary>
    internal static class AuctionCasRetry
    {
        public static async Task<AuctionResult> RetryAsync(
            int retries, Func<Task<AuctionResult>> attempt, CancellationToken ct)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await Task.Delay(50 * (1 << i), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Auction] Mutation failed: {e.Message}");
                    return AuctionResult.Fail(AuctionRule.BackendUnavailable);
                }
            }

            return AuctionResult.Fail(AuctionRule.Conflict);
        }
    }
}
