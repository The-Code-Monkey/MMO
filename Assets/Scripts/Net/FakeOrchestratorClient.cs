using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Net
{
    /// <summary>
    /// Fake <see cref="IOrchestratorClient"/> for EditMode tests. Allows controlling the orchestration
    /// outcome (success, failure, timeout) without any real HTTP or Docker calls.
    /// </summary>
    public sealed class FakeOrchestratorClient : IOrchestratorClient
    {
        public bool Enabled { get; set; } = true;

        /// <summary>If true, <see cref="StartAsync"/> throws immediately.</summary>
        public bool ThrowOnStart { get; set; }

        /// <summary>If true, <see cref="WaitHealthyAsync"/> never reports healthy (simulates timeout).</summary>
        public bool NeverHealthy { get; set; }

        /// <summary>If true, <see cref="WaitHealthyAsync"/> reports failed immediately.</summary>
        public bool FailImmediately { get; set; }

        /// <summary>If set, <see cref="WaitHealthyAsync"/> returns this state after one poll.</summary>
        public OrchestrationState? ForcedState { get; set; }

        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public OrchestrationHandle LastHandle { get; private set; }

        public Task<OrchestrationHandle> StartAsync(string raidId, string zoneTemplateId, string tokenSecret, CancellationToken ct)
        {
            if (!Enabled)
                throw new InvalidOperationException("Orchestrator is disabled.");

            if (ThrowOnStart)
                throw new InvalidOperationException("Simulated orchestrator failure.");

            var handle = new OrchestrationHandle("fake-" + Guid.NewGuid().ToString("N"), raidId, zoneTemplateId, tokenSecret);
            Started = true;
            LastHandle = handle;
            return Task.FromResult(handle);
        }

        public Task<OrchestrationState> WaitHealthyAsync(
            OrchestrationHandle handle,
            Func<OrchestrationState, bool, bool, double, double, OrchestrationState> tick,
            double timeoutSeconds,
            CancellationToken ct)
        {
            if (!Enabled)
                return Task.FromResult(OrchestrationState.Failed);

            if (ForcedState.HasValue)
                return Task.FromResult(ForcedState.Value);

            if (FailImmediately)
                return Task.FromResult(OrchestrationState.Failed);

            if (NeverHealthy)
            {
                // The FSM will time out based on elapsedSeconds > timeoutSeconds
                return Task.FromResult(OrchestrationState.Starting);
            }

            return Task.FromResult(OrchestrationState.Ready);
        }

        public Task StopAsync(OrchestrationHandle handle, CancellationToken ct)
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public Task<(string Host, ushort Port)?> GetEndpointAsync(OrchestrationHandle handle, CancellationToken ct)
        {
            if (!Enabled || !handle.IsValid)
                return Task.FromResult<(string, ushort)?>(null);

            return Task.FromResult<(string, ushort)?>(("127.0.0.1", 7777));
        }

        public void Reset()
        {
            Started = false;
            Stopped = false;
            LastHandle = default;
            ThrowOnStart = false;
            NeverHealthy = false;
            FailImmediately = false;
            ForcedState = null;
        }
    }
}