using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMO.Net
{
    /// <summary>
    /// Contract for the on-demand raid orchestrator control plane. The Unity zone server never
    /// shells out to Docker; it speaks HTTP through this interface so the orchestration logic
    /// is testable with a fake and swappable with a real HTTP client.
    /// </summary>
    public interface IOrchestratorClient
    {
        /// <summary>Whether orchestration is enabled. When false, callers must fall back to host selection.</summary>
        bool Enabled { get; }

        /// <summary>
        /// Requests a new raid instance container.
        /// </summary>
        /// <param name="raidId">Unique identifier for this raid instance.</param>
        /// <param name="zoneTemplateId">The zone template/build variant to run.</param>
        /// <param name="tokenSecret">Shared secret for zone transfer tokens (passed to the container).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A handle to track the instance.</returns>
        Task<OrchestrationHandle> StartAsync(string raidId, string zoneTemplateId, string tokenSecret, CancellationToken ct);

        /// <summary>
        /// Polls the instance until it reaches <see cref="OrchestrationState.Ready"/> or a terminal state.
        /// </summary>
        /// <param name="handle">Handle returned by <see cref="StartAsync"/>.</param>
        /// <param name="tick">Function that advances the pure FSM given container health, failure, and elapsed time.</param>
        /// <param name="timeoutSeconds">Overall timeout for the orchestration attempt.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The final orchestration state.</returns>
        Task<OrchestrationState> WaitHealthyAsync(OrchestrationHandle handle, Func<OrchestrationState, bool, bool, double, double, OrchestrationState> tick, double timeoutSeconds, CancellationToken ct);

        /// <summary>
        /// Stops and removes the raid instance container.
        /// </summary>
        /// <param name="handle">Handle returned by <see cref="StartAsync"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        Task StopAsync(OrchestrationHandle handle, CancellationToken ct);

        /// <summary>
        /// Gets the host and port for a healthy/ready instance.
        /// </summary>
        /// <param name="handle">Handle returned by <see cref="StartAsync"/>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Tuple of (host, port) or null if not ready.</returns>
        Task<(string Host, ushort Port)?> GetEndpointAsync(OrchestrationHandle handle, CancellationToken ct);
    }
}