using System;
using MMO.Social;

namespace MMO.Net
{
    /// <summary>
    /// The orchestration state machine states. Pure and deterministic so the entire wait-for-healthy
    /// flow is EditMode-testable with no clocks, no network, no Unity dependencies.
    /// </summary>
    public enum OrchestrationState
    {
        Requested,
        Starting,
        Healthy,
        Ready,
        Failed,
        TimedOut,
    }

    /// <summary>
    /// Pure state transition logic for raid orchestration. All transitions are driven by explicit
    /// inputs (container health, failure signals, elapsed time) so tests can exhaustively verify
    /// success, failure, and timeout paths without any real time or I/O.
    /// </summary>
    public static class OrchestrationTick
    {
        /// <summary>
        /// Advances the orchestration state given the current inputs.
        /// </summary>
        /// <param name="state">Current state.</param>
        /// <param name="containerHealthy">Whether the container reports healthy.</param>
        /// <param name="failed">Whether a terminal failure was reported (e.g., Docker error).</param>
        /// <param name="elapsedSeconds">Seconds since the request was made.</param>
        /// <param name="timeoutSeconds">Configured timeout after which the attempt times out.</param>
        /// <returns>The next state.</returns>
        public static OrchestrationState Advance(
            OrchestrationState state,
            bool containerHealthy,
            bool failed,
            double elapsedSeconds,
            double timeoutSeconds)
        {
            if (failed)
                return OrchestrationState.Failed;

            // A terminal state never leaves, even if a later input disagrees.
            if (state == OrchestrationState.Failed ||
                state == OrchestrationState.TimedOut ||
                state == OrchestrationState.Ready)
                return state;

            // Healthy wins over the timeout clock: a slow container that finally came up is ready.
            if (containerHealthy)
                return OrchestrationState.Ready;

            if (elapsedSeconds > timeoutSeconds)
                return OrchestrationState.TimedOut;

            if (state == OrchestrationState.Requested)
                return OrchestrationState.Starting;

            return state;
        }
    }

    /// <summary>
    /// Opaque handle returned when an orchestration request is accepted.
    /// </summary>
    public readonly struct OrchestrationHandle
    {
        public readonly string InstanceId;
        public readonly string RaidId;
        public readonly string ZoneTemplateId;
        public readonly string TokenSecret;

        public OrchestrationHandle(string instanceId, string raidId, string zoneTemplateId, string tokenSecret)
        {
            InstanceId = instanceId;
            RaidId = raidId;
            ZoneTemplateId = zoneTemplateId;
            TokenSecret = tokenSecret;
        }

        public bool IsValid => !string.IsNullOrEmpty(InstanceId);

        /// <summary>Converts this handle to a zone registry record once the container is healthy.</summary>
        public ZoneRegistryRecord ToZoneRegistryRecord(string host, ushort port, string serverId, int maxInstances, DateTimeOffset now)
        {
            return ZoneRegistryRecord.Create(serverId, host, port, ZoneTemplateId, maxInstances, now);
        }
    }
}