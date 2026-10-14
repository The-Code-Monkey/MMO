using System;
using System.Threading;
using System.Threading.Tasks;
using MMO.Social;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// High-level orchestration service that ties the on-demand container control plane into the
    /// existing raid allocation flow. When enabled it requests a fresh container, waits for it to
    /// become healthy, registers it as a zone, and then delegates to the standard host selection.
    /// If the orchestrator is unavailable or the container fails to start, it falls back to
    /// <see cref="RaidHostSelection"/> (issue #57) and ultimately to the local zone (issue #51).
    /// </summary>
    public sealed class RaidOrchestrationService
    {
        private readonly IOrchestratorClient _orchestrator;
        private readonly IZoneRegistryRepository _zoneRegistry;
        private readonly Func<PartyRecord, CancellationToken, Task<(RaidRule Rule, RaidInstanceRecord Instance)>> _fallbackAllocate;
        private readonly double _healthTimeoutSeconds;
        private readonly int _maxInstancesPerZone;

        /// <summary>
        /// Creates the orchestration service.
        /// </summary>
        /// <param name="orchestrator">The orchestrator client (real or fake).</param>
        /// <param name="zoneRegistry">Repository for registering the new zone.</param>
        /// <param name="fallbackAllocate">The fallback allocation function (typically <see cref="RaidInstanceService.AllocateAsync"/>).</param>
        /// <param name="healthTimeoutSeconds">How long to wait for the container to become healthy.</param>
        /// <param name="maxInstancesPerZone">Max instances the new zone will report as capacity.</param>
        public RaidOrchestrationService(
            IOrchestratorClient orchestrator,
            IZoneRegistryRepository zoneRegistry,
            Func<PartyRecord, CancellationToken, Task<(RaidRule Rule, RaidInstanceRecord Instance)>> fallbackAllocate,
            double healthTimeoutSeconds = 90.0,
            int maxInstancesPerZone = 1)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _zoneRegistry = zoneRegistry ?? throw new ArgumentNullException(nameof(zoneRegistry));
            _fallbackAllocate = fallbackAllocate ?? throw new ArgumentNullException(nameof(fallbackAllocate));
            _healthTimeoutSeconds = healthTimeoutSeconds;
            _maxInstancesPerZone = maxInstancesPerZone;
        }

        /// <summary>
        /// Attempts to allocate a raid for the party using on-demand orchestration with fallback.
        /// </summary>
        /// <param name="party">The party requesting the raid.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The raid rule outcome and instance record.</returns>
        public async Task<(RaidRule Rule, RaidInstanceRecord Instance)> EnterRaidAsync(PartyRecord party, CancellationToken ct)
        {
            if (party == null)
                return (RaidRule.NoParty, null);

            if (!_orchestrator.Enabled)
            {
                Debug.Log("[RaidOrchestration] Orchestrator disabled, falling back to host selection.");
                return await _fallbackAllocate(party, ct);
            }

            string instanceId = Guid.NewGuid().ToString("N");
            string tokenSecret = GenerateTokenSecret();

            OrchestrationHandle handle;
            try
            {
                handle = await _orchestrator.StartAsync(instanceId, "raid", tokenSecret, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RaidOrchestration] StartAsync failed: {e.Message}. Falling back to host selection.");
                return await _fallbackAllocate(party, ct);
            }

            OrchestrationState state;
            try
            {
                state = await _orchestrator.WaitHealthyAsync(
                    handle,
                    OrchestrationTick.Advance,
                    _healthTimeoutSeconds,
                    ct);
            }
            catch (OperationCanceledException)
            {
                await SafeStopAsync(handle, ct);
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RaidOrchestration] WaitHealthyAsync failed: {e.Message}. Falling back to host selection.");
                await SafeStopAsync(handle, ct);
                return await _fallbackAllocate(party, ct);
            }

            if (state != OrchestrationState.Ready)
            {
                Debug.LogWarning($"[RaidOrchestration] Orchestration ended in state {state}. Stopping container and falling back.");
                await SafeStopAsync(handle, ct);
                return await _fallbackAllocate(party, ct);
            }

            var endpoint = await _orchestrator.GetEndpointAsync(handle, ct);
            if (!endpoint.HasValue)
            {
                Debug.LogError("[RaidOrchestration] Container healthy but no endpoint returned. Falling back.");
                await SafeStopAsync(handle, ct);
                return await _fallbackAllocate(party, ct);
            }

            string host = endpoint.Value.Host;
            ushort port = endpoint.Value.Port;

            var now = DateTimeOffset.UtcNow;
            string serverId = $"orchestrator-{handle.InstanceId}";
            var zoneRecord = handle.ToZoneRegistryRecord(host, port, serverId, _maxInstancesPerZone, now);

            try
            {
                await _zoneRegistry.SaveZoneAsync(zoneRecord);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RaidOrchestration] Failed to register zone: {e.Message}. Stopping container and falling back.");
                await SafeStopAsync(handle, ct);
                return await _fallbackAllocate(party, ct);
            }

            Debug.Log($"[RaidOrchestration] Registered orchestrated zone {serverId} at {host}:{port}. Proceeding with allocation.");
            return await _fallbackAllocate(party, ct);
        }

        /// <summary>
        /// Stops the orchestrated instance when the raid completes (called from teardown path).
        /// </summary>
        public async Task TeardownAsync(string instanceId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(instanceId))
                return;

            var handle = new OrchestrationHandle(instanceId, "", "", "");
            await SafeStopAsync(handle, ct);

            try
            {
                await _zoneRegistry.DeleteZoneAsync($"orchestrator-{instanceId}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RaidOrchestration] Failed to deregister zone for {instanceId}: {e.Message}");
            }
        }

        private static string GenerateTokenSecret()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        private async Task SafeStopAsync(OrchestrationHandle handle, CancellationToken ct)
        {
            try
            {
                await _orchestrator.StopAsync(handle, ct);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RaidOrchestration] Failed to stop container {handle.InstanceId}: {e.Message}");
            }
        }
    }
}